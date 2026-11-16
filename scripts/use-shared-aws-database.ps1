$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$taskRoot = Split-Path $repoRoot -Parent
. (Join-Path $PSScriptRoot 'sync-common.ps1')
$credentialPath = Join-Path $taskRoot '.tools/shared-aws-database.credential.xml'
if (-not (Test-Path -LiteralPath $credentialPath)) { throw 'The encrypted AWS database credential is missing.' }
$aws = Find-SyncAws $repoRoot
$identity = Invoke-SyncAws $aws @('sts', 'get-caller-identity') 'ap-southeast-5'
if ($identity.Account -ne '441798856671') { throw 'Wrong AWS account; expected 441798856671.' }
$pluginDirectory = Join-Path $taskRoot '.tools/ssm-plugin/portable/package/bin'
if (-not (Test-Path -LiteralPath (Join-Path $pluginDirectory 'session-manager-plugin.exe'))) { throw 'AWS Session Manager plugin is missing.' }
$env:PATH = $pluginDirectory + [IO.Path]::PathSeparator + $env:PATH
$stateRoot = Join-Path $repoRoot '.tools/shared-database'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
$tunnelPort = 15439
$listener = Get-NetTCPConnection -LocalPort $tunnelPort -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $owner = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $listener.OwningProcess)
    if ($owner.Name -ne 'session-manager-plugin.exe' -or
        $owner.ExecutablePath -ne (Join-Path $pluginDirectory 'session-manager-plugin.exe') -or
        $owner.CommandLine -notlike '*i-0169c2d5a6674e1ea*' -or
        $listener.LocalAddress -notin @('127.0.0.1', '::1')) {
        throw 'Port 15439 belongs to an unexpected process. It has not been stopped.'
    }
} else {
    $parameterPath = Join-Path $stateRoot 'port-forward.json'
    [IO.File]::WriteAllText($parameterPath, '{"portNumber":["5432"],"localPortNumber":["15439"]}', (New-Object Text.UTF8Encoding($false)))
    $arguments = 'ssm start-session --target i-0169c2d5a6674e1ea --document-name AWS-StartPortForwardingSession --parameters "file://' + $parameterPath + '" --region ap-southeast-5 --no-cli-pager'
    $tunnel = Start-Process -FilePath $aws -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stateRoot 'tunnel.out.log') -RedirectStandardError (Join-Path $stateRoot 'tunnel.err.log')
    $tunnel.Id | Set-Content -LiteralPath (Join-Path $stateRoot 'tunnel.pid')
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        if ($tunnel.HasExited) { throw 'SSM database tunnel failed. See .tools/shared-database/tunnel.err.log.' }
        $listener = Get-NetTCPConnection -LocalPort $tunnelPort -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($listener) { break }
    }
    if (-not $listener -or $listener.LocalAddress -notin @('127.0.0.1', '::1')) { throw 'The private SSM database tunnel did not become ready.' }
}
# Resolve DPAPI only into this process; never print or save plaintext credentials.
$credential = Import-Clixml -LiteralPath $credentialPath
if ($credential -isnot [pscredential]) { throw 'Invalid encrypted database credential.' }
$env:WEBGSO_DB_URL = 'jdbc:postgresql://127.0.0.1:15439/webgso?ApplicationName=WebGSO-local-shared&connectTimeout=15&socketTimeout=60'
$env:WEBGSO_DB_USER = $credential.UserName
$env:WEBGSO_DB_PASSWORD = $credential.GetNetworkCredential().Password
$credential = $null
$env:WEBGSO_SHARED_DATABASE = 'true'
$env:WEBGSO_WORKERS_ENABLED = 'false'
$env:WEBGSO_BACKUP_ENABLED = 'false'
$env:WEBGSO_MAIL_ENABLED = 'false'
$env:WEBGSO_MAIL_DISPATCH_ENABLED = 'false'
$env:WEBGSO_API_ALERTS_ENABLED = 'false'
$env:WEBGSO_API_DIGEST_ENABLED = 'false'
$env:WEBGSO_ADMIN_USERNAME = ''
$env:WEBGSO_ADMIN_EMAIL = ''
$env:WEBGSO_ADMIN_PASSWORD = ''
$env:WEBGSO_DEMO_PASSWORD = ''
Write-Host 'Database: AWS webgso through Systems Manager on private loopback port 15439. No SSH.'
