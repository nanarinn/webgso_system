param([string]$KeyPath = (Join-Path $env:USERPROFILE 'Downloads/gso-web-services-key.pem'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    $env:PSModulePath = (Join-Path $PSHOME 'Modules') + ';' + (Join-Path $env:ProgramFiles 'WindowsPowerShell/Modules')
}
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& scp -o BatchMode=yes -o ConnectTimeout=15 -i $KeyPath (Join-Path $PSScriptRoot 'install-aws.py') 'ubuntu@56.69.52.36:/home/ubuntu/install-mmf-aws.py'
if ($LASTEXITCODE -ne 0) { throw 'Installer upload failed.' }
$ErrorActionPreference = 'Continue'
& ssh -o BatchMode=yes -o ConnectTimeout=15 -i $KeyPath ubuntu@56.69.52.36 'sudo -n python3 /home/ubuntu/install-mmf-aws.py --resume'
$installExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($installExit -ne 0) { throw 'MQTT is not ready. Ask the broker administrator to permit TCP 8883 from 56.69.52.36/32, then run this script again.' }
$exportScript = @'
import pathlib,shlex
for line in pathlib.Path('/etc/webgso-mmf/client.env').read_text().splitlines():
    key,value=line.split('=',1)
    if key=='WEBGSO_MMF_API_TOKEN':
        print(shlex.split(value)[0])
'@
# Capture only the generated API token for the local client; never print it.
$token = ($exportScript | & ssh -o BatchMode=yes -o ConnectTimeout=15 -i $KeyPath ubuntu@56.69.52.36 'sudo -n python3 -') -join ''
if ($LASTEXITCODE -ne 0 -or $token.Length -lt 32) { throw 'Could not configure the local API client.' }
try {
    $secureToken = New-Object System.Security.SecureString
    foreach ($character in $token.ToCharArray()) { $secureToken.AppendChar($character) }
    $secureToken.MakeReadOnly()
    $credential = [pscredential]::new('https://56.69.52.36/api/mmf/BIDOR/latest', $secureToken)
    $credential | Export-Clixml -LiteralPath (Join-Path $taskRoot '.tools/mmf-aws-api.credential.xml')
} finally { $token=$null }
& (Join-Path $taskRoot '.tools/activate-demo-local.ps1')
Write-Host 'MMF ready for local and AWS WebGSO: https://56.69.52.36/api/mmf/BIDOR/latest' -ForegroundColor Green
