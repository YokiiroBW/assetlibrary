from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import ssl
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock
import urllib.request
import uuid


ROOT = Path(__file__).resolve().parents[2]


def load_builder():
    spec = importlib.util.spec_from_file_location("trial_package_builder", ROOT / "scripts/build_read_only_trial.py")
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


BUILDER = load_builder()


def powershell_script(pwsh: str, code: str, argument: Path):
    with tempfile.TemporaryDirectory(prefix="trial-script-") as temporary:
        path = Path(temporary) / "check.ps1"
        path.write_text("param([string]$Target)\n$ErrorActionPreference='Stop'\n" + code, encoding="utf-8")
        return subprocess.run([pwsh, "-NoProfile", "-File", str(path), str(argument)], capture_output=True, text=True, timeout=15)


class TrialPackageTests(unittest.TestCase):
    def test_source_requires_clean_commit_and_binds_all_files(self):
        with tempfile.TemporaryDirectory(prefix="trial-source-") as temporary:
            root = Path(temporary)
            (root / "Directory.Build.props").write_text("<Project />", encoding="utf-8")
            def git(*args):
                return subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True, check=True, timeout=10).stdout.strip()
            git("init", "-q")
            git("add", ".")
            git("-c", "user.name=Trial Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "baseline")
            revision = BUILDER.clean_revision(root)
            self.assertEqual(revision, git("rev-parse", "HEAD"))
            (root / "new-source.ts").write_text("export const value = 1;", encoding="utf-8")
            with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "clean committed"):
                BUILDER.clean_revision(root)

    def test_output_refuses_replacement_and_escape(self):
        with tempfile.TemporaryDirectory(prefix="trial-output-") as temporary:
            root = Path(temporary)
            output = root / ".runtime/sandbox-storage/V01-019/build"
            BUILDER.create_output(root, output)
            marker = output / "preserve.txt"
            marker.write_text("keep", encoding="utf-8")
            with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "already exists"):
                BUILDER.create_output(root, output)
            with self.assertRaises(BUILDER.RELEASE.ReleaseBuildError):
                BUILDER.create_output(root, root / "outside")
            self.assertEqual(marker.read_text(), "keep")

    def test_manifest_binds_host_web_and_scripts_without_release_claim(self):
        with tempfile.TemporaryDirectory(prefix="trial-manifest-") as temporary:
            root = Path(temporary)
            for relative in ("host/program.exe", "web/index.html", "trial.ps1", "migrations/manifest.json"):
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(relative.encode())
            manifest = BUILDER.package_manifest(root, "a" * 40, "0.1.0", {})
            self.assertFalse(manifest["production_file_writes_enabled"])
            self.assertFalse(manifest["formal_release"])
            self.assertEqual(len(manifest["files"]), 4)
            before = manifest["files"]
            (root / "web/index.html").write_bytes(b"changed")
            after = BUILDER.package_manifest(root, "a" * 40, "0.1.0", {})["files"]
            self.assertNotEqual(before, after)

    def test_powershell_parses_and_never_installs_system_components(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is unavailable")
        script = ROOT / "infra/windows/trial/trial.ps1"
        command = "$t=$null;$e=$null;[Management.Automation.Language.Parser]::ParseFile($Target,[ref]$t,[ref]$e)|Out-Null;if($e.Count){exit 1}"
        result = powershell_script(pwsh, command, script)
        self.assertEqual(result.returncode, 0, result.stderr)
        source = script.read_text(encoding="utf-8")
        for forbidden in ("New-Service", "Register-ScheduledTask", "Import-Certificate", "New-NetFirewallRule", "New-SelfSignedCertificate"):
            self.assertNotIn(forbidden, source)
        launcher = (ROOT / "infra/windows/trial/trial_process.py").read_text(encoding="utf-8")
        self.assertIn("subprocess.SW_HIDE", launcher)
        self.assertIn("close_fds=True", launcher)

    @unittest.skipUnless(os.name == "nt", "Windows private-directory preflight")
    def test_runtime_preflight_preserves_foreign_state_and_rejects_tampering(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is unavailable")
        with tempfile.TemporaryDirectory(prefix="al19pre-") as temporary:
            root = Path(temporary)
            package = root / "package"
            package.mkdir()
            shutil.copy2(ROOT / "infra/windows/trial/trial.ps1", package)
            (package / "web").mkdir()
            index = package / "web/index.html"
            index.write_bytes(b"<html>test</html>")
            manifest = BUILDER.package_manifest(package, "a" * 40, "0.1.0", {})
            manifest_path = package / "package-manifest.json"
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            foreign = root / "foreign"
            command = "$s=[Security.Principal.WindowsIdentity]::GetCurrent().User;$a=[Security.AccessControl.DirectorySecurity]::new();$a.SetOwner($s);$a.SetAccessRuleProtection($true,$false);$a.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($s,'FullControl','ContainerInherit,ObjectInherit','None','Allow'));[IO.FileSystemAclExtensions]::Create([IO.DirectoryInfo]::new($Target),$a)"
            created = powershell_script(pwsh, command, foreign)
            self.assertEqual(created.returncode, 0, created.stderr)
            preserved = foreign / "preserve.txt"
            preserved.write_bytes(b"foreign state must not be changed")
            def rejected(state):
                result = subprocess.run([pwsh, "-NoProfile", "-File", str(package / "trial.ps1"), "-Action", "initialize", "-StatePath", str(state)], capture_output=True, text=True, encoding="utf-8", timeout=15)
                self.assertNotEqual(result.returncode, 0)
                return json.loads(result.stdout)["code"]
            self.assertEqual(rejected(foreign), "trial_existing_state_rejected")
            self.assertEqual(preserved.read_bytes(), b"foreign state must not be changed")
            self.assertFalse((foreign / "trial.lock").exists())
            state = root / "never-created"
            original = index.read_bytes()
            index.write_bytes(b"changed")
            self.assertEqual(rejected(state), "trial_package_integrity_failed")
            self.assertFalse(state.exists())
            index.write_bytes(original)
            extra = package / "unexpected.txt"
            extra.write_bytes(b"unexpected")
            self.assertEqual(rejected(state), "trial_package_unexpected_file")
            extra.unlink()
            manifest["files"].append({"path": "../foreign/preserve.txt", "length": 0, "sha256": "a" * 64})
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            self.assertEqual(rejected(state), "trial_package_member_invalid")
            self.assertFalse(state.exists())


@unittest.skipUnless(os.name == "nt", "native trial database test requires Windows")
class TrialNativeDatabaseTests(unittest.TestCase):
    def test_private_cluster_initialize_restart_ownership_and_backup(self):
        pg_bin = os.environ.get("ASSETLIBRARY_TEST_TRIAL_POSTGRES_BIN")
        pwsh = shutil.which("pwsh")
        if not pg_bin or not pwsh:
            if os.environ.get("ASSETLIBRARY_TEST_TRIAL_REQUIRED") == "1":
                self.fail("required trial PostgreSQL/PowerShell prerequisites are missing")
            self.skipTest("set ASSETLIBRARY_TEST_TRIAL_POSTGRES_BIN for native trial initialization")
        with tempfile.TemporaryDirectory(prefix="al019-") as temporary:
            root = Path(temporary)
            package = root / "package"
            package.mkdir()
            shutil.copytree(ROOT / "database/migrations/production", package / "migrations")
            shutil.copy2(ROOT / "infra/windows/trial/trial_database.py", package)
            shutil.copy2(ROOT / "infra/windows/trial/trial_process.py", package)
            state = root / "state"
            command = "$s=[Security.Principal.WindowsIdentity]::GetCurrent().User;$a=[Security.AccessControl.DirectorySecurity]::new();$a.SetOwner($s);$a.SetAccessRuleProtection($true,$false);$a.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($s,'FullControl','ContainerInherit,ObjectInherit','None','Allow'));[IO.FileSystemAclExtensions]::Create([IO.DirectoryInfo]::new($Target),$a)"
            created = powershell_script(pwsh, command, state)
            self.assertEqual(created.returncode, 0, created.stderr)
            for directory in ("secrets", "logs", "backups"):
                (state / directory).mkdir()
            with socket.socket() as listener:
                listener.bind(("127.0.0.1", 0))
                port = listener.getsockname()[1]
            (state / "trial-owner.json").write_text(json.dumps({
                "product": "AssetLibrary/read-only-trial", "state_path": str(state), "deployment_id": str(uuid.uuid4()),
                "postgres_bin": pg_bin, "database_port": port,
            }), encoding="utf-8")
            def run(action, success=True):
                result = subprocess.run([sys.executable, "-I", "-B", str(package / "trial_database.py"), action, "--state", str(state)], capture_output=True, text=True, timeout=150)
                self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
                return json.loads(result.stdout)
            try:
                initialized = run("initialize")
                expected_count = len(json.loads((package / "migrations/manifest.json").read_text())["migrations"])
                self.assertEqual(initialized, {"status": "initialized", "migrations": expected_count, "module_logins": 6})
                backups = list((state / "backups").glob("*/metadata.json"))
                self.assertEqual(len(backups), 1)
                metadata = json.loads(backups[0].read_text())
                archive = backups[0].parent / metadata["archive_file"]
                self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), metadata["archive_sha256"])
                credentials = (state / "secrets/database-passwords.json").read_bytes()
                self.assertEqual(len(set(json.loads(credentials).values())), 8)
                self.assertEqual(run("initialize")["status"], "initialized")
                self.assertEqual((state / "secrets/database-passwords.json").read_bytes(), credentials)
                self.assertEqual(len(list((state / "backups").glob("*/metadata.json"))), 1)
                self.assertEqual(run("start")["status"], "started")
                self.assertEqual(run("start")["status"], "already_running")
                process_path = state / "postgres-process.json"
                original = process_path.read_bytes()
                altered = json.loads(original)
                altered["created"] += 1
                process_path.write_text(json.dumps(altered), encoding="utf-8")
                self.assertEqual(run("stop", False)["code"], "database_process_record_mismatch")
                process_path.write_bytes(original)
                self.assertEqual(run("status")["status"], "running")
                self.assertEqual(run("stop")["status"], "stopped")
                spec = importlib.util.spec_from_file_location("native_trial_database", package / "trial_database.py")
                runtime = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(runtime)
                database = runtime.TrialDatabase(state, package)
                with mock.patch.object(runtime, "write_json", side_effect=OSError("simulated full state volume")):
                    with self.assertRaises(OSError):
                        database.start()
                self.assertIsNone(database.actual_process(), "failed process recording left a server running")
                self.assertEqual(run("start")["status"], "started")
                self.assertEqual(run("stop")["status"], "stopped")
                cluster_path = state / "postgres-cluster.json"
                original_cluster = cluster_path.read_bytes()
                altered = json.loads(original_cluster)
                altered["system_identifier"] = "1"
                cluster_path.write_text(json.dumps(altered), encoding="utf-8")
                self.assertEqual(run("start", False)["code"], "database_cluster_owner_mismatch")
                cluster_path.write_bytes(original_cluster)
            finally:
                run("stop")


