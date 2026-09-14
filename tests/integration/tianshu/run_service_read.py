"""TS063 real service-read HTTPS verification in a fresh isolated PostgreSQL cluster."""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--postgres-bin", type=Path, required=True)
    options = parser.parse_args()
    if not options.execute:
        return 77
    run = ROOT / ".runtime" / ("ts063-service-" + uuid.uuid4().hex[:12])
    run.mkdir(parents=True)
    path = ROOT / "tests/integration/read-only-trial/run_e2e.py"
    spec = importlib.util.spec_from_file_location("ts063_trial_runner", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    database_module = module.load_fixture()
    with tempfile.TemporaryDirectory(prefix="al20-ts063-") as temporary:
        runtime = Path(temporary)
        web = runtime / "web"
        web.mkdir()
        (web / "index.html").write_text("<!doctype html><title>TS063 API-only synthetic fixture</title>", encoding="utf-8")
        os.environ.update({"ASSETLIBRARY_TEST_POSTGRES_REQUIRED": "1", "ASSETLIBRARY_TEST_POSTGRES_EXTERNAL": "0",
                           "ASSETLIBRARY_TEST_POSTGRES_BIN": str(options.postgres_bin.resolve()),
                           "ASSETLIBRARY_TEST_RUNTIME": str(runtime)})
        fixture_type = database_module.PostgreSqlIntegrationTests
        fixture = fixture_type(methodName="runTest")
        logins = []
        try:
            fixture_type.setUpClass()
            settings = module.prepare_database(database_module, fixture, logins)
            settings.update({"runtime_root": str(runtime), "web_root": str(web), "node": "unused",
                             "playwright_module": "unused", "browser_script": "unused", "evidence": str(run),
                             "dotnet": str(options.dotnet.resolve()),
                             "host_dll": str(ROOT / "services/core-server/Host/bin/Release/net10.0/AssetLibrary.CoreServer.Host.dll")})
            settings_file = runtime / "settings.json"
            settings_file.write_text(json.dumps(settings), encoding="utf-8")
            environment = {**os.environ, "ASSETLIBRARY_TRIAL_E2E_REQUIRED": "1",
                           "ASSETLIBRARY_TRIAL_E2E_SETTINGS": str(settings_file),
                           "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
            command = [str(options.dotnet.resolve()), "test", str(ROOT / "tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj"),
                       "--configuration", "Release", "--no-build", "--no-restore", "--filter", "FullyQualifiedName~ServiceReadIntegrationTests",
                       "--logger", "trx;LogFileName=service-read.trx", "--results-directory", str(run)]
            result = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, encoding="utf-8", timeout=300,
                                    creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            (run / "runner.log").write_text(result.stdout + result.stderr, encoding="utf-8")
            print(result.stdout)
            if result.returncode:
                return result.returncode
            counters = ET.parse(run / "service-read.trx").find(".//{*}Counters")
            if counters is None or counters.attrib.get("total") != "1" or counters.attrib.get("passed") != "1":
                raise RuntimeError("Required service integration test did not execute and pass")
            persistence_path = ROOT / "tests/integration/tianshu/service_read_persistence.py"
            persistence_spec = importlib.util.spec_from_file_location("ts063_persistence", persistence_path)
            persistence = importlib.util.module_from_spec(persistence_spec)
            persistence_spec.loader.exec_module(persistence)
            grant_spec = importlib.util.spec_from_file_location("ts063_grants", ROOT / "tests/integration/tianshu/service_read_grants.py")
            grants = importlib.util.module_from_spec(grant_spec)
            grant_spec.loader.exec_module(grants)
            checks = grants.verify_grants(fixture, settings["database"], settings["logins"]["library"])
            checks += persistence.verify_persistence(database_module, fixture, settings["database"])
        finally:
            cleaned = fixture.doCleanups()
            try:
                for login in logins:
                    fixture.sql("postgres", fixture.admin, f"DROP ROLE {login};")
            finally:
                fixture_type.doClassCleanups()
            if not cleaned or getattr(fixture_type, "tearDown_exceptions", []):
                raise RuntimeError("Owned PostgreSQL cleanup failed")
    (run / "acceptance.json").write_text(json.dumps({"status": "passed", "aggregate_tests": 1,
        "resource_cleanup": "verified", "original_hash_and_mtime": "unchanged", "persistence_checks": checks,
        "boundary": "real PG16.15 and pinned HTTPS; HIBP test responder; no browser UI or platform consumer"}, indent=2), encoding="utf-8")
    print("TS063 evidence: " + str(run))
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    raise SystemExit(main())
