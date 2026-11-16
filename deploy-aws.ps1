param(
    [string]$Region = 'ap-southeast-5',
    [string]$InstanceId = 'i-0169c2d5a6674e1ea',
    [string]$Bucket = 'gso-prototype-data-my-k7m4p2'
)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
. (Join-Path $repoRoot 'scripts/sync-common.ps1')
$stateRoot = Join-Path $repoRoot '.tools/design-sync'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
$deployLock = $null
$buildRoot = $null
$uploaded = @()
$commandId = $null
$remoteFinished = $false
try {
    $deployLock = [IO.File]::Open((Join-Path $stateRoot 'deploy.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $aws = Find-SyncAws $repoRoot
    $identity = Invoke-SyncAws $aws @('sts', 'get-caller-identity') $Region
    if ($identity.Account -ne '441798856671') { throw 'Wrong AWS account. Expected 441798856671.' }
    $instance = Invoke-SyncAws $aws @('ec2', 'describe-instances', '--instance-ids', $InstanceId) $Region
    $server = $instance.Reservations[0].Instances[0]
    if ($server.State.Name -ne 'running') { throw 'AWS server is not running.' }
    $deploymentId = [Guid]::NewGuid().ToString('N')
    $buildRoot = Join-Path $stateRoot ('build-' + $deploymentId)
    New-Item -ItemType Directory -Path $buildRoot | Out-Null
    Write-Host '1/3 Snapshot, build and tests...'
    $before = Get-SyncFingerprint $repoRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src') -Destination $buildRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'pom.xml') -Destination $buildRoot
    if ((Get-SyncFingerprint $repoRoot) -ne $before) { throw 'Source changed while snapshotting. The next saved version will be deployed.' }
    $jdkRoot = Join-Path (Split-Path $repoRoot -Parent) '.tools/jdk'
    if (-not $env:JAVA_HOME -or -not (Test-Path (Join-Path $env:JAVA_HOME 'bin/java.exe'))) {
        $env:JAVA_HOME = (Get-ChildItem -LiteralPath $jdkRoot -Directory | Select-Object -First 1).FullName
    }
    $maven = Join-Path (Split-Path $repoRoot -Parent) '.tools/maven/apache-maven-3.9.9/bin/mvn.cmd'
    if (-not (Test-Path -LiteralPath $maven)) { throw 'Cached Maven is missing; install Maven before starting auto-sync.' }
    $cache = Join-Path (Split-Path $repoRoot -Parent) '.tools/m2'
    # Windows PowerShell treats harmless native stderr as an exception under Stop.
    # Capture both streams, then trust the actual Maven exit status.
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $maven "-Dmaven.repo.local=$cache" '-f' (Join-Path $buildRoot 'pom.xml') 'verify' 2>&1 | ForEach-Object { Write-Host "$_" }
        $buildExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($buildExit -ne 0) { throw 'Build/tests failed. AWS was not changed.' }
    if ((Get-SyncFingerprint $repoRoot) -ne $before) { throw 'Source changed during build. Waiting for the latest saved version; AWS was not changed.' }
    $artifact = Join-Path $buildRoot 'target/webgso-system-2.0.0-SNAPSHOT.jar'
    if (-not (Test-Path -LiteralPath $artifact)) { throw 'Built JAR not found.' }
    $prefix = 'webgso-auto-deploy/' + $deploymentId
    Write-Host '2/3 Uploading tested release through S3...'
    $files = @(
        @{ Path = $artifact; Name = 'application.jar' },
        @{ Path = (Join-Path $repoRoot 'scripts/deploy-aws.py'); Name = 'deploy.py' }
    )
    $downloads = @()
    foreach ($file in $files) {
        $key = $prefix + '/' + $file.Name
        $null = Invoke-SyncAws $aws @('s3api', 'put-object', '--bucket', $Bucket, '--key', $key, '--body', $file.Path, '--server-side-encryption', 'AES256') $Region
        $uploaded += $key
        $url = Invoke-SyncAws $aws @('s3', 'presign', ('s3://' + $Bucket + '/' + $key), '--expires-in', '3600') $Region -Text
        $downloads += @{ Name = $file.Name; Url = $url.Trim(); Hash = (Get-SyncFileHash $file.Path).ToLowerInvariant() }
    }
    $stage = '/home/ubuntu/webgso-update-' + $deploymentId + '.jar'
    $helper = '/home/ubuntu/webgso-update-' + $deploymentId + '.py'
    # Presigned URLs stay in a private request file, never in the status/build log.
    $commands = @('set -eu', 'umask 077', "exec 9>/run/lock/webgso-deploy.lock", 'flock -n 9', "trap 'rm -f $stage $helper' EXIT")
    for ($n = 0; $n -lt $downloads.Count; $n++) {
        $target = if ($n -eq 0) { $stage } else { $helper }
        $item = $downloads[$n]
        $commands += "curl --fail --silent --show-error --retry 2 --max-time 180 --proto '=https' '$($item.Url)' -o '$target'"
        $commands += "echo '$($item.Hash)  $target' | sha256sum --check --status"
    }
    $commands += "python3 '$helper' '$stage'"
    $request = @{ InstanceIds = @($InstanceId); DocumentName = 'AWS-RunShellScript'; TimeoutSeconds = 900; Comment = ('WebGSO tested auto-deploy ' + $deploymentId); Parameters = @{ commands = $commands; executionTimeout = @('900') } }
    $requestPath = Join-Path $stateRoot ('request-' + $deploymentId + '.json')
    [IO.File]::WriteAllText($requestPath, ($request | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    try {
        Write-Host '3/3 Deploying through Systems Manager; backing up and checking health...'
        $sent = Invoke-SyncAws $aws @('ssm', 'send-command', '--cli-input-json', ('file://' + $requestPath)) $Region
        $commandId = $sent.Command.CommandId
    } finally { Remove-Item -LiteralPath $requestPath -ErrorAction SilentlyContinue }
    $deadline = (Get-Date).AddMinutes(18)
    do {
        Start-Sleep -Seconds 5
        try { $invocation = Invoke-SyncAws $aws @('ssm', 'get-command-invocation', '--command-id', $commandId, '--instance-id', $InstanceId) $Region }
        catch { if ((Get-Date) -ge $deadline) { throw }; continue }
        if ($invocation.Status -in @('Success', 'Failed', 'Cancelled', 'TimedOut', 'Undeliverable', 'Terminated')) {
            $remoteFinished = $true
            Write-Host $invocation.StandardOutputContent
            if ($invocation.Status -ne 'Success') { throw ('AWS deployment failed: ' + $invocation.Status + '. ' + $invocation.StandardErrorContent) }
            $before | Set-Content -LiteralPath (Join-Path $stateRoot 'last-success.txt')
            Write-Host ('AWS updated and checked: https://' + $server.PublicIpAddress + '/login')
            return
        }
    } while ((Get-Date) -lt $deadline)
    throw ('Deployment status unknown; inspect SSM command ' + $commandId + ' before retrying.')
} finally {
    # Do not delete downloads if a remote command may still be using them.
    if (-not $commandId -or $remoteFinished) {
        foreach ($key in $uploaded) { try { $null = Invoke-SyncAws $aws @('s3api', 'delete-object', '--bucket', $Bucket, '--key', $key) $Region } catch { Write-Warning 'Temporary release cleanup failed.' } }
    }
    if ($buildRoot -and (Test-Path -LiteralPath $buildRoot)) {
        $resolved = [IO.Path]::GetFullPath($buildRoot)
        $allowed = [IO.Path]::GetFullPath($stateRoot) + [IO.Path]::DirectorySeparatorChar
        if ($resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
    if ($deployLock) { $deployLock.Dispose() }
}
