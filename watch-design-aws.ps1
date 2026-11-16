param([int]$QuietSeconds = 20, [switch]$Once)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
. (Join-Path $repoRoot 'scripts/sync-common.ps1')
$stateRoot = Join-Path $repoRoot '.tools/design-sync'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
$lockStream = $null
try { $lockStream = [IO.File]::Open((Join-Path $stateRoot 'watch.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
catch { throw 'Auto-sync is already running.' }
function Write-SyncStatus([string]$Message) {
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $Message
    Add-Content -LiteralPath (Join-Path $stateRoot 'status.log') -Value $line
    Write-Host $line
}
try {
    $stopPath = Join-Path $stateRoot 'stop.request'
    Remove-Item -LiteralPath $stopPath -ErrorAction SilentlyContinue
    $PID | Set-Content -LiteralPath (Join-Path $stateRoot 'watch.pid')
    $lastSuccessPath = Join-Path $stateRoot 'last-success.txt'
    $deployed = if (Test-Path -LiteralPath $lastSuccessPath) { (Get-Content -LiteralPath $lastSuccessPath -Raw).Trim() } else { '' }
    $candidate = Get-SyncFingerprint $repoRoot
    $changedAt = Get-Date
    $attempted = ''
    Write-SyncStatus 'Watching coding and design. After 20 quiet seconds: verify, S3 upload, Systems Manager deploy. No SSH.'
    do {
        if (Test-Path -LiteralPath $stopPath) { Write-SyncStatus 'Auto-sync stopped.'; break }
        try { $fingerprint = Get-SyncFingerprint $repoRoot } catch { Start-Sleep -Seconds 3; continue }
        if ($fingerprint -ne $candidate) { $candidate = $fingerprint; $changedAt = Get-Date }
        if ($candidate -ne $deployed -and $candidate -ne $attempted -and ($Once -or ((Get-Date) - $changedAt).TotalSeconds -ge $QuietSeconds)) {
            $attempted = $candidate
            Write-SyncStatus 'Testing and deploying saved coding/design changes...'
            try {
                & (Join-Path $repoRoot 'deploy-aws.ps1') *> (Join-Path $stateRoot 'last-deploy.log')
                $deployed = (Get-Content -LiteralPath $lastSuccessPath -Raw).Trim()
                Write-SyncStatus 'AWS updated successfully. Refresh your AWS browser page.'
            } catch {
                Write-SyncStatus ('Sync failed; AWS retains the last healthy release. See .tools/design-sync/last-deploy.log. ' + $_.Exception.Message)
                if ($Once) { throw }
            }
        }
        if (-not $Once) { Start-Sleep -Seconds 3 }
    } while (-not $Once)
} finally {
    Remove-Item -LiteralPath (Join-Path $stateRoot 'watch.pid') -ErrorAction SilentlyContinue
    if ($lockStream) { $lockStream.Dispose() }
}
