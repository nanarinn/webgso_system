# BIDOR MMF MQTT API (Java 21)

This is a separate Java service, not synthetic API data. It subscribes read-only to
`LSS_BIDOR_30MW-NEDA/+/#` on `ssl://56.69.184.157:8883`, using `bidor-viewer`.
Only `Weather_Station_SAE`, `_1`, `_2`, etc. are accepted. PPC/inverter messages are ignored.
It supports a station JSON object containing named measurements, or one measurement per sensor topic.
Source timestamps and units are preserved. The latest measurement is kept for each station/parameter;
duplicates and older messages cannot replace it. Data older than 300 seconds is Stale.
No source data is changed, averaged, fabricated or published back to MQTT.

## Build on this Windows workspace

Open PowerShell in the `java-migration` directory and run:

```powershell
$env:JAVA_HOME = (Resolve-Path '..\.tools\jdk\jdk-21.0.12.1+1').Path
& '..\.tools\maven\apache-maven-3.9.9\bin\mvn.cmd' -f '.\mmf-mqtt-api\pom.xml' verify
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\mmf-mqtt-api\start-mmf.ps1'
```

Enter the MQTT password at the hidden prompt. It is used only by this process and is not saved.
Keep that terminal open while testing; Ctrl+C stops the service.

## Actual local endpoints after startup

- http://127.0.0.1:5190/health
- http://127.0.0.1:5190/api/mmf/BIDOR/latest
- http://127.0.0.1:5190/api/mmf/BIDOR/latest?station=Weather_Station_SAE_3

The service binds to loopback only. These are local URLs, not public AWS URLs.
`503` means MQTT is not connected or there are no readings for the requested station.
Old readings remain visible as Stale, including when MQTT disconnects; mqttConnected indicates transport status.
Do not interpret Fresh as confirmation that the sensor values are physically correct.
The cache is in memory: after restart it waits for retained/new measurements.

TLS certificate and hostname verification stay enabled. For a private CA, obtain the broker's CA
from its administrator and import it into a dedicated Java truststore, then supply
`-Djavax.net.ssl.trustStore=...` when starting Java. Do not disable certificate checks.
The broker certificate must match the IP or a correctly configured broker DNS name.

## Connect to WebGSO

Use the station-specific URL when testing one MMF station. The new MMF API testing page can test it
after the WebGSO server explicitly allows `127.0.0.1` in both `WEBGSO_API_PRIVATE_HOSTS` and
`WEBGSO_API_HTTP_HOSTS`. Append to existing allowlists; do not replace their entries.
Existing network restrictions are intentionally unchanged by this module.
Testing the all-stations response retains each station in JSON; WebGSO's legacy reading database
does not have a dedicated station column, so do not import all stations as one plant's readings
without a station-aware storage migration.

## AWS

Build/run this service on EC2 on loopback port 5190 using a dedicated systemd service and a
root-readable EnvironmentFile containing MQTT credentials. Local and AWS processes need their
own configuration; the Windows prompt does not configure EC2.
Do not expose port 5190 in the security group. For external access, configure an HTTPS reverse
proxy and API authentication first; no public MMF route is installed automatically.
`MMF_API_TOKEN` enables Bearer authentication. Binding to a non-loopback address requires a
token of at least 32 characters. Never put a token/password in an API URL.

Optional environment variables: `MMF_MQTT_URL`, `MMF_MQTT_USERNAME`, `MMF_MQTT_PASSWORD`,
`MMF_MQTT_ROOT`, `MMF_API_PORT`, `MMF_API_BIND`, `MMF_API_TOKEN`.

## Shared AWS HTTPS endpoint for this workspace

After building/deploying the main WebGSO integration, run from `java-migration`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\mmf-mqtt-api\setup-aws.ps1'
```

Enter the broker password at the hidden prompt. The script installs a dedicated loopback-only
`webgso-mmf.service` on the existing EC2 server. It preserves MQTT TLS validation and adds an
HTTPS reverse-proxy route only after MQTT connects. Port 5190 remains private; no security-group
change is needed on the WebGSO server. The broker must allow incoming MQTT connections from
the WebGSO server. The broker password stays in a root-only environment file, and the generated
API token is stored in Windows DPAPI locally and a root-only environment file on AWS.

Use `https://56.69.52.36/api/mmf/BIDOR/latest` in MMF API testing on local or AWS WebGSO.
The local Cloudflare tunnel uses the same local configuration. WebGSO supplies the Bearer token
only for the exact configured HTTPS authority and path; redirects are never followed. Anonymous
browser requests receive HTTP 401. Optional `?station=Weather_Station_SAE_3` selects one station.
Do not paste the token into URLs. This IP must be updated if EC2's public IP changes.

This setup adds no scheduled collection or email and does not modify the WebGSO database.
The service starts automatically after EC2 reboot and reconnects if the broker drops.

### Resume after a broker network timeout

If setup installed the service but reported that MQTT is not ready, ask the broker administrator
to allow TCP port 8883 from `56.69.52.36/32` to broker `56.69.184.157`. They should check the broker
security group, network ACL, host firewall and listener as appropriate. A TCP timeout identifies
a network reachability problem; it does not establish which of these controls is responsible.
Do not open the broker to all internet addresses for this setup.

After the administrator confirms access, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\mmf-mqtt-api\complete-aws.ps1'
```

This reuses the credentials already saved on EC2, waits for MQTT readiness, then configures the
HTTPS route and the local WebGSO credential. No MQTT password needs to be entered again.
Until this succeeds, the shared HTTPS endpoint is not ready for MMF testing.
