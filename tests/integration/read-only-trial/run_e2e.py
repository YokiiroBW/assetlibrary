"""Run the real PostgreSQL / HTTPS / Chromium trial in owned temporary storage."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
import signal
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--postgres-bin", type=Path, required=True)
    parser.add_argument("--postgres-external", action="store_true", help="Use the explicit PostgreSQL test connection environment (CI service).")
    parser.add_argument("--node", type=Path, required=True)
    parser.add_argument("--web-root", type=Path, required=True)
    parser.add_argument("--playwright-module", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, default=ROOT / ".runtime/real-trial-evidence")
    return parser.parse_args()


def load_fixture():
    path = ROOT / "tests/database/test_migration_integration.py"
    spec = importlib.util.spec_from_file_location("trial_postgres_fixture", path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def prepare_database(module, fixture, created_logins: list[str]) -> dict[str, object]:
    database = fixture.fresh_database("trial_e2e")
    module.MIGRATIONS.apply_migrations(
        fixture.runner_tools(database), fixture.manifest, fixture.backup_directory("trial_e2e")
    )
    roles = {
        "audit": "assetlibrary_database_auditor",
        "gateway": "assetlibrary_gateway_auth_runtime",
        "library": "assetlibrary_library_storage_runtime",
        "asset": "assetlibrary_asset_identity_runtime",
        "scan": "assetlibrary_scan_reconciliation_runtime",
        "task": "assetlibrary_task_health_runtime",
    }
    logins = {}
    for key, role in roles.items():
        login = f"v020_{key}_{uuid.uuid4().hex[:12]}"
        fixture.sql(database, fixture.admin, f"""
BEGIN;
CREATE ROLE {login} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
GRANT {role} TO {login} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
GRANT CONNECT ON DATABASE "{database}" TO {login};
COMMIT;
""")
        created_logins.append(login)
        logins[key] = login
    return {"host": fixture.host, "port": fixture.port, "database": database, "logins": logins}


def main() -> int:
    options = arguments()
    if not options.execute:
        print(json.dumps({"status": "not_executed", "reason": "pass --execute to create owned test resources"}))
        return 77
    for path in (options.dotnet, options.node, options.web_root / "index.html", options.playwright_module):
        if not path.is_file():
            raise RuntimeError("a required test tool or built Web artifact is missing")
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8]
    options.evidence = options.evidence.resolve() / run_id
    options.evidence.mkdir(parents=True, exist_ok=False)
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain=v1"], cwd=ROOT, text=True).strip())
    (options.evidence / "run.json").write_text(json.dumps({
        "run_id": run_id, "source_revision": revision, "working_tree_dirty": dirty,
        "web_root": str(options.web_root.resolve()), "playwright_module": str(options.playwright_module.resolve()),
    }, indent=2), encoding="utf-8")
    print(f"REAL_TRIAL_EVIDENCE {options.evidence}", flush=True)
    module = load_fixture()
    with tempfile.TemporaryDirectory(prefix="al20-") as temporary:
        runtime = Path(temporary)
        os.environ["ASSETLIBRARY_TEST_POSTGRES_REQUIRED"] = "1"
        os.environ["ASSETLIBRARY_TEST_POSTGRES_EXTERNAL"] = "1" if options.postgres_external else "0"
        os.environ["ASSETLIBRARY_TEST_POSTGRES_BIN"] = str(options.postgres_bin.resolve())
        os.environ["ASSETLIBRARY_TEST_RUNTIME"] = str(runtime)
        fixture_type = module.PostgreSqlIntegrationTests
        fixture = fixture_type(methodName="runTest")
        created_logins: list[str] = []
        try:
            fixture_type.setUpClass()
            settings = prepare_database(module, fixture, created_logins)
            settings.update({
                "runtime_root": str(runtime),
                "web_root": str(options.web_root.resolve()),
                "node": str(options.node.resolve()),
                "playwright_module": str(options.playwright_module.resolve()),
                "browser_script": str(ROOT / "tests/integration/read-only-trial/browser.mjs"),
                "evidence": str(options.evidence.resolve()),
                "dotnet": str(options.dotnet.resolve()),
                "host_dll": str(ROOT / "services/core-server/Host/bin/Release/net10.0/AssetLibrary.CoreServer.Host.dll"),
            })
            settings_file = runtime / "test-settings.json"
            settings_file.write_text(json.dumps(settings), encoding="utf-8")
            environment = os.environ.copy()
            environment["ASSETLIBRARY_TRIAL_E2E_REQUIRED"] = "1"
            environment["ASSETLIBRARY_TRIAL_E2E_SETTINGS"] = str(settings_file)
            command = [
                str(options.dotnet), "test",
                str(ROOT / "tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj"),
                "--configuration", "Release", "--no-build", "--no-restore",
                "--filter", "FullyQualifiedName~TrialHostIntegrationTests",
                "--logger", "console;verbosity=normal", "--logger", "trx;LogFileName=trial-e2e.trx",
                "--results-directory", str(options.evidence),
            ]
            environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false"
            environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
            with subprocess.Popen(command, cwd=ROOT, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                  text=True, encoding="utf-8", start_new_session=os.name != "nt",
                                  creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0) as process:
                try:
                    output, _ = process.communicate(timeout=300)
                except subprocess.TimeoutExpired:
                    if os.name == "nt":
                        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                                       capture_output=True, timeout=15, check=False)
                    else:
                        os.killpg(process.pid, signal.SIGKILL)
                    process.communicate(timeout=15)
                    raise RuntimeError("the real trial runner exceeded its deadline") from None
            (options.evidence / "runner.log").write_text(output, encoding="utf-8")
            print(output)
            if process.returncode == 0:
                counters = ET.parse(options.evidence / "trial-e2e.trx").find(".//{*}Counters")
                if counters is None or counters.attrib.get("total") != "1" or counters.attrib.get("passed") != "1":
                    raise RuntimeError("the required real trial must actually execute and pass")
                (options.evidence / "acceptance.json").write_text(json.dumps({
                    "status": "passed", "run_id": run_id, "aggregate_tests_passed": 1, "skipped": 0,
                    "scenarios": ["protected_operator_bootstrap", "runtime_role_boundary", "real_browser_registration",
                                  "real_worker_initial_scan", "browse_and_full_path_search", "desktop_and_mobile_dark",
                                  "ordinary_user_and_hidden_library_denial", "origin_and_csrf_denial",
                                  "persistent_session_restart", "offline_login", "offline_snapshot_preservation",
                                  "failed_scan_retry", "queued_scan_cancellation_retry", "administrator_recovery_replay",
                                  "old_session_revocation", "unchanged_source_hash_and_mtime", "owned_tls_container_disposal"],
                }, indent=2), encoding="utf-8")
            exit_code = process.returncode
        finally:
            cleaned = fixture.doCleanups()
            try:
                for login in created_logins:
                    fixture.sql("postgres", fixture.admin, f"DROP ROLE {login};")
            finally:
                fixture_type.doClassCleanups()
            if not cleaned or getattr(fixture_type, "tearDown_exceptions", []):
                raise RuntimeError("owned PostgreSQL cleanup did not complete")
    if exit_code == 0:
        acceptance_file = options.evidence / "acceptance.json"
        acceptance = json.loads(acceptance_file.read_text(encoding="utf-8"))
        acceptance["resource_cleanup"] = "verified"
        acceptance_file.write_text(json.dumps(acceptance, indent=2), encoding="utf-8")
    return exit_code


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    raise SystemExit(main())
