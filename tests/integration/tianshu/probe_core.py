"""Probe only an owned synthetic Core created by the existing native-client fixture."""
from __future__ import annotations

import argparse
import hashlib
import http.cookiejar
import importlib.util
import json
import os
from pathlib import Path
import socket
import ssl
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPCookieProcessor, HTTPSHandler, ProxyHandler, Request, build_opener
import uuid

ROOT = Path(__file__).resolve().parents[3]
NATIVE = ROOT / "tests/integration/native-clients/serve.py"


def require(condition, label):
    if not condition:
        raise AssertionError(label)


def pinned_context(origin, fingerprint):
    """Acquire a leaf without credentials, pin it, then use normal hostname/time validation."""
    address = urlsplit(origin)
    require(address.scheme == "https" and address.hostname == "localhost", "owned loopback origin")
    acquisition = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    acquisition.check_hostname = False
    acquisition.verify_mode = ssl.CERT_NONE
    with socket.create_connection(("127.0.0.1", address.port), timeout=5) as raw:
        with acquisition.wrap_socket(raw, server_hostname="localhost") as connection:
            leaf = connection.getpeercert(binary_form=True)
    require(hashlib.sha256(leaf).hexdigest().lower() == fingerprint.lower(), "owned TLS leaf pin")
    return ssl.create_default_context(cadata=ssl.DER_cert_to_PEM_cert(leaf))


class Client:
    def __init__(self, connection):
        self.origin = connection["origin"]
        self.csrf = None
        self.opener = build_opener(ProxyHandler({}), HTTPCookieProcessor(http.cookiejar.CookieJar()),
                                  HTTPSHandler(context=pinned_context(self.origin, connection["certificate_sha256"])))

    def request(self, path, body=None, csrf=True, origin=True):
        headers = {"Content-Type": "application/json"}
        if origin:
            headers["Origin"] = self.origin
        if csrf and self.csrf:
            headers["X-AssetLibrary-CSRF"] = self.csrf
        data = None if body is None else json.dumps(body).encode()
        request = Request(self.origin + path, data=data, headers=headers)
        try:
            response = self.opener.open(request, timeout=10)
        except HTTPError as error:
            response = error
        with response:
            payload = response.read(1024 * 1024 + 1)
            require(len(payload) <= 1024 * 1024, "bounded HTTP response")
            return response.status, json.loads(payload) if payload else None

    def login(self, account, password):
        status, body = self.request("/assetlink/v1/auth/login", {"account_name": account, "password": password})
        require(status == 200 and body["authenticated"] is True, "synthetic login")
        self.csrf = body["csrf_token"]

    def control(self, operation, body, expected=200, **fields):
        request_id = str(uuid.uuid4())
        status, result = self.request("/assetlink/v1/control", {
            "message_type": "control.request", "request_id": request_id,
            "operation": operation, "body": body, **fields,
        })
        require(status == expected, operation + " HTTP status")
        if status == 200:
            require(result["request_id"] == request_id, operation + " correlation")
            require(result["message_type"] == "control.result" and result["ok"] is True, "success envelope")
            return result["body"]
        require(result["message_type"] == "error" and result["request_id"] in (request_id, "unknown"), "error envelope")
        return result["error"]["code"]


