# Shared API sources for local and AWS WebGSO

Use the same source URLs in both installations. A WebGSO login URL identifies the application;
an API endpoint identifies the data source. Local and AWS databases remain separate.

| Dataset | Shared source URL |
| --- | --- |
| Day Ahead | http://56.69.46.237/kmpr/forecast/dayahead |
| Week Ahead | http://56.69.46.237/kmpr/forecast/weekahead |
| 4 Month Ahead | http://56.69.46.237/kmpr/forecast/fourmonthsahead |
| 4 Month Ahead 1 | http://56.69.46.237/kmpr/forecast/fourmonthsahead1 |
| 4 Month Ahead 2 | http://56.69.46.237/kmpr/forecast/fourmonthsahead2 |
| 4 Month Ahead 3 | http://56.69.46.237/kmpr/forecast/fourmonthsahead3 |
| 4 Month Ahead 4 | http://56.69.46.237/kmpr/forecast/fourmonthsahead4 |
| MMF | http://56.69.46.237/lss-bidor/neda/live/mmf |

These public IP hosts are explicitly allowed for HTTP. Private destinations, TLS certificate
validation, redirect blocking and response size restrictions remain enforced.
AWS uses `/etc/systemd/system/webgso.service.d/shared-api-hosts.conf`; local development uses
`application-local.properties` and the workspace local runner.

Checks on 4 November 2026: the MMF endpoint returned HTTP 200 from the laptop and EC2.
The Day Ahead and Week Ahead endpoints at 43.216.231.199 timed out from both machines.
The replacement address is documented in `../../sae-docker/AWS_ENDPOINTS.txt`.
All seven DDQ endpoints at 56.69.46.237 returned HTTP 200 with ForecastResponse JSON
on 4 November 2026. Use the replacement URLs above instead of the old IP.
An allowlist entry permits a request; it cannot bring an unavailable source online.

The separate MQTT bridge at 56.69.52.36 is not yet ready and is not the shared MMF source above.
