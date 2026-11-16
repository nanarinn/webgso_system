param([string]$KeyPath = (Join-Path $env:USERPROFILE 'Downloads/gso-web-services-key.pem'))
$ErrorActionPreference = 'Stop'
# Windows PowerShell launched from PowerShell 7 must not load PS7-only modules.
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    $env:PSModulePath = (Join-Path $PSHOME 'Modules') + ';' + (Join-Path $env:ProgramFiles 'WindowsPowerShell/Modules')
}
$serverAddress = '56.69.52.36'
$moduleRoot = $PSScriptRoot
$taskRoot = Split-Path (Split-Path $moduleRoot -Parent) -Parent
$jar = Join-Path $moduleRoot 'target/mmf-mqtt-api-1.0.0.jar'
if (-not (Test-Path -LiteralPath $jar)) { throw 'Build mmf-mqtt-api first.' }
Write-Host 'Setting up the AWS MMF API. Enter the MQTT password, not your GSO login password.'
$securePassword = Read-Host 'MQTT password for bidor-viewer (hidden)' -AsSecureString
$mqttCredential = [pscredential]::new('bidor-viewer', $securePassword)
$tokenCredentialPath = Join-Path $taskRoot '.tools/mmf-aws-api.credential.xml'
if (Test-Path -LiteralPath $tokenCredentialPath) {
    $apiCredential = Import-Clixml -LiteralPath $tokenCredentialPath
} else {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $secureToken = New-Object System.Security.SecureString
    foreach ($character in [Convert]::ToBase64String($bytes).ToCharArray()) { $secureToken.AppendChar($character) }
    $secureToken.MakeReadOnly()
    $apiCredential = [pscredential]::new('https://56.69.52.36/api/mmf/BIDOR/latest', $secureToken)
}
& scp -o BatchMode=yes -o ConnectTimeout=15 -i $KeyPath $jar "ubuntu@${serverAddress}:/home/ubuntu/mmf-mqtt-api-upload.jar"
if ($LASTEXITCODE -ne 0) { throw 'JAR upload failed.' }
& scp -o BatchMode=yes -o ConnectTimeout=15 -i $KeyPath (Join-Path $moduleRoot 'install-aws.py') "ubuntu@${serverAddress}:/home/ubuntu/install-mmf-aws.py"
if ($LASTEXITCODE -ne 0) { throw 'Installer upload failed.' }
$payload = @{password=$mqttCredential.GetNetworkCredential().Password;token=$apiCredential.GetNetworkCredential().Password} | ConvertTo-Json -Compress
try {
    # Pass credentials through SSH stdin, never command arguments or log files.
    $ErrorActionPreference = 'Continue'
    $payload | & ssh -o BatchMode=yes -o ConnectTimeout=15 -i $KeyPath "ubuntu@$serverAddress" 'sudo -n python3 /home/ubuntu/install-mmf-aws.py'
    $installExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($installExit -ne 0) { throw 'AWS MQTT connection not ready; check the diagnostic message above.' }
    $apiCredential | Export-Clixml -LiteralPath $tokenCredentialPath
} finally { $payload=$null; $mqttCredential=$null; $securePassword=$null }
Write-Host 'Restarting local WebGSO with the shared HTTPS MMF API...'
& (Join-Path $taskRoot '.tools/activate-demo-local.ps1')
Write-Host 'Ready: https://56.69.52.36/api/mmf/BIDOR/latest' -ForegroundColor Green
Write-Host 'Paste this URL in MMF API testing on local or AWS WebGSO. Direct anonymous browser requests return 401.'