@unittest.skipUnless(os.name == "nt", "native trial package test requires Windows")
class TrialNativePackageTests(unittest.TestCase):
    def test_real_package_operator_https_restart_and_integrity(self):
        configured = os.environ.get("ASSETLIBRARY_TEST_TRIAL_PACKAGE")
        pg_bin = os.environ.get("ASSETLIBRARY_TEST_TRIAL_POSTGRES_BIN")
        pwsh = shutil.which("pwsh")
        if not configured or not pg_bin or not pwsh:
            if os.environ.get("ASSETLIBRARY_TEST_TRIAL_PACKAGE_REQUIRED") == "1":
                self.fail("required trial package/PG/PowerShell prerequisites are missing")
            self.skipTest("set ASSETLIBRARY_TEST_TRIAL_PACKAGE for same-source native package validation")
        source_package = Path(configured).resolve()
        transcript = []
        with tempfile.TemporaryDirectory(prefix="al19pkg-") as temporary:
            root = Path(temporary)
            package = root / "package"
            shutil.copytree(source_package, package)
            script = package / "trial.ps1"
            state = root / "state"
            assets = root / "assets"
            assets.mkdir()
            asset = assets / "unchanged.txt"
            asset.write_bytes(b"read-only-package-native-evidence\n")
            fingerprint = (hashlib.sha256(asset.read_bytes()).hexdigest(), asset.stat().st_mtime_ns)
            with socket.socket() as listener:
                listener.bind(("127.0.0.1", 0))
                port = listener.getsockname()[1]
            with socket.socket() as listener:
                listener.bind(("127.0.0.1", 0))
                database_port = listener.getsockname()[1]
            origin = f"https://localhost:{port}"
            settings = root / "settings.json"
            settings.write_text(json.dumps({"public_origin": origin, "storage_sources": [{
                "source_key": "sandbox", "display_name": "2026-09-07T00:00:00Z", "allowed_root": str(assets),
            }]}), encoding="utf-8")
            def run(action, *arguments, input_text=None, success=True, allow_unavailable=False):
                result = subprocess.run([pwsh, "-NoProfile", "-File", str(script), "-Action", action, "-StatePath", str(state), *arguments], input=input_text, capture_output=True, text=True, encoding="utf-8", timeout=150)
                transcript.append(result.stdout + result.stderr)
                response = json.loads(result.stdout.splitlines()[-1])
                if allow_unavailable and response.get("reason") == "service_unavailable" and result.returncode != 0:
                    return response
                self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
                return response
            def perform_operator(arguments, secret):
                for attempt in range(3):
                    # This exercises the documented logical retry after a real transient risk-service failure.
                    # Only the first recovery call creates an identity; retry IDs/expiry/password stay unchanged.
                    retry_arguments = arguments if attempt == 0 else tuple(value for value in arguments if value != "-NewAttempt")
                    response = run("operator", *retry_arguments, input_text=secret, allow_unavailable=True)
                    if response.get("outcome") == "applied":
                        return response
                self.fail("real administrator risk dependency stayed unavailable after three identical logical attempts")
            initialize = ("-SettingsFile", str(settings), "-Python", sys.executable, "-PostgresBin", pg_bin, "-DatabasePort", str(database_port))
            initialized = run("initialize", *initialize)
            self.assertEqual(initialized["status"], "initialized")
            self.assertEqual(initialized["certificate_trust"], "manual_required")
            self.assertEqual(json.loads((state / "trial.json").read_text())["storage_sources"][0]["display_name"], "2026-09-07T00:00:00Z")
            self.assertEqual(run("initialize", *initialize)["status"], "already_initialized")
            password = secrets.token_urlsafe(40)
            operator = ("-OperatorAction", "bootstrap", "-AccountName", "trialadministrator", "-DisplayName", "Trial administrator", "-PasswordFromStdin")
            try:
                self.assertEqual(perform_operator(operator, password)["outcome"], "applied")
                attempt_before = (state / "operator-attempt.json").read_bytes()
                self.assertFalse(password in attempt_before.decode(), "operator attempt persisted a password")
                replay = perform_operator(operator, password)
                self.assertTrue(replay["was_replayed"])
                self.assertEqual((state / "operator-attempt.json").read_bytes(), attempt_before)
                self.assertEqual(run("start")["status"], "running")
                self.assertEqual(run("start", success=False)["code"], "trial_already_running")
                self.assertEqual(run("status")["status"], "ready")
                control = json.loads((state / ".trial-process.json").read_text())
                stop_request = state / ".trial-stop.json"
                try:
                    stale = dict(control)
                    stale["generation"] = str(uuid.uuid4())
                    stop_request.write_text(json.dumps(stale), encoding="utf-8")
                    time.sleep(0.8)
                    self.assertEqual(run("status")["status"], "ready")
                    forged = dict(control)
                    forged["stop_nonce"] = ("0" if control["stop_nonce"][0] != "0" else "1") + control["stop_nonce"][1:]
                    stop_request.write_text(json.dumps(forged), encoding="utf-8")
                    time.sleep(0.8)
                    self.assertEqual(run("status")["status"], "ready")
                finally:
                    stop_request.unlink(missing_ok=True)
                context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
                context.load_verify_locations(cadata=ssl.DER_cert_to_PEM_cert((state / "tls/localhost.cer").read_bytes()))
                opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))
                with opener.open(origin + "/", timeout=10) as response:
                    self.assertEqual(response.status, 200)
                    self.assertIn(b"<html", response.read(8192))
                def login():
                    request = urllib.request.Request(origin + "/assetlink/v1/auth/login", data=json.dumps({"account_name": "trialadministrator", "password": password}).encode(), headers={"Origin": origin, "Content-Type": "application/json"}, method="POST")
                    with opener.open(request, timeout=15) as response:
                        payload = json.loads(response.read(8192))
                        self.assertTrue(payload["authenticated"])
                        self.assertTrue(payload["is_system_administrator"])
                login()
                process_path = state / "host-process.json"
                original = process_path.read_bytes()
                altered = json.loads(original)
                altered["created_ticks"] += 1
                process_path.write_text(json.dumps(altered), encoding="utf-8")
                self.assertEqual(run("stop", success=False)["code"], "trial_host_process_owner_mismatch")
                process_path.write_bytes(original)
                self.assertEqual(run("status")["status"], "ready")
                stopped = run("stop")
                self.assertEqual(stopped["status"], "stopped")
                self.assertEqual(stopped["shutdown"], "graceful")
                self.assertFalse((state / ".trial-process.json").exists())
                self.assertEqual(run("status")["status"], "stopped")
                self.assertEqual(run("start")["status"], "running")
                login()
                previous_password = password
                password = secrets.token_urlsafe(40)
                recovery = ("-OperatorAction", "recover", "-AccountName", "trialadministrator", "-NewAttempt", "-PasswordFromStdin")
                self.assertEqual(perform_operator(recovery, password)["outcome"], "applied")
                login()
                index = package / "web/index.html"
                original_index = index.read_bytes()
                try:
                    index.write_bytes(original_index + b"\n<!-- integrity test -->")
                    self.assertEqual(run("status", success=False)["code"], "trial_package_integrity_failed")
                finally:
                    index.write_bytes(original_index)
                self.assertEqual(run("status")["status"], "ready")
                self.assertEqual((hashlib.sha256(asset.read_bytes()).hexdigest(), asset.stat().st_mtime_ns), fingerprint)
                logged = "\n".join(transcript) + "\n".join(path.read_text(encoding="utf-8", errors="replace") for path in (state / "logs").glob("*.log"))
                for secret in [password, previous_password, control["stop_nonce"], *json.loads((state / "secrets/database-passwords.json").read_text()).values()]:
                    self.assertFalse(secret in logged, "native lifecycle output disclosed a credential")
            finally:
                run("stop")


if __name__ == "__main__":
    unittest.main()
