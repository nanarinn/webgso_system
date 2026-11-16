"""Deploy a tested JAR to the existing manually launched WebGSO instance.

Preserves the running process environment. Never prints or saves its credentials.
Does not modify account records, initialize the schema or restore databases.
"""
import os
import pathlib
import shutil
import signal
import subprocess
import sys
import time
import urllib.request


class ServiceProcess:
    def __init__(self):
        self.pid = int(subprocess.check_output(
            ["systemctl", "show", "webgso.service", "-p", "MainPID", "--value"], text=True).strip())

    def poll(self):
        result = subprocess.run(["systemctl", "is-active", "--quiet", "webgso.service"])
        return None if result.returncode == 0 else 1


def service(action):
    subprocess.run(["sudo", "-n", "systemctl", action, "webgso.service"], check=True)


def stop(pid):
    os.kill(pid, signal.SIGTERM)
    for _ in range(30):
        if not pathlib.Path(f"/proc/{pid}").exists():
            return
        time.sleep(1)
    raise RuntimeError("Application did not stop; deployment halted")


def start(command, env, root, logpath):
    with open(logpath, "ab") as log:
        os.chmod(logpath, 0o600)
        return subprocess.Popen(command, cwd=root, env=env, stdin=subprocess.DEVNULL,
                                stdout=log, stderr=log, start_new_session=True)


def healthy(child, logpath, port):
    for _ in range(90):
        if child.poll() is not None:
            return False
        if logpath is None:
            output = subprocess.check_output(
                ["sudo", "-n", "journalctl", "_PID=" + str(child.pid), "--no-pager", "-o", "cat"], text=True)
        else:
            output = logpath.read_text(errors="replace")
        if "Started WebgsoApplication" in output:
            try:
                with urllib.request.urlopen(f"http://127.0.0.1:{port}/login", timeout=5) as response:
                    if response.status == 200:
                        return True
            except OSError:
                pass
        time.sleep(1)
    return False


def main():
    root = pathlib.Path("/home/ubuntu/webgso_system")
    stage = pathlib.Path(sys.argv[1]).resolve()
    if stage.parent != pathlib.Path("/home/ubuntu") or not stage.name.startswith("webgso-update-"):
        raise RuntimeError("Unexpected staged JAR path")
    if not stage.is_file():
        raise RuntimeError("Staged JAR not found")
    managed = subprocess.run(["systemctl", "is-active", "--quiet", "webgso.service"]).returncode == 0
    if managed:
        pids = [str(ServiceProcess().pid)]
    else:
        pids = subprocess.check_output(["pgrep", "-f", r"^java .*webgso-system"], text=True).split()
    if len(pids) != 1:
        raise RuntimeError("Expected one running WebGSO process. No deployment applied.")
    pid = int(pids[0])
    args = [a.decode() for a in pathlib.Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0") if a]
    jar = root / "target/webgso-system-2.0.0-SNAPSHOT.jar"
    if str(jar) not in args and "target/webgso-system-2.0.0-SNAPSHOT.jar" not in args:
        raise RuntimeError("Unexpected application command")
    raw = subprocess.check_output(["sudo", "-n", "cat", f"/proc/{pid}/environ"])
    env = dict(item.decode().split("=", 1) for item in raw.split(b"\0") if b"=" in item)
    port = env.get("PORT", "5186")
    for arg in args:
        if arg.startswith("--server.port="):
            port = arg.split("=", 1)[1]
    if not port.isdigit():
        raise RuntimeError("Unexpected server port")
    # One-time identity replacement must never run during a design deployment.
    command = [arg for arg in args if not arg.startswith("--webgso.demo.replace-affiqah=")]
    env.pop("WEBGSO_DEMO_PASSWORD", None)
    stamp = time.strftime("%Y%m%d-%H%M%S", time.gmtime()) + "-" + str(os.getpid())
    backup_dir = pathlib.Path("/home/ubuntu/.local/state/webgso/releases")
    backup_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
    backup = backup_dir / ("webgso-" + stamp + ".jar")
    shutil.copy2(jar, backup)
    print("Application backup created. Existing server settings retained.", flush=True)
    if managed:
        service("stop")
    else:
        stop(pid)
    child = None
    try:
        # SSM runs as root; retain access for the existing service user.
        original = jar.stat()
        os.chown(stage, original.st_uid, original.st_gid)
        os.chmod(stage, original.st_mode & 0o777)
        os.replace(stage, jar)
        logpath = backup_dir / ("deploy-" + stamp + ".log")
        if managed:
            service("start")
            child = ServiceProcess()
        else:
            child = start(command, env, root, logpath)
        if not healthy(child, None if managed else logpath, port):
            raise RuntimeError("New application did not pass startup/login-page checks")
    except Exception:
        if managed:
            service("stop")
        elif child is not None and child.poll() is None:
            stop(child.pid)
        shutil.copy2(backup, jar)
        rollback_log = backup_dir / ("rollback-" + stamp + ".log")
        if managed:
            service("start")
            previous = ServiceProcess()
        else:
            previous = start(command, env, root, rollback_log)
        if healthy(previous, None if managed else rollback_log, port):
            print("Deployment failed; previous application restored.", flush=True)
        else:
            print("Rollback startup failed. Review server logs.", flush=True)
        raise
    print("Deployment successful: AWS login page returned HTTP 200.", flush=True)


if __name__ == "__main__":
    main()
