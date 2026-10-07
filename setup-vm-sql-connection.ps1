$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw '.NET 8 SDK was not found.'
}

$sqlUser = 'sblss_host_app'
Write-Host "Connecting as SQL login $sqlUser." -ForegroundColor Cyan
$securePassword = Read-Host 'SQL Server login password' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder['Server'] = '192.168.227.128,1433'
    $builder['Database'] = 'SingleBuyerLssRepository'
    $builder['User ID'] = $sqlUser
    $builder['Password'] = $password
    $builder['Encrypt'] = 'True'
    $builder['TrustServerCertificate'] = 'True'
    $builder['Connect Timeout'] = '30'
    @{ 'ConnectionStrings:DefaultConnection' = $builder.ConnectionString } |
        ConvertTo-Json -Compress |
        & $dotnet user-secrets set --project .\TestingSystem.Web\TestingSystem.Web.csproj
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not save the VM SQL connection in .NET user secrets.'
    }
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    Remove-Variable password -ErrorAction SilentlyContinue
    Remove-Variable securePassword -ErrorAction SilentlyContinue
}

Write-Host 'VM SQL connection saved for the host application. Start with .\run-local-sqlserver.ps1 -VmSqlServer.' -ForegroundColor Green
