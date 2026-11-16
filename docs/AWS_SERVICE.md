# AWS application service

WebGSO runs as `webgso.service` under the `ubuntu` account. It is enabled for
machine startup and restarts after an application failure (`Restart=on-failure`).
The working directory is `/home/ubuntu/webgso_system` and the JAR remains in
`target/webgso-system-2.0.0-SNAPSHOT.jar`.

The installed unit is `/etc/systemd/system/webgso.service`. Runtime settings were
retained from the existing application process and stored in
`/etc/webgso/runtime.env` (root-owned directory mode 700, file mode 600).
Do not print, screenshot or commit that file: it contains database credentials.
The one-time demo replacement option/password are omitted from the service.

Useful commands in the AWS SSH terminal:

```bash
sudo systemctl status webgso --no-pager
sudo systemctl restart webgso
sudo journalctl -u webgso -n 50 --no-pager
```

Use `deploy-aws.ps1` on the laptop for later application changes. It detects the
active service, backs up the JAR, stops/starts it through systemd, checks the new
process startup and login page, and tries the previous JAR if startup fails.
It preserves the service environment file and its permissions.

The service was started and a deployment through the managed-service path was
verified with HTTP 200. Startup on reboot is configured/enabled; the EC2 machine
was not rebooted as part of this change.

## Direct HTTPS access

The AWS instance now serves WebGSO at `https://56.69.52.36/login` through Nginx.
Other laptops and phones can use this address without an SSH tunnel. The Java
application remains on loopback port 5186 and PostgreSQL remains on loopback 5432.
HTTP port 80 redirects to HTTPS except for certificate validation requests.

Nginx site: `/etc/nginx/sites-available/webgso`.
Application proxy settings: `/etc/systemd/system/webgso.service.d/public-https.conf`.
These enable forwarded HTTPS detection, the public base URL, and Secure session
cookies. Use the HTTPS address for login; the old HTTP SSH-forward URL on port
5188 cannot use Secure login cookies.

The trusted IP certificate uses Certbot's shortlived profile and is renewed by
`webgso-cert-renew.timer` twice daily, with Nginx reloaded after renewal.
Certbot is installed at `/opt/webgso-certbot/bin/certbot`. Never print or copy
certificate private keys into Git or chat.

Verified on 4 November 2026: HTTPS login and authenticated dashboard access work.
A real certificate renewal and the Nginx reload hook both succeeded. The staging
dry-run endpoint was unreachable from this instance; production ACME worked.

```bash
systemctl status nginx webgso-cert-renew.timer --no-pager
sudo /opt/webgso-certbot/bin/certbot renew --cert-name webgso-aws-ip --dry-run
```

The current EC2 public IP is not an Elastic IP. It can change after an EC2 stop
and start; if that happens, update the Nginx/app address and obtain a certificate
for the new IP. Rebooting the existing instance does not intentionally change
its public IP. No Elastic IP was allocated by this setup.
