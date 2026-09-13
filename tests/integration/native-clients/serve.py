"""Serve the real Core and an owned temporary PostgreSQL for native clients."""
from __future__ import annotations

import argparse
import ctypes
import hashlib
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import sys
import tempfile
import time
from urllib.parse import urlsplit
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
MAXIMUM_SECONDS = 7200


def lifetime(value: str) -> int:
    result = int(value)
    if not 1 <= result <= MAXIMUM_SECONDS:
        raise argparse.ArgumentTypeError("lifetime must be between 1 and 7200 seconds")
    return result


def arguments(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--dotnet", required=True, type=Path)
    parser.add_argument("--postgres-bin", required=True, type=Path)
    parser.add_argument("--web-root", required=True, type=Path)
    parser.add_argument("--lifetime-seconds", type=lifetime, default=MAXIMUM_SECONDS)
    parser.add_argument("--evidence", type=Path, default=ROOT / ".runtime/native-clients-evidence")
    parser.add_argument("--image-fixtures", type=Path, help="Explicit synthetic preview corpus with its generated manifest")
    parser.add_argument("--image-preview-worker", type=Path, help="Explicit published isolated decoder; no fixture bypass")
    return parser.parse_args(argv)


def existing_runner():
    source = ROOT / "tests/integration/read-only-trial/run_e2e.py"
    spec = importlib.util.spec_from_file_location("native_client_existing_trial", source)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def stage_image_fixtures(source: Path, runtime: Path) -> Path:
    if source.is_symlink() or source.is_junction():
        raise ValueError("synthetic image corpus root must not be a link")
    source = source.resolve(strict=True)
    manifest_path = source / "manifest.json"
    if manifest_path.is_symlink() or manifest_path.stat().st_size > 131072:
        raise ValueError("synthetic image manifest is invalid or exceeds its bound")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    entries = manifest.get("files", [])
    if manifest.get("kind") != "synthetic_preview_integration_inputs" or not isinstance(entries, list) or not 1 <= len(entries) <= 128:
        raise ValueError("only an explicit bounded synthetic preview corpus may be staged")
    destination = runtime / "native-image-fixtures"
    destination.mkdir(exist_ok=False)
    total = 0
    seen = set()
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("synthetic image manifest entries must be objects")
        relative = entry.get("path", "")
        if not isinstance(relative, str) or len(relative) > 512:
            raise ValueError("synthetic image path is invalid")
        parts = relative.split("/")
        if not relative or "\\" in relative or ":" in relative or any(part in ("", ".", "..") for part in parts) or len(parts) > 16:
            raise ValueError("synthetic image path escaped its declared corpus")
        if relative.casefold() in seen:
            raise ValueError("synthetic image paths collide")
        seen.add(relative.casefold())
        original = source.joinpath(*parts)
        part = original
        while part != source:
            if part.is_symlink() or part.is_junction():
                raise ValueError("synthetic image corpus must not contain links")
            part = part.parent
        if not original.is_file() or not original.resolve().is_relative_to(source) or not 0 < original.stat().st_size <= 33554432:
            raise ValueError("synthetic image input is missing, nonregular or too large")
        target = destination.joinpath(*parts)
        target.parent.mkdir(parents=True, exist_ok=True)
        count = 0
        digest = hashlib.sha256()
        with original.open("rb") as incoming, target.open("xb") as outgoing:
            while chunk := incoming.read(65536):
                count += len(chunk)
                total += len(chunk)
                if count > 33554432 or total > 67108864:
                    raise ValueError("synthetic image copy exceeded its bound")
                digest.update(chunk)
                outgoing.write(chunk)
        if count != entry.get("bytes") or digest.hexdigest() != entry.get("sha256"):
            raise ValueError("synthetic image changed since its generated manifest")
    return destination


def process_exists(identifier: int) -> bool:
    if os.name != "nt":
        try:
            os.kill(identifier, 0)
            return True
        except ProcessLookupError:
            return False
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
    kernel.WaitForSingleObject.restype = ctypes.c_ulong
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = kernel.OpenProcess(0x00100000, False, identifier)
    if not handle:
        if ctypes.get_last_error() == 5:
            raise RuntimeError("process cleanup could not be checked")
        return False
    try:
        return kernel.WaitForSingleObject(handle, 0) == 258
    finally:
        kernel.CloseHandle(handle)


def terminate_owned_process(process: subprocess.Popen) -> None:
    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                       capture_output=True, check=False, timeout=20,
                       creationflags=subprocess.CREATE_NO_WINDOW)
    else:
        os.killpg(process.pid, signal.SIGKILL)
    process.wait(timeout=20)


