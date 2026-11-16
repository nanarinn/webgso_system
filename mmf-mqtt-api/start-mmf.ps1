$ErrorActionPreference = 'Stop'
$moduleRoot = $PSScriptRoot
$workspaceRoot = Split-Path (Split-Path $moduleRoot -Parent) -Parent
$jdkPath = Join-Path $workspaceRoot '.tools/jdk/jdk-21.0.12.1+1'
$javaExe = if (Test-Path (Join-Path $jdkPath 'bin/java.exe')) { Join-Path $jdkPath 'bin/java.exe' } else { 'java' }
$jarPath = Join-Path $moduleRoot 'target/mmf-mqtt-api-1.0.0.jar'
if (!(Test-Path $jarPath)) { throw 'Build the MMF service first. See README.md.' }
if (!$env:MMF_MQTT_PASSWORD) {
    $secureMqttPassword = Read-Host 'MQTT password for bidor-viewer (hidden)' -AsSecureString
    $mqttCredential = New-Object System.Management.Automation.PSCredential('bidor-viewer', $secureMqttPassword)
    $env:MMF_MQTT_PASSWORD = $mqttCredential.GetNetworkCredential().Password
}
try {
    # Match the working WebGSO Windows runner's TCP selector fallback.
    $javaOptions = @('-Djdk.net.unixdomain.tmpdir=.java-no-unix-sockets')
    Push-Location $moduleRoot
    try { & $javaExe @javaOptions -jar $jarPath }
    finally { Pop-Location }
} finally {
    Remove-Item Env:MMF_MQTT_PASSWORD -ErrorAction SilentlyContinue
}