def probe(connection):
    checks = []
    client = Client(connection)
    status, _ = client.request("/assetlink/v1/control", {"message_type": "control.request"})
    require(status == 401, "unauthenticated rejected")
    checks.append("anonymous_control_401")
    client.login(connection["account_name"], connection["password"])
    library = connection["library_id"]
    libraries = client.control("libraries.list", {"page_size": 100})
    require(any(item["library_id"] == library for item in libraries["items"]), "registered library visible")
    checks.append("authorized_library_discovery")
    query = {"library_id": library, "parent_relative_path": "", "page_size": 100, "kind": "files"}
    first = client.control("entries.browse", query)
    require(len(first["items"]) == 100 and first["next_cursor"], "first page boundary")
    second = client.control("entries.browse", {**query, "cursor": first["next_cursor"]})
    ids = [item["entry_id"] for item in first["items"] + second["items"]]
    require(len(ids) == 137 and len(set(ids)) == 137 and second["next_cursor"] is None, "complete two-page scope")
    checks.append("root_files_100_plus_37_unique_entries")
    require(client.control("entries.browse", {**query, "page_size": 101}, 400) == "invalid_request", "page bound")
    require(client.control("entries.browse", {**query, "cursor": first["next_cursor"], "sort_direction": "desc"}, 400)
            == "invalid_request", "cursor scope binding")
    require(client.control("entries.browse", {**query, "parent_relative_path": "../escape"}, 400)
            == "invalid_request", "relative path boundary")
    checks.append("page_cursor_and_traversal_rejected")
    detail = client.control("entries.get", {"library_id": library, "entry_id": ids[0]})
    require(detail["entry"]["entry_id"] == ids[0] and "sha256" not in detail["entry"], "indexed detail without invented hash")
    checks.append("stable_entry_detail_no_hash_claim")
    hits = client.control("assets.search", {"scope": "library", "library_id": library, "query": "说明_中文", "page_size": 100})
    require(len(hits["items"]) == 1 and hits["items"][0]["entry"]["library_id"] == library, "localized scoped search")
    checks.append("localized_scoped_search")
    scan = client.control("library_scans.get", {"library_id": library})["scan"]
    require(scan["state"] == "succeeded" and scan["committed_entries"] >= connection["sample_file_count"], "real committed scan")
    checks.append("real_initial_scan_committed")
    for operation in ("assets.deduplicate", "assets.classify", "assets.prefer", "organization.plan", "organization.execute"):
        require(client.control(operation, {}, 400) == "unsupported_operation", "missing capability is explicit")
    checks.append("five_candidate_operation_names_unsupported")
    envelope = {"message_type": "control.request", "request_id": str(uuid.uuid4()), "operation": "libraries.list", "body": {}}
    require(client.request("/assetlink/v1/control", envelope, csrf=False)[0] == 403, "missing CSRF")
    require(client.request("/assetlink/v1/control", envelope, origin=False)[0] == 403, "missing Origin")
    checks.append("origin_and_csrf_rejected")
    reader = Client(connection)
    reader.login(connection["invisible_account_name"], connection["invisible_account_password"])
    require(reader.control("libraries.list", {})["items"] == [], "invisible library omitted")
    require(reader.control("entries.get", {"library_id": library, "entry_id": ids[0]}, 404) == "not_found", "invisible detail")
    require(reader.control("library_scans.get", {"library_id": library}, 403) == "permission_denied", "management denied")
    checks.append("real_reader_permissions_empty_404_403")
    for session in (reader, client):
        require(session.request("/assetlink/v1/auth/logout", {})[0] == 204, "logout confirmed")
        require(session.request("/assetlink/v1/auth/session")[0] == 401, "revoked session")
    checks.append("both_sessions_revoked")
    return checks


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--dotnet", required=True, type=Path)
    parser.add_argument("--postgres-bin", required=True, type=Path)
    options = parser.parse_args()
    if not options.execute:
        print('{"status":"not_executed"}')
        return 77
    run = ROOT / ".runtime" / ("ts060-http-" + uuid.uuid4().hex[:12])
    run.mkdir(parents=True, exist_ok=False)
    web = run / "static-fixture"
    web.mkdir()
    (web / "index.html").write_text("<!doctype html><title>TS-060 API fixture; Web UI not tested</title>", encoding="utf-8")
    # PostgreSQL backup filenames exceed Windows MAX_PATH under the long worktree.
    # This owned system temporary parent contains only this invocation's fixture.
    with tempfile.TemporaryDirectory(prefix="ts060-") as temp_root:
        return run_probe(options, run, web, Path(temp_root))


def run_probe(options, run, web, temporary):
    environment = {**os.environ, "TEMP": str(temporary), "TMP": str(temporary),
                   "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "PYTHONDONTWRITEBYTECODE": "1"}
    spec = importlib.util.spec_from_file_location("ts060_native", NATIVE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    command = [sys.executable, "-I", "-B", str(NATIVE), "--execute", "--dotnet", str(options.dotnet.resolve()),
               "--postgres-bin", str(options.postgres_bin.resolve()), "--web-root", str(web),
               "--lifetime-seconds", "240", "--evidence", str(run / "server")]
    ready = None
    checks = None
    with (run / "runner.log").open("w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        try:
            deadline = time.monotonic() + 240
            while process.poll() is None and time.monotonic() < deadline:
                files = list((run / "server").glob("*/ready.json"))
                if files:
                    candidate_ready = json.loads(files[0].read_text(encoding="utf-8"))
                    require(all(Path(candidate_ready[key]).resolve().is_relative_to(temporary.resolve())
                                for key in ("connection_file", "stop_file")), "owned rendezvous")
                    ready = candidate_ready
                    break
                time.sleep(0.2)
            require(ready is not None, "real Core did not become ready; inspect private runtime logs")
            checks = probe(json.loads(Path(ready["connection_file"]).read_text(encoding="utf-8")))
        finally:
            try:
                if ready is not None:
                    Path(ready["stop_file"]).touch()
                process.wait(timeout=75 if ready is not None else 10)
            finally:
                module.terminate_owned_process(process)
    require(process.returncode == 0, "server fixture and cleanup must pass")
    receipts = list((run / "server").glob("*/acceptance.json"))
    require(len(receipts) == 1, "one real server receipt")
    receipt = json.loads(receipts[0].read_text(encoding="utf-8"))
    require(receipt["resource_cleanup"] == "verified" and receipt["sample_hashes_and_mtimes"] == "unchanged", "cleanup and integrity")
    report = {"status": "passed", "checks": checks, "server": receipt,
              "web_ui": "not_tested_static_shell_only", "platform": "not_connected",
              "production_publishing": "unavailable", "hibp": "existing_test_handler"}
    (run / "http-checks.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": "passed", "report": str(run / "http-checks.json"), "checks": len(checks)}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