def read_ready(evidence: Path, runtime: Path) -> dict | None:
    path = evidence / "ready.json"
    if not path.exists():
        return None
    ready = json.loads(path.read_text(encoding="utf-8"))
    for key in ("connection_file", "stop_file"):
        resolved = Path(ready[key]).resolve()
        if not resolved.is_relative_to(runtime.resolve()):
            raise RuntimeError("native rendezvous escaped its owned temporary directory")
    origin = urlsplit(ready["origin"])
    if origin.scheme != "https" or origin.hostname != "localhost" or origin.port is None:
        raise RuntimeError("native temporary server must use its localhost TLS identity")
    return ready


def run_host(options, evidence: Path, runtime: Path, settings: dict) -> dict:
    settings.update({
        "runtime_root": str(runtime), "web_root": str(options.web_root.resolve()),
        "node": "", "playwright_module": "", "browser_script": "",
        "evidence": str(evidence), "dotnet": str(options.dotnet.resolve()),
        "host_dll": str(ROOT / "services/core-server/Host/bin/Release/net10.0/AssetLibrary.CoreServer.Host.dll"),
    })
    settings_file = runtime / "native-settings.json"
    settings_file.write_text(json.dumps(settings), encoding="utf-8")
    environment = os.environ.copy()
    environment.update({
        "ASSETLIBRARY_TRIAL_E2E_REQUIRED": "1", "ASSETLIBRARY_TRIAL_E2E_SETTINGS": str(settings_file),
        "ASSETLIBRARY_NATIVE_CLIENT_REQUIRED": "1",
        "ASSETLIBRARY_NATIVE_CLIENT_SECONDS": str(options.lifetime_seconds),
        "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    })
    environment.pop("ASSETLIBRARY_NATIVE_IMAGE_FIXTURES", None)
    environment.pop("ASSETLIBRARY_IMAGE_PREVIEW_WORKER", None)
    if options.image_fixtures is not None:
        staged = stage_image_fixtures(options.image_fixtures, runtime)
        environment["ASSETLIBRARY_NATIVE_IMAGE_FIXTURES"] = str(staged)
    if options.image_preview_worker is not None:
        worker = options.image_preview_worker
        if not worker.is_absolute() or not worker.is_file():
            raise ValueError("the explicit image decoder must be an existing absolute executable")
        environment["ASSETLIBRARY_IMAGE_PREVIEW_WORKER"] = str(worker.resolve())
    command = [str(options.dotnet), "test",
               str(ROOT / "tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj"),
               "--configuration", "Release", "--no-build", "--no-restore",
               "--filter", "FullyQualifiedName~NativeClientIntegrationServerTests",
               "--logger", "trx;LogFileName=native-host.trx", "--results-directory", str(evidence)]
    ready = None
    deadline = time.monotonic() + 180
    with (evidence / "host.log").open("w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT,
                                   start_new_session=os.name != "nt",
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        try:
            while process.poll() is None:
                if ready is None:
                    ready = read_ready(evidence, runtime)
                    if ready is not None:
                        deadline = time.monotonic() + options.lifetime_seconds + 60
                        print("NATIVE_CLIENT_READY " + json.dumps(ready), flush=True)
                if time.monotonic() >= deadline:
                    raise RuntimeError("native server exceeded its startup or lifetime deadline")
                time.sleep(0.2)
        except KeyboardInterrupt:
            if ready is not None:
                Path(ready["stop_file"]).touch(exist_ok=True)
                process.wait(timeout=45)
            raise
        finally:
            terminate_owned_process(process)
    if process.returncode != 0 or ready is None:
        raise RuntimeError("real native server fixture failed; inspect its local host.log")
    counters = ET.parse(evidence / "native-host.trx").find(".//{*}Counters")
    if counters is None or counters.attrib.get("total") != "1" or counters.attrib.get("passed") != "1":
        raise RuntimeError("real native fixture did not execute and pass exactly once")
    receipt = json.loads((evidence / "host-cleanup.json").read_text(encoding="utf-8"))
    if receipt.get("status") != "disposed" or receipt.get("sample_hashes_and_mtimes") != "unchanged":
        raise RuntimeError("real host disposal and original integrity were not verified")
    if process_exists(ready["host_process_id"]):
        raise RuntimeError("native test host process remained after disposal")
    with socket.socket() as probe:
        probe.settimeout(1)
        if probe.connect_ex(("127.0.0.1", urlsplit(ready["origin"]).port)) == 0:
            raise RuntimeError("the native fixture HTTPS listener remained open")
    return receipt


def main(argv: list[str] | None = None) -> int:
    options = arguments(argv)
    if not options.execute:
        print(json.dumps({"status": "not_executed", "reason": "pass --execute to create owned test resources"}))
        return 77
    for path in (options.dotnet, options.web_root / "index.html",
                 ROOT / "services/core-server/Host/bin/Release/net10.0/AssetLibrary.CoreServer.Host.dll"):
        if not path.is_file():
            raise RuntimeError("a required exact SDK or built production artifact is missing")
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8]
    evidence = options.evidence.resolve() / run_id
    evidence.mkdir(parents=True, exist_ok=False)
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    (evidence / "run.json").write_text(json.dumps({
        "run_id": run_id, "source_revision": revision,
        "working_tree_dirty": bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip()),
        "web_root": str(options.web_root.resolve()), "lifetime_seconds": options.lifetime_seconds,
        "client_acceptance": "recorded separately by each native client",
    }, indent=2), encoding="utf-8")
    print(f"NATIVE_CLIENT_EVIDENCE {evidence}", flush=True)
    runner = existing_runner()
    module = runner.load_fixture()
    with tempfile.TemporaryDirectory(prefix="al20-native-") as temporary:
        runtime = Path(temporary)
        os.environ.update({
            "ASSETLIBRARY_TEST_POSTGRES_REQUIRED": "1", "ASSETLIBRARY_TEST_POSTGRES_EXTERNAL": "0",
            "ASSETLIBRARY_TEST_POSTGRES_BIN": str(options.postgres_bin.resolve()),
            "ASSETLIBRARY_TEST_RUNTIME": str(runtime),
        })
        fixture_type = module.PostgreSqlIntegrationTests
        fixture = fixture_type(methodName="runTest")
        created_logins: list[str] = []
        postgres_pid = None
        settings = None
        try:
            fixture_type.setUpClass()
            postgres_pid = int((fixture_type.cluster_data / "postmaster.pid").read_text().splitlines()[0])
            settings = runner.prepare_database(module, fixture, created_logins)
            receipt = run_host(options, evidence, runtime, settings)
        finally:
            try:
                cleaned = fixture.doCleanups()
                if settings is not None:
                    database_count = fixture.sql("postgres", fixture.admin,
                                                 "SELECT COUNT(*) FROM pg_database WHERE datname = '"
                                                 + settings["database"] + "';").stdout.strip()
                    if database_count != "0":
                        raise RuntimeError("native temporary database was not removed")
                for login in created_logins:
                    fixture.sql("postgres", fixture.admin, f"DROP ROLE {login};")
                if created_logins:
                    names = ",".join("'" + login + "'" for login in created_logins)
                    count = fixture.sql("postgres", fixture.admin,
                                        f"SELECT COUNT(*) FROM pg_roles WHERE rolname IN ({names});").stdout.strip()
                    if count != "0" or not cleaned:
                        raise RuntimeError("native temporary database and role cleanup failed")
            finally:
                fixture_type.doClassCleanups()
            if getattr(fixture_type, "tearDown_exceptions", []):
                raise RuntimeError("temporary PostgreSQL disposal failed")
            if postgres_pid is not None and process_exists(postgres_pid):
                raise RuntimeError("temporary PostgreSQL process remained alive")
    if runtime.exists():
        raise RuntimeError("native private rendezvous or synthetic assets were not removed")
    (evidence / "acceptance.json").write_text(json.dumps({
        **receipt, "status": "passed", "resource_cleanup": "verified",
        "postgres_roles_removed": len(created_logins), "runtime_removed": True,
        "https_listener_closed": True, "host_and_postgres_processes_exited": True,
        "native_client_acceptance": "not asserted by this server fixture",
    }, indent=2), encoding="utf-8")
    print("NATIVE_CLIENT_CLEANUP verified", flush=True)
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    raise SystemExit(main())
