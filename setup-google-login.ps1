$ErrorActionPreference = 'Stop'
Write-Host 'GSO Web Services - Google sign-in setup' -ForegroundColor Cyan
Write-Host 'Create a Google OAuth Web application client first.'
Write-Host 'Authorized redirect URI: http://localhost:5187/login/oauth2/code/google'
Write-Host 'Enter the OAuth Client ID and Client secret. Do not use your Gmail password.'
$googleClientId = (Read-Host 'Google OAuth Client ID').Trim()
if ($googleClientId -notmatch '^[a-zA-Z0-9._-]+\.apps\.googleusercontent\.com$') { throw 'Enter a valid Google OAuth Client ID.' }
$googleSecret = Read-Host 'Google OAuth Client secret (hidden)' -AsSecureString
if ($googleSecret.Length -eq 0) { throw 'Client secret is required.' }
$googleCredential = New-Object System.Management.Automation.PSCredential($googleClientId, $googleSecret)
$googleDirectory = Join-Path $env:LOCALAPPDATA 'GSO Web Services'
New-Item -ItemType Directory -Path $googleDirectory -Force | Out-Null
$googleCredential | Export-Clixml -LiteralPath (Join-Path $googleDirectory 'google.oauth.xml')
Write-Host 'Saved in the Windows encrypted local credential store. Restart GSO using run-java.ps1 -Port 5187.' -ForegroundColor Green
