import hashlib
import json
import os
import pathlib
import shutil
import signal
import subprocess
import tempfile
import time
import unittest
import urllib.request

ROOT = pathlib.Path(__file__).parent
REPO = ROOT.parents[2]
WINDOWS = os.name == "nt"
PUBLISH = REPO / ".runtime" / "sandbox-storage" / "M0-004" / "artifact" / ("win-x64" if WINDOWS else "linux-x64")
BIN = PUBLISH / ("ServerPackagingSpike.exe" if WINDOWS else "ServerPackagingSpike")
RUN_BIN = None

def run_env(extra=None):
    env = os.environ.copy(); env.update(extra or {}); return env

def run_bootstrap(stdout=None):
    if WINDOWS:
        command = [
            shutil.which("pwsh") or shutil.which("powershell") or "pwsh",
            "-NoProfile",
            "-File",
            str(ROOT / "bootstrap.ps1"),
        ]
    else:
        command = ["bash", str(ROOT / "bootstrap.sh")]
    subprocess.run(command, check=True, env=run_env(), stdout=stdout)

def popen_flags():
    return subprocess.CREATE_NEW_PROCESS_GROUP if WINDOWS else 0

def graceful_stop(process):
    if WINDOWS:
        process.send_signal(signal.CTRL_BREAK_EVENT)
    else:
        process.send_signal(signal.SIGTERM)
    return process.wait(timeout=5)

class SpikeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not BIN.exists():
            run_bootstrap()
        global RUN_BIN
        run_dir = pathlib.Path(tempfile.mkdtemp(prefix="m0-004-run-"))
        shutil.copytree(PUBLISH, run_dir, dirs_exist_ok=True)
        RUN_BIN = run_dir / "ServerPackagingSpike"
        if WINDOWS:
            RUN_BIN = RUN_BIN.with_suffix(".exe")
        else:
            RUN_BIN.chmod(0o700)

    @classmethod
    def tearDownClass(cls):
        if RUN_BIN:
            shutil.rmtree(RUN_BIN.parent, ignore_errors=True)

    def test_manifest_is_single_source(self):
        m = json.loads((ROOT / "manifest.json").read_text())
        self.assertEqual(m["contract"], "m0-004/v1")
        self.assertEqual(m["source"], "src/ServerPackagingSpike.csproj")

    def test_missing_configuration(self):
        p = subprocess.run([str(RUN_BIN)], env=run_env({"SPIKE_DATA_PATH": ""}), capture_output=True, text=True, timeout=5)
        self.assertEqual(p.returncode, 78); self.assertIn("missing_configuration", p.stderr)

    def test_readonly_data_path(self):
        with tempfile.TemporaryDirectory() as d:
            if WINDOWS:
                data_path = pathlib.Path(d) / "not-a-directory"
                data_path.write_text("occupied")
            else:
                data_path = pathlib.Path(d)
                os.chmod(d, 0o500)
            try:
                p = subprocess.run([str(RUN_BIN)], env=run_env({"SPIKE_DATA_PATH": str(data_path), "SPIKE_PORT": "5091"}), capture_output=True, text=True, timeout=5)
                if not WINDOWS and os.geteuid() == 0:
                    self.skipTest("root bypasses directory mode bits; read-only gate requires non-root")
                self.assertEqual(p.returncode, 78); self.assertIn("data_path_unwritable", p.stderr)
            finally:
                if not WINDOWS:
                    os.chmod(d, 0o700)

    def test_health_graceful_restart_and_occupied_port(self):
        with tempfile.TemporaryDirectory() as d:
            env = run_env({"SPIKE_DATA_PATH": d, "SPIKE_PORT": "5092"})
            p = subprocess.Popen([str(RUN_BIN)], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, creationflags=popen_flags())
            try:
                for _ in range(40):
                    try:
                        body = urllib.request.urlopen("http://127.0.0.1:5092/healthz", timeout=0.2).read()
                        if json.loads(body)["status"] == "ok": break
                    except Exception: time.sleep(.1)
                else: self.fail("health endpoint did not start")
                ready = json.loads(urllib.request.urlopen("http://127.0.0.1:5092/readyz", timeout=.2).read())
                self.assertEqual(ready["status"], "ready")
                probe = subprocess.run([str(RUN_BIN), "--health-probe"], env=env, timeout=5)
                self.assertEqual(probe.returncode, 0)
                self.assertEqual(graceful_stop(p), 0)
                p2 = subprocess.Popen([str(RUN_BIN)], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, creationflags=popen_flags())
                try:
                    for _ in range(40):
                        try:
                            if json.loads(urllib.request.urlopen("http://127.0.0.1:5092/healthz", timeout=.2).read())["status"] == "ok": break
                        except Exception: time.sleep(.1)
                    else: self.fail("restart health endpoint did not start")
                    occupied = subprocess.Popen([str(RUN_BIN)], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, creationflags=popen_flags())
                    try:
                        try: occupied.wait(timeout=2)
                        except subprocess.TimeoutExpired: occupied.terminate(); occupied.wait(timeout=3)
                        self.assertNotEqual(occupied.returncode, 0)
                    finally:
                        if occupied.poll() is None: occupied.kill(); occupied.wait()
                        if occupied.stdout: occupied.stdout.close()
                        if occupied.stderr: occupied.stderr.close()
                finally:
                    self.assertEqual(graceful_stop(p2), 0)
                    output = (p2.stdout.read() + p2.stderr.read()) if p2.stdout and p2.stderr else ""
                    self.assertNotIn(str(REPO), output)
                    if p2.stdout: p2.stdout.close()
                    if p2.stderr: p2.stderr.close()
            finally:
                if p.poll() is None: p.kill(); p.wait()
                if p.stdout: p.stdout.close()
                if p.stderr: p.stderr.close()

    def test_command_line_configuration_for_windows_service(self):
        with tempfile.TemporaryDirectory() as d:
            command = [
                str(RUN_BIN),
                "--SPIKE_DATA_PATH", d,
                "--SPIKE_PORT", "5093",
                "--SPIKE_BIND_HOST", "127.0.0.1",
            ]
            p = subprocess.Popen(command, env=run_env({"SPIKE_DATA_PATH": ""}), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, creationflags=popen_flags())
            try:
                for _ in range(40):
                    try:
                        body = urllib.request.urlopen("http://127.0.0.1:5093/healthz", timeout=.2).read()
                        if json.loads(body)["status"] == "ok": break
                    except Exception: time.sleep(.1)
                else: self.fail("command-line configured endpoint did not start")
                self.assertEqual(graceful_stop(p), 0)
            finally:
                if p.poll() is None: p.kill(); p.wait()
                if p.stdout: p.stdout.close()
                if p.stderr: p.stderr.close()

    def test_invalid_port_and_bind_host(self):
        for env in ({"SPIKE_DATA_PATH": tempfile.gettempdir(), "SPIKE_PORT": "80"}, {"SPIKE_DATA_PATH": tempfile.gettempdir(), "SPIKE_BIND_HOST": "bad host"}):
            p = subprocess.run([str(RUN_BIN)], env=run_env(env), capture_output=True, text=True, timeout=5)
            self.assertEqual(p.returncode, 78); self.assertIn("invalid_configuration", p.stderr)

    def test_packaging_definitions_and_provenance(self):
        m = json.loads((ROOT / "manifest.json").read_text())
        self.assertEqual(m["targets"], ["linux-x64", "win-x64", "docker"])
        self.assertIn("--health-probe", (ROOT / "Dockerfile").read_text())
        self.assertIn("SPIKE_BIND_HOST=0.0.0.0", (ROOT / "Dockerfile").read_text())
        self.assertIn("SPIKE_PROBE_HOST=127.0.0.1", (ROOT / "Dockerfile").read_text())
        self.assertTrue((ROOT / "systemd/assetlibrary-m0-004-spike.service").exists())
        self.assertTrue((ROOT / "scripts/windows-service.ps1").exists())
        self.assertTrue((REPO / ".runtime/sandbox-storage/M0-004/artifact/win-x64/ServerPackagingSpike.exe").exists())
        files = REPO / ".runtime/sandbox-storage/M0-004/artifact/files.sha256"
        run_bootstrap(stdout=subprocess.DEVNULL)
        expected = subprocess.check_output(["git", "log", "-1", "--format=%H", "--", "tests/spikes/server-packaging", ".codex/tasks/M0-004.md"], cwd=REPO, text=True).strip()
        self.assertEqual((REPO / ".runtime/sandbox-storage/M0-004/artifact/source-commit.txt").read_text().strip(), expected)
        self.assertEqual((REPO / ".runtime/sandbox-storage/M0-004/artifact/aggregate-sha256.txt").read_text().strip(), hashlib.sha256(files.read_bytes()).hexdigest())
        first = files.read_bytes()
        run_bootstrap(stdout=subprocess.DEVNULL)
        self.assertEqual(first, files.read_bytes(), "cold publish is not reproducible")

    def test_windows_service_adapter_is_direct_and_guarded(self):
        script = (ROOT / "scripts/windows-service.ps1").read_text()
        self.assertIn("AssetLibrarySpikeOwner", script)
        self.assertIn("--SPIKE_DATA_PATH", script)
        self.assertIn("verify-absent", script)
        self.assertNotIn("service.cmd", script)
        self.assertNotIn("Set-Content", script)

if __name__ == "__main__": unittest.main()
