param(
    [switch]$StartApp
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$gitExe = if ($gitCommand) { $gitCommand.Source } else { $null }
if (-not $gitExe) {
    $gitCandidates = @(
        (Join-Path $env:ProgramFiles 'Git\cmd\git.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe'),
        (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\cmd\git.exe')
    )
    $gitExe = $gitCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $gitExe) { throw 'Git was not found. Install Git for Windows or open this script from the Codex terminal.' }
Write-Host "Using Git at $gitExe" -ForegroundColor Cyan

function Run-Git {
    param([string[]]$Arguments)
    & $gitExe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')"
    }
}

$branch = (& $gitExe branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') {
    throw "Switch to the local main branch before updating. Current branch: $branch"
}

$remote = (& $gitExe remote get-url origin).Trim()
if ($LASTEXITCODE -ne 0 -or $remote -notmatch '^https://github\.com/jamal503/sblss-system(?:\.git)?$') {
    throw "The origin remote is not the expected SBLSS GitHub repository: $remote"
}

$changes = @(& $gitExe status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect local Git changes.' }
$savedStash = $null
if ($changes.Count -gt 0) {
    Write-Host 'Saving local changes in a Git stash before pulling...' -ForegroundColor Cyan
    Run-Git -Arguments @('stash', 'push', '--include-untracked', '-m', "SBLSS local setup before pull $(Get-Date -Format yyyyMMdd-HHmmss)")
    $savedStash = (& $gitExe rev-parse refs/stash).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $savedStash) { throw 'Could not identify the saved local changes.' }
}

try {
    Write-Host 'Pulling the latest code from GitHub origin/main...' -ForegroundColor Cyan
    Run-Git -Arguments @('-c', 'core.pager=cat', 'pull', '--ff-only', 'origin', 'main')
}
catch {
    if ($savedStash) {
        Write-Warning 'Git pull failed. Restoring the saved local changes.'
        & $gitExe stash apply $savedStash
        if ($LASTEXITCODE -ne 0) {
            throw "Pull failed and local changes need manual recovery from stash $savedStash."
        }
    }
    throw
}

if ($savedStash) {
    Write-Host 'Restoring local VM and LAN setup changes...' -ForegroundColor Cyan
    & $gitExe stash apply $savedStash
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub code was pulled, but local changes conflict. Resolve the conflicts or recover from stash $savedStash. Database settings were not changed."
    }
    Write-Host "Local changes restored. Safety stash retained: $savedStash" -ForegroundColor Yellow
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) { throw '.NET 8 SDK was not found.' }
$project = Join-Path $projectRoot 'TestingSystem.Web\TestingSystem.Web.csproj'

function Get-VmConnection {
    $line = @(& $dotnet user-secrets list --project $project 2>$null |
        Where-Object { $_ -match '^ConnectionStrings:DefaultConnection\s*=' }) | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line -split '\s=\s', 2)[1]
}

$connectionString = Get-VmConnection
$needsSetup = -not $connectionString
if ($connectionString) {
    try {
        $settings = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
        $needsSetup = $settings.DataSource -ne '192.168.227.128,1433' -or
            $settings.InitialCatalog -ne 'SingleBuyerLssRepository'
    }
    catch { $needsSetup = $true }
}

if ($needsSetup) {
    $setupScript = Join-Path $projectRoot 'setup-vm-sql-connection.ps1'
    if (-not (Test-Path -LiteralPath $setupScript)) {
        throw 'VM setup script is missing. Restore setup-vm-sql-connection.ps1, then retry.'
    }
    Write-Host 'VM SQL connection needs setup. Enter the SQL login password when prompted.' -ForegroundColor Yellow
    & $setupScript
    if (-not $?) { throw 'VM SQL connection setup failed.' }
    $connectionString = Get-VmConnection
}

$database = [System.Data.SqlClient.SqlConnection]::new($connectionString)
try {
    $database.Open()
    $command = $database.CreateCommand()
    $command.CommandText = 'SELECT DB_NAME()'
    $databaseName = [string]$command.ExecuteScalar()
    if ($databaseName -ne 'SingleBuyerLssRepository') { throw "Connected to unexpected database: $databaseName" }
}
finally { $database.Dispose() }

Write-Host 'GitHub code updated and VM SQL connection verified.' -ForegroundColor Green
Write-Host 'No Git commit or push was performed.' -ForegroundColor Green

if ($StartApp) {
    & (Join-Path $projectRoot 'run-local-sqlserver.ps1') -VmSqlServer -Share -ShareAddress '192.168.0.173' -ShareEntra -ShareHostName 'sblss-lan.local'
}
else {
    Write-Host 'To start the shared website, run:' -ForegroundColor Cyan
    Write-Host 'powershell -ExecutionPolicy Bypass -File .\run-local-sqlserver.ps1 -VmSqlServer -Share -ShareAddress 192.168.0.173 -ShareEntra -ShareHostName sblss-lan.local'
}
