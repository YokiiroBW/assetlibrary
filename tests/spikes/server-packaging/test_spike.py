import json, os, pathlib, shutil, signal, subprocess, tempfile, time, unittest, urllib.request

ROOT = pathlib.Path(__file__).parent
REPO = ROOT.parents[2]
PUBLISH = REPO / ".runtime" / "sandbox-storage" / "M0-004" / "artifact" / "linux-x64"
BIN = PUBLISH / "ServerPackagingSpike"
RUN_BIN = None

def run_env(extra=None):
    env = os.environ.copy(); env.update(extra or {}); return env

class SpikeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not BIN.exists():
            subprocess.run(["bash", str(ROOT / "bootstrap.sh")], check=True)
        global RUN_BIN
        run_dir = pathlib.Path(tempfile.mkdtemp(prefix="m0-004-run-"))
        shutil.copytree(PUBLISH, run_dir, dirs_exist_ok=True)
        RUN_BIN = run_dir / "ServerPackagingSpike"
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
        p = subprocess.run([str(RUN_BIN)], env=run_env({"SPIKE_DATA_PATH": ""}), capture_output=True, text=True)
        self.assertEqual(p.returncode, 78); self.assertIn("missing_configuration", p.stderr)

    def test_readonly_data_path(self):
        with tempfile.TemporaryDirectory() as d:
            os.chmod(d, 0o500)
            try:
                p = subprocess.run([str(RUN_BIN)], env=run_env({"SPIKE_DATA_PATH": d, "SPIKE_PORT": "5091"}), capture_output=True, text=True)
                if os.geteuid() == 0:
                    self.skipTest("root bypasses directory mode bits; read-only gate requires non-root")
                self.assertEqual(p.returncode, 78); self.assertIn("data_path_unwritable", p.stderr)
            finally: os.chmod(d, 0o700)

    def test_health_graceful_restart_and_occupied_port(self):
        with tempfile.TemporaryDirectory() as d:
            env = run_env({"SPIKE_DATA_PATH": d, "SPIKE_PORT": "5092"})
            p = subprocess.Popen([str(RUN_BIN)], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            try:
                for _ in range(40):
                    try:
                        body = urllib.request.urlopen("http://127.0.0.1:5092/healthz", timeout=0.2).read()
                        if json.loads(body)["status"] == "ok": break
                    except Exception: time.sleep(.1)
                else: self.fail("health endpoint did not start")
                p.send_signal(signal.SIGTERM); self.assertEqual(p.wait(timeout=5), 0)
                p2 = subprocess.Popen([str(RUN_BIN)], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                try:
                    for _ in range(40):
                        try:
                            if json.loads(urllib.request.urlopen("http://127.0.0.1:5092/healthz", timeout=.2).read())["status"] == "ok": break
                        except Exception: time.sleep(.1)
                    else: self.fail("restart health endpoint did not start")
                    occupied = subprocess.Popen([str(RUN_BIN)], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                    try:
                        try: occupied.wait(timeout=2)
                        except subprocess.TimeoutExpired: occupied.terminate(); occupied.wait(timeout=3)
                        self.assertNotEqual(occupied.returncode, 0)
                    finally:
                        if occupied.poll() is None: occupied.kill(); occupied.wait()
                        if occupied.stdout: occupied.stdout.close()
                        if occupied.stderr: occupied.stderr.close()
                finally:
                    p2.terminate(); p2.wait(timeout=5)
                    output = (p2.stdout.read() + p2.stderr.read()) if p2.stdout and p2.stderr else ""
                    self.assertNotIn(str(REPO), output)
                    if p2.stdout: p2.stdout.close()
                    if p2.stderr: p2.stderr.close()
            finally:
                if p.poll() is None: p.kill(); p.wait()
                if p.stdout: p.stdout.close()
                if p.stderr: p.stderr.close()

    def test_invalid_port_and_bind_host(self):
        for env in ({"SPIKE_DATA_PATH": tempfile.gettempdir(), "SPIKE_PORT": "80"}, {"SPIKE_DATA_PATH": tempfile.gettempdir(), "SPIKE_BIND_HOST": "bad host"}):
            p = subprocess.run([str(RUN_BIN)], env=run_env(env), capture_output=True, text=True)
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
        subprocess.run(["bash", str(ROOT / "bootstrap.sh")], check=True, env=run_env({"M0_004_DOTNET_DIR": "/tmp/m0-004-dotnet-10.0.111"}), stdout=subprocess.DEVNULL)
        expected = subprocess.check_output(["git", "log", "-1", "--format=%H", "--", "tests/spikes/server-packaging", ".codex/tasks/M0-004.md"], cwd=REPO, text=True).strip()
        self.assertEqual((REPO / ".runtime/sandbox-storage/M0-004/artifact/source-commit.txt").read_text().strip(), expected)
        self.assertEqual((REPO / ".runtime/sandbox-storage/M0-004/artifact/aggregate-sha256.txt").read_text().strip(), subprocess.check_output(["sha256sum", str(files)], text=True).split()[0])
        first = files.read_bytes()
        subprocess.run(["bash", str(ROOT / "bootstrap.sh")], check=True, env=run_env({"M0_004_DOTNET_DIR": "/tmp/m0-004-dotnet-10.0.111"}), stdout=subprocess.DEVNULL)
        self.assertEqual(first, files.read_bytes(), "cold publish is not reproducible")

if __name__ == "__main__": unittest.main()
