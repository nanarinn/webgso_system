param(
    [switch]$Demo,
    [switch]$Production,
    [int]$Port = 5186,
    [string]$PublicBaseUrl,
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
Set-Location -LiteralPath $projectRoot

# Explicit trusted origin for temporary HTTPS phone-verification tests.
if ($PublicBaseUrl) {
    $publicOrigin = [Uri]$PublicBaseUrl
    if (-not $publicOrigin.IsAbsoluteUri -or $publicOrigin.Scheme -ne 'https' -or
        -not $publicOrigin.Host -or $publicOrigin.UserInfo -or $publicOrigin.Query -or
        $publicOrigin.Fragment -or $publicOrigin.AbsolutePath -ne '/') {
        throw 'PublicBaseUrl must be an HTTPS origin without credentials, path, query or fragment.'
    }
    $env:WEBGSO_PUBLIC_BASE_URL = $publicOrigin.GetLeftPart([UriPartial]::Authority)
    $env:WEBGSO_SECURE_COOKIE = 'true'
}

# Google OAuth uses a separate Windows-encrypted credential, not the Gmail App Password.
$googleCredentialPath = Join-Path $env:LOCALAPPDATA 'GSO Web Services\google.oauth.xml'
if (-not $Demo -and -not $env:GOOGLE_CLIENT_SECRET -and (Test-Path -LiteralPath $googleCredentialPath)) {
    $googleCredential = Import-Clixml -LiteralPath $googleCredentialPath
    if ($googleCredential -isnot [System.Management.Automation.PSCredential]) { throw 'Invalid local Google OAuth credential file.' }
    $env:GOOGLE_CLIENT_ID = $googleCredential.UserName
    $env:GOOGLE_CLIENT_SECRET = $googleCredential.GetNetworkCredential().Password
    if (-not $env:GOOGLE_LOGIN_ENABLED) { $env:GOOGLE_LOGIN_ENABLED = 'true' }
    $googleCredential = $null
}

# Windows DPAPI-encrypted credential, entered locally and never stored in Git.
$gmailCredentialPath = Join-Path $env:LOCALAPPDATA 'GSO Web Services\gmail.credential.xml'
if (-not $Demo -and -not $env:SMTP_PASSWORD -and (Test-Path -LiteralPath $gmailCredentialPath)) {
    $gmailCredential = Import-Clixml -LiteralPath $gmailCredentialPath
    if ($gmailCredential -isnot [System.Management.Automation.PSCredential]) { throw 'Invalid local Gmail credential file.' }
    $env:SMTP_HOST = 'smtp.gmail.com'
    $env:SMTP_PORT = '587'
    $env:SMTP_USERNAME = $gmailCredential.UserName
    $env:SMTP_PASSWORD = $gmailCredential.GetNetworkCredential().Password
    $env:WEBGSO_MAIL_FROM = $gmailCredential.UserName
    $env:WEBGSO_MAIL_ENABLED = 'true'
    $env:WEBGSO_API_ALERTS_ENABLED = 'true'
    $env:WEBGSO_API_ALERTS_RECIPIENT = $gmailCredential.UserName
    $gmailCredential = $null
}

# Local daily PostgreSQL backups requested by the user; demo remains isolated.
if (-not $Demo -and -not $env:WEBGSO_BACKUP_ENABLED) { $env:WEBGSO_BACKUP_ENABLED = 'true' }

# Credentials remain in the environment or the existing local secret store.
# Reading the former connection string allows migration without a password reset;
# .NET is not required to run the Java application.
if (-not $Demo -and -not $env:WEBGSO_DB_PASSWORD) {
    $legacySecret = Join-Path $env:APPDATA 'Microsoft\UserSecrets\sblss-system-local\secrets.json'
    if (Test-Path -LiteralPath $legacySecret) {
        $settings = Get-Content -LiteralPath $legacySecret -Raw | ConvertFrom-Json
        $connectionString = $settings.'ConnectionStrings:DefaultConnection'
        if ($connectionString) {
            $connection = New-Object System.Data.Common.DbConnectionStringBuilder
            $connection.set_ConnectionString($connectionString)
            if ($connection.ContainsKey('Host')) {
                $dbHost = $connection['Host']
                $dbPort = if ($connection.ContainsKey('Port')) { $connection['Port'] } else { '5432' }
                $dbName = $connection['Database']
                if (-not $env:WEBGSO_DB_URL) { $env:WEBGSO_DB_URL = "jdbc:postgresql://${dbHost}:${dbPort}/${dbName}" }
                if (-not $env:WEBGSO_DB_USER) { $env:WEBGSO_DB_USER = $connection['Username'] }
                $env:WEBGSO_DB_PASSWORD = $connection['Password']
            }
        }
    }
}
if (-not $env:JAVA_HOME -or -not (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin\java.exe'))) {
    $env:JAVA_HOME = $null
    $portableRoot = Join-Path $projectRoot '.java-runtime\jdk'
    if (-not (Test-Path -LiteralPath $portableRoot)) { $portableRoot = Join-Path (Split-Path $projectRoot -Parent) '.tools\jdk' }
    if (Test-Path -LiteralPath $portableRoot) { $env:JAVA_HOME = (Get-ChildItem -LiteralPath $portableRoot -Directory | Select-Object -First 1).FullName }
}
$java = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\java.exe' } else { (Get-Command java -ErrorAction Stop).Source }
$jar = Join-Path $projectRoot 'target\webgso-system-2.0.0-SNAPSHOT.jar'
if ($Build -or -not (Test-Path -LiteralPath $jar)) {
    & (Join-Path $projectRoot 'mvnw.cmd') verify
    if ($LASTEXITCODE -ne 0) { throw 'Java build or verification failed.' }
}
$arguments = @('-jar', $jar, "--server.port=$Port")
# Some packaged Windows environments cannot connect Java's AF_UNIX wakeup pipe.
# A deliberately absent socket directory makes the JDK use its TCP fallback.
if ($env:WEBGSO_WINDOWS_TCP_FALLBACK -ne 'false') {
    $absentSocketDirectory = Join-Path $projectRoot '.java-no-unix-sockets'
    $arguments = @("-Djdk.net.unixdomain.tmpdir=$absentSocketDirectory") + $arguments
}
if ($Demo) { $arguments += '--spring.profiles.active=demo' }
elseif (-not $Production) { $arguments += '--spring.profiles.active=local' }
Write-Host "Starting WEBGSO Java at http://localhost:$Port" -ForegroundColor Green
& $java @arguments
exit $LASTEXITCODE
