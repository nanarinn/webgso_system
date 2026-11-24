#!/bin/bash
set -euo pipefail

REGION="ap-southeast-5"
ARTIFACT_BUCKET="gso-view-data-clone-artifacts-441798856671-ap-southeast-5"
PUBLIC_HOST="viewer.56-68-32-72.sslip.io"

dnf update -y
dnf install -y java-21-amazon-corretto-headless postgresql15-server postgresql15 python3

if [ ! -f /swapfile ]; then
  dd if=/dev/zero of=/swapfile bs=1M count=2048
  chmod 0600 /swapfile
  mkswap /swapfile
  swapon /swapfile
  echo '/swapfile swap swap defaults 0 0' >>/etc/fstab
fi

systemctl enable --now amazon-ssm-agent

postgresql-setup --initdb --unit postgresql
systemctl enable --now postgresql

# IAM instance-profile credentials can take a short time to become available
# during the first boot. Wait here instead of leaving a half-configured server.
for attempt in $(seq 1 30); do
  if aws sts get-caller-identity --region "$REGION" >/dev/null 2>&1; then
    break
  fi
  if [ "$attempt" -eq 30 ]; then
    echo "Timed out waiting for the clone instance IAM credentials" >&2
    exit 1
  fi
  sleep 10
done

DB_PASSWORD="$(aws ssm get-parameter --region "$REGION" --name /gso/view-data-clone/database-password --with-decryption --query Parameter.Value --output text)"
ADMIN_EMAIL="$(aws ssm get-parameter --region "$REGION" --name /gso/view-data-clone/admin-email --with-decryption --query Parameter.Value --output text)"
ADMIN_PASSWORD="$(aws ssm get-parameter --region "$REGION" --name /gso/view-data-clone/admin-password --with-decryption --query Parameter.Value --output text)"

sudo -u postgres psql -v ON_ERROR_STOP=1 --set=db_password="$DB_PASSWORD" <<'SQL'
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'view_data_app') THEN
    CREATE ROLE view_data_app LOGIN;
  END IF;
END
$$;
SELECT format('ALTER ROLE view_data_app PASSWORD %L', :'db_password') \gexec
SELECT 'CREATE DATABASE view_data OWNER view_data_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'view_data') \gexec
REVOKE CONNECT ON DATABASE view_data FROM PUBLIC;
GRANT CONNECT ON DATABASE view_data TO view_data_app;
SQL

# The Amazon Linux PostgreSQL default uses ident for localhost TCP. The Java
# service authenticates with the dedicated password stored in SSM instead.
sed -i -E '/^host[[:space:]]+all[[:space:]]+all[[:space:]]+(127\.0\.0\.1\/32|::1\/128)[[:space:]]+/s/[[:space:]]+ident$/ scram-sha-256/' /var/lib/pgsql/data/pg_hba.conf
systemctl reload postgresql

install -d -m 0750 -o root -g root /opt/gso-view-data /etc/gso-view-data
aws s3 cp "s3://$ARTIFACT_BUCKET/webgso-view-data.jar" /opt/gso-view-data/webgso-view-data.jar --region "$REGION"
chmod 0640 /opt/gso-view-data/webgso-view-data.jar

python3 - "$DB_PASSWORD" "$ADMIN_EMAIL" "$ADMIN_PASSWORD" <<'PY'
import shlex, sys
db_password, admin_email, admin_password = sys.argv[1:]
values = {
    "WEBGSO_DB_URL": "jdbc:postgresql://127.0.0.1:5432/view_data",
    "WEBGSO_DB_USER": "view_data_app",
    "WEBGSO_DB_PASSWORD": db_password,
    "WEBGSO_SCHEMA_INITIALIZE": "true",
    "WEBGSO_BACKUP_ENABLED": "false",
    "WEBGSO_WORKERS_ENABLED": "false",
    "WEBGSO_COLLECTION_WORKERS_ENABLED": "true",
    "VIEW_DATA_ADMIN_EMAIL": admin_email,
    "VIEW_DATA_ADMIN_PASSWORD": admin_password,
    "VIEW_DATA_ADMIN_NAME": "Admin",
    "SERVER_FORWARD_HEADERS_STRATEGY": "framework",
}
with open("/etc/gso-view-data/clone.env", "w", encoding="utf-8") as handle:
    for key, value in values.items():
        handle.write(f"{key}={shlex.quote(value)}\n")
PY
chmod 0600 /etc/gso-view-data/clone.env

cat >/etc/systemd/system/gso-view-data.service <<'UNIT'
[Unit]
Description=GSO View Data Clone
After=network-online.target postgresql.service
Wants=network-online.target

[Service]
Type=simple
User=ec2-user
Group=ec2-user
EnvironmentFile=/etc/gso-view-data/clone.env
ExecStart=/usr/bin/java -Xms128m -Xmx512m -jar /opt/gso-view-data/webgso-view-data.jar --server.port=5189 --server.address=127.0.0.1 --spring.profiles.active=local
Restart=always
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/tmp

[Install]
WantedBy=multi-user.target
UNIT

chown -R ec2-user:ec2-user /opt/gso-view-data
systemctl daemon-reload
systemctl enable --now gso-view-data

curl -fsSL "https://caddyserver.com/api/download?os=linux&arch=amd64" -o /usr/local/bin/caddy
chmod 0755 /usr/local/bin/caddy
setcap cap_net_bind_service=+ep /usr/local/bin/caddy || true
useradd --system --home /var/lib/caddy --shell /sbin/nologin caddy || true
install -d -m 0750 -o caddy -g caddy /var/lib/caddy /etc/caddy

cat >/etc/caddy/Caddyfile <<EOF
$PUBLIC_HOST {
  encode gzip
  reverse_proxy 127.0.0.1:5189
  header {
    X-Content-Type-Options nosniff
    X-Frame-Options DENY
    Referrer-Policy strict-origin-when-cross-origin
  }
}
EOF

cat >/etc/systemd/system/caddy.service <<'UNIT'
[Unit]
Description=Caddy HTTPS proxy for GSO View Data
After=network-online.target gso-view-data.service
Wants=network-online.target

[Service]
User=caddy
Group=caddy
ExecStart=/usr/local/bin/caddy run --environ --config /etc/caddy/Caddyfile
ExecReload=/usr/local/bin/caddy reload --config /etc/caddy/Caddyfile --force
Restart=on-failure
TimeoutStopSec=5s
LimitNOFILE=1048576
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/caddy

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now caddy
