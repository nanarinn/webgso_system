param(
    [string]$DatabaseName = "webgso_system",
    [string]$AppUser = "webgso_app",
    [int]$Port = 5432,
    [switch]$SkipDemoData
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

if ($DatabaseName -notmatch '^[a-z][a-z0-9_]{0,62}$') {
    throw "DatabaseName must start with a lowercase letter and contain only lowercase letters, numbers or underscores."
}
if ($AppUser -notmatch '^[a-z][a-z0-9_]{0,62}$') {
    throw "AppUser must start with a lowercase letter and contain only lowercase letters, numbers or underscores."
}

$postgresRoot = Get-ChildItem -LiteralPath "C:\Program Files\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
    Sort-Object { [int](($_.Name -split '\.')[0]) } -Descending |
    Select-Object -First 1
if (-not $postgresRoot) {
    throw "PostgreSQL Server was not found under C:\Program Files\PostgreSQL. Install PostgreSQL Server first."
}

$psql = Join-Path $postgresRoot.FullName "bin\psql.exe"
$createdb = Join-Path $postgresRoot.FullName "bin\createdb.exe"
if (-not (Test-Path -LiteralPath $psql) -or -not (Test-Path -LiteralPath $createdb)) {
    throw "PostgreSQL command-line tools were not found in $($postgresRoot.FullName)\bin."
}

$service = Get-Service -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^postgresql-' } |
    Select-Object -First 1
if (-not $service) { throw "The PostgreSQL Windows service was not found." }
if ($service.Status -ne "Running") {
    throw "PostgreSQL service '$($service.Name)' is not running. Start it as Administrator, then retry."
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { throw ".NET 8 SDK was not found." }

$securePassword = Read-Host "Enter the PostgreSQL 'postgres' password" -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $adminPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
}

$randomBytes = [byte[]]::new(32)
$randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomGenerator.GetBytes($randomBytes)
}
finally {
    $randomGenerator.Dispose()
}
$appPassword = ([BitConverter]::ToString($randomBytes) -replace '-', '').ToLowerInvariant()
$env:PGPASSWORD = $adminPassword
try {
    Write-Host "Checking PostgreSQL administrator access..." -ForegroundColor Cyan
    & $psql --host=localhost --port=$Port --username=postgres --dbname=postgres `
        --no-password --set=ON_ERROR_STOP=1 --command="SELECT version();" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL administrator login failed. Check the postgres password." }

    $roleExists = (@(& $psql --host=localhost --port=$Port --username=postgres --dbname=postgres `
        --no-password --tuples-only --no-align --command="SELECT 1 FROM pg_roles WHERE rolname = '$AppUser';") -join '').Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect PostgreSQL application roles." }
    if ($roleExists -eq "1") {
        $roleSql = 'ALTER ROLE "{0}" WITH LOGIN PASSWORD ''{1}'';' -f $AppUser, $appPassword
        & $psql --host=localhost --port=$Port --username=postgres --dbname=postgres --no-password `
            --set=ON_ERROR_STOP=1 --command=$roleSql
    }
    else {
        $roleSql = 'CREATE ROLE "{0}" WITH LOGIN PASSWORD ''{1}'';' -f $AppUser, $appPassword
        & $psql --host=localhost --port=$Port --username=postgres --dbname=postgres --no-password `
            --set=ON_ERROR_STOP=1 --command=$roleSql
    }
    if ($LASTEXITCODE -ne 0) { throw "Unable to create or update PostgreSQL application role." }

    $databaseExists = (@(& $psql --host=localhost --port=$Port --username=postgres --dbname=postgres `
        --no-password --tuples-only --no-align --command="SELECT 1 FROM pg_database WHERE datname = '$DatabaseName';") -join '').Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect PostgreSQL databases." }
    if ($databaseExists -ne "1") {
        & $createdb --host=localhost --port=$Port --username=postgres --no-password `
            --owner=$AppUser --encoding=UTF8 $DatabaseName
        if ($LASTEXITCODE -ne 0) { throw "Unable to create PostgreSQL database '$DatabaseName'." }
    }
    else {
        $databaseSql = 'ALTER DATABASE "{0}" OWNER TO "{1}";' -f $DatabaseName, $AppUser
        & $psql --host=localhost --port=$Port --username=postgres --dbname=postgres --no-password `
            --set=ON_ERROR_STOP=1 --command=$databaseSql
    }
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    $adminPassword = $null
}

$connectionString = "Host=localhost;Port=$Port;Database=$DatabaseName;Username=$AppUser;Password=$appPassword;Include Error Detail=true"
& $dotnet user-secrets set "ConnectionStrings:DefaultConnection" $connectionString `
    --project .\TestingSystem.Web\TestingSystem.Web.csproj | Out-Null
& $dotnet user-secrets set "UiOnly:Enabled" "false" `
    --project .\TestingSystem.Web\TestingSystem.Web.csproj | Out-Null

$env:UiOnly__Enabled = "false"
$env:ConnectionStrings__DefaultConnection = $connectionString
try {
    Write-Host "Creating and synchronising the PostgreSQL schema..." -ForegroundColor Cyan
    & $dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj --no-launch-profile --no-restore -- --update-database
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL schema synchronisation failed." }

    if (-not $SkipDemoData) {
        Write-Host "Adding linked demo plants, APIs, schedules, reports and audit data..." -ForegroundColor Cyan
        & $dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj --no-launch-profile --no-restore -- --seed-dummy-api
        if ($LASTEXITCODE -ne 0) { throw "PostgreSQL demo data seed failed." }
    }
}
finally {
    Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    Remove-Item Env:UiOnly__Enabled -ErrorAction SilentlyContinue
    $connectionString = $null
    $appPassword = $null
}

Write-Host "PostgreSQL setup completed." -ForegroundColor Green
Write-Host "Database: $DatabaseName" -ForegroundColor Green
Write-Host "Application role: $AppUser" -ForegroundColor Green
Write-Host "The generated application password is stored in .NET user-secrets, not in Git." -ForegroundColor Yellow
Write-Host "Open pgAdmin and refresh Databases to see '$DatabaseName'." -ForegroundColor Cyan
Write-Host "Run .\setup-local-admin.cmd once to create the first local Super Admin account." -ForegroundColor Cyan
Write-Host "Start the connected system with .\run-postgresql.cmd" -ForegroundColor Cyan
