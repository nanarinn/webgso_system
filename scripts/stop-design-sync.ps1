$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$pidPath = Join-Path $repoRoot '.tools/design-sync/watch.pid'
if (Test-Path -LiteralPath $pidPath) {
    $watchId = [int](Get-Content -LiteralPath $pidPath -Raw).Trim()
    $process = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $watchId) -ErrorAction SilentlyContinue
    $watchScript = Join-Path $repoRoot 'watch-design-aws.ps1'
    if ($process -and $process.CommandLine -like ('*' + $watchScript + '*')) {
        # Wait for an in-progress deployment to finish before stopping the watcher.
        New-Item -ItemType File -Path (Join-Path $repoRoot '.tools/design-sync/stop.request') -Force | Out-Null
        Write-Host 'Stop requested. An in-progress deployment will finish first.'
        exit 0
    }
}
Write-Host 'Design sync is not running.'
