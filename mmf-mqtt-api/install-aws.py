"""Invoked over SSH with secret configuration on stdin. Never logs secret values."""
import json, os, pathlib, pwd, shlex, shutil, subprocess, sys, time, urllib.request

def run(*args):
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL)

def env_value(value):
    if any(c in value for c in '\r\n\x00'):
        raise ValueError('Invalid multiline credential')
    return '"' + value.replace('\\', '\\\\').replace('"', '\\"') + '"'

def secure_file(path, text):
    path = pathlib.Path(path)
    fd = os.open(str(path), os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    os.fchmod(fd, 0o600)
    with os.fdopen(fd, 'w') as file:
        file.write(text)

def main():
    if '--resume' in sys.argv:
        values = {}
        for line in pathlib.Path('/etc/webgso-mmf/runtime.env').read_text().splitlines():
            key, value = line.split('=', 1)
            values[key] = shlex.split(value)[0]
        config = {'password': values['MMF_MQTT_PASSWORD'], 'token': values['MMF_API_TOKEN']}
    else:
        config = json.load(sys.stdin)
    password, token = config['password'], config['token']
    if not password or len(token) < 32:
        raise ValueError('Missing MQTT credential or API token')
    directory = pathlib.Path('/etc/webgso-mmf')
    directory.mkdir(mode=0o700, exist_ok=True)
    directory.chmod(0o700)
    install = pathlib.Path('/opt/webgso-mmf')
    install.mkdir(mode=0o755, exist_ok=True)
    try:
        pwd.getpwnam('webgso-mmf')
    except KeyError:
        run('useradd', '--system', '--no-create-home', '--shell', '/usr/sbin/nologin', 'webgso-mmf')
    stage = pathlib.Path('/home/ubuntu/mmf-mqtt-api-upload.jar')
    if (install / 'mmf-api.jar').exists():
        shutil.copy2(install / 'mmf-api.jar', install / 'mmf-api.jar.previous')
    shutil.copyfile(stage, install / 'mmf-api.jar')
    (install / 'mmf-api.jar').chmod(0o644)
    secure_file(directory / 'runtime.env', '\n'.join(k + '=' + env_value(v) for k, v in {
        'MMF_MQTT_URL': 'ssl://56.69.184.157:8883', 'MMF_MQTT_USERNAME': 'bidor-viewer',
        'MMF_MQTT_PASSWORD': password, 'MMF_MQTT_ROOT': 'LSS_BIDOR_30MW-NEDA',
        'MMF_API_BIND': '127.0.0.1', 'MMF_API_PORT': '5190', 'MMF_API_TOKEN': token
    }.items()) + '\n')
    pathlib.Path('/etc/systemd/system/webgso-mmf.service').write_text('''[Unit]
Description=WebGSO BIDOR MMF MQTT API
After=network-online.target
Wants=network-online.target
[Service]
User=webgso-mmf
EnvironmentFile=/etc/webgso-mmf/runtime.env
ExecStart=/usr/bin/java -Xms32m -Xmx128m -jar /opt/webgso-mmf/mmf-api.jar
Restart=on-failure
RestartSec=10
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
RestrictSUIDSGID=true
UMask=0077
[Install]
WantedBy=multi-user.target
''')
    run('systemctl', 'daemon-reload')
    run('systemctl', 'enable', 'webgso-mmf')
    run('systemctl', 'restart', 'webgso-mmf')
    request = urllib.request.Request('http://127.0.0.1:5190/health', headers={'Authorization': 'Bearer ' + token})
    connected = False
    for _ in range(25):
        try:
            with urllib.request.urlopen(request, timeout=3) as response:
                connected = json.load(response).get('mqttConnected') is True
            if connected:
                break
        except Exception:
            pass
        time.sleep(2)
    if not connected:
        print('MMF service installed but MQTT is not ready. Check broker access, TLS certificate and credentials. No public API route was added.')
        return 2
    secure_file(directory / 'client.env', 'WEBGSO_MMF_API_URL=https://56.69.52.36/api/mmf/BIDOR/latest\nWEBGSO_MMF_API_TOKEN=' + env_value(token) + '\n')
    dropin = pathlib.Path('/etc/systemd/system/webgso.service.d')
    dropin.mkdir(exist_ok=True)
    (dropin / 'mmf-api.conf').write_text('[Service]\nEnvironmentFile=/etc/webgso-mmf/client.env\n')
    site = pathlib.Path('/etc/nginx/sites-available/webgso')
    original = site.read_text()
    marker = '    # WebGSO authenticated MMF API\n'
    if marker not in original:
        addition = '''    # WebGSO authenticated MMF API
    location = /api/mmf/BIDOR/latest {
        proxy_pass http://127.0.0.1:5190;
        proxy_set_header Authorization $http_authorization;
        proxy_connect_timeout 5s;
        proxy_read_timeout 15s;
    }
'''
        if original.count('    location / {\n        proxy_pass') != 1:
            raise ValueError('Unexpected Nginx layout; public route not changed')
        shutil.copy2(site, site.with_name('webgso.before-mmf'))
        site.write_text(original.replace('    location / {\n        proxy_pass', addition + '    location / {\n        proxy_pass', 1))
        try:
            run('nginx', '-t')
        except Exception:
            site.write_text(original)
            raise
    run('systemctl', 'reload', 'nginx')
    run('systemctl', 'daemon-reload')
    run('systemctl', 'restart', 'webgso')
    print('AWS MMF connected. HTTPS API installed with Bearer authentication. WebGSO restarted.')
    return 0

try:
    sys.exit(main())
except Exception:
    print('MMF installation did not complete. Secret values were not printed. Review server service status.')
    sys.exit(1)
