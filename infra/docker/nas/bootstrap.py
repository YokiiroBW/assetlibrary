#!/usr/bin/env python3
"""One-shot NAS deployment jobs; application behavior stays in the existing Host."""
from __future__ import annotations

import argparse
import contextlib
import getpass
import hashlib
import importlib.util
import io
import ipaddress
import json
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import stat
import subprocess
import sys
import tempfile
from datetime import datetime, timedelta, timezone
from urllib.parse import urlsplit
import uuid


APP = Path("/var/lib/assetlibrary")
CONTROL = Path("/bootstrap/control")
PG_DATA = Path("/bootstrap/postgres-data")
PG_TLS = Path("/bootstrap/postgres-tls")
APP_UID = 1654
PG_UID = 999
PRODUCT = "AssetLibrary/NAS/read-only/v1"
LOGIN_ROLES = {
    "audit": "assetlibrary_database_auditor",
    "gateway": "assetlibrary_gateway_auth_runtime",
    "library": "assetlibrary_library_storage_runtime",
    "asset": "assetlibrary_asset_identity_runtime",
    "scan": "assetlibrary_scan_reconciliation_runtime",
    "task": "assetlibrary_task_health_runtime",
    "migrator": "assetlibrary_migration_executor",
}


class DeploymentError(RuntimeError):
    pass


def read_json(path: Path) -> dict:
    require_path(path)
    if path.stat().st_size > 65536:
        raise DeploymentError("deployment_document_too_large")
    return json.loads(path.read_text(encoding="utf-8"))


def require_path(path: Path) -> None:
    if not path.is_absolute():
        raise DeploymentError("deployment_path_not_absolute")
    for item in (path, *path.parents):
        if item.is_symlink():
            raise DeploymentError("deployment_link_rejected")


def private_write(path: Path, content: bytes, uid: int = 0, *, replace: bool = False) -> None:
    require_path(path)
    if path.exists() and not replace:
        raise DeploymentError("deployment_file_already_exists")
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("xb") as stream:
            os.fchmod(stream.fileno(), 0o600)
            os.fchown(stream.fileno(), uid, uid)
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def write_json(path: Path, value: dict, uid: int = 0, *, replace: bool = False) -> None:
    private_write(path, (json.dumps(value, sort_keys=True, ensure_ascii=True) + "\n").encode(), uid, replace=replace)


def settings(value: dict) -> dict:
    if set(value) != {"deployment_name", "public_origin", "bind_address", "storage_sources"}:
        raise DeploymentError("deployment_settings_fields_invalid")
    if not re.fullmatch(r"assetlibrary-[a-z0-9-]{1,32}", value["deployment_name"]):
        raise DeploymentError("deployment_name_invalid")
    origin = urlsplit(value["public_origin"])
    if (origin.scheme != "https" or origin.username or origin.password or origin.path not in ("", "/")
            or origin.query or origin.fragment or origin.port is None or not 1024 <= origin.port <= 65535):
        raise DeploymentError("deployment_https_origin_invalid")
    host = origin.hostname or ""
    try:
        ipaddress.ip_address(host)
    except ValueError:
        if not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?", host):
            raise DeploymentError("deployment_hostname_invalid")
    bind = ipaddress.ip_address(value["bind_address"])
    if bind.version != 4:
        raise DeploymentError("deployment_ipv4_bind_required")
    sources = value["storage_sources"]
    if not isinstance(sources, list) or not 1 <= len(sources) <= 32:
        raise DeploymentError("deployment_sources_invalid")
    keys: set[str] = set()
    roots: list[PurePosixPath] = []
    for source in sources:
        if set(source) != {"source_key", "display_name", "host_path"}:
            raise DeploymentError("deployment_source_fields_invalid")
        key = source["source_key"]
        root = PurePosixPath(source["host_path"])
        if (not re.fullmatch(r"[a-z0-9_-]{1,64}", key) or key in keys
                or not isinstance(source["display_name"], str) or not 1 <= len(source["display_name"]) <= 200
                or not root.is_absolute() or root == PurePosixPath("/") or source["host_path"].startswith("//") or ".." in root.parts
                or str(root) != source["host_path"]
                or any(ord(c) < 32 for c in source["host_path"] + source["display_name"])):
            raise DeploymentError("deployment_source_invalid")
        if any(root == previous or root in previous.parents or previous in root.parents for previous in roots):
            raise DeploymentError("deployment_source_overlap")
        roots.append(root)
        keys.add(key)
    return value


def settings_digest(value: dict) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def verify_image_container(document: object, deployment: str, name: str, image_id: str,
                           revision: str, *, running: bool) -> dict:
    """Read Docker's actual container configuration, never infer it from Ready IPC."""
    if (not isinstance(document, list) or len(document) != 1 or not isinstance(document[0], dict)
            or not re.fullmatch(r"assetlibrary-[a-z0-9-]{1,32}", name)
            or not re.fullmatch(r"sha256:[0-9a-f]{64}", image_id)
            or not re.fullmatch(r"[0-9a-f]{40}", revision)):
        raise DeploymentError("deployment_image_inspection_invalid")
    deployment = str(uuid.UUID(deployment))
    container = document[0]
    config, host, state = container.get("Config", {}), container.get("HostConfig", {}), container.get("State", {})
    labels = config.get("Labels", {})
    if (container.get("Image") != image_id or labels.get("io.assetlibrary.deployment") != deployment
            or labels.get("io.assetlibrary.product") != "nas-read-only-v1"
            or labels.get("io.assetlibrary.role") != "nas-image-supervisor"
            or labels.get("org.opencontainers.image.revision") != revision
            or labels.get("com.docker.compose.project") != name
            or labels.get("com.docker.compose.service") != "image"):
        raise DeploymentError("deployment_image_identity_mismatch")
    executable = "/app/image-supervisor/AssetLibrary.ImageSupervisor"
    if (config.get("User") != "0:0" or config.get("Entrypoint") != [executable]
            or config.get("Cmd") not in (None, []) or container.get("Path") != executable
            or container.get("Args") not in (None, []) or host.get("Init") not in (None, False)):
        raise DeploymentError("deployment_image_pid1_invalid")
    caps = lambda values: {value.removeprefix("CAP_") for value in values or []}
    if (caps(host.get("CapAdd")) != {"CHOWN", "SETUID", "SETGID", "KILL"}
            or caps(host.get("CapDrop")) != {"ALL"}
            or set(host.get("SecurityOpt") or []) not in ({"no-new-privileges"}, {"no-new-privileges:true"}, {"no-new-privileges=true"})
            or host.get("Privileged") is not False or host.get("ReadonlyRootfs") is not True):
        raise DeploymentError("deployment_image_privilege_policy_invalid")
    if (host.get("NetworkMode") != "none" or host.get("PidMode") not in ("", "private")
            or host.get("IpcMode") != "private" or host.get("UTSMode") not in ("", "private")
            or host.get("PortBindings") or host.get("PublishAllPorts") or config.get("ExposedPorts")
            or host.get("Devices") or host.get("DeviceRequests") or host.get("VolumesFrom")):
        raise DeploymentError("deployment_image_namespace_policy_invalid")
    environment = config.get("Env") or []
    if (not isinstance(environment, list) or len(environment) > 1
            or any(not isinstance(value, str) or not value.startswith("PATH=") or len(value) > 4096 for value in environment)
            or host.get("Tmpfs")):
        raise DeploymentError("deployment_image_environment_policy_invalid")
    if host.get("Memory") != 512 * 1024 * 1024 or host.get("CpuShares") != 256:
        raise DeploymentError("deployment_image_limits_invalid")
    if host.get("RestartPolicy", {}).get("Name") != "unless-stopped":
        raise DeploymentError("deployment_image_restart_policy_invalid")
    mounts = container.get("Mounts", [])
    if (len(mounts) != 1 or mounts[0].get("Type") != "volume"
            or mounts[0].get("Name") != name + "-image-ipc"
            or mounts[0].get("Destination") != "/run/assetlibrary-image" or mounts[0].get("RW") is not True):
        raise DeploymentError("deployment_image_mount_policy_invalid")
    if config.get("Healthcheck", {}).get("Test") != ["CMD", executable, "--health"]:
        raise DeploymentError("deployment_image_health_command_invalid")
    if running and (state.get("Running") is not True or state.get("Pid", 0) <= 0
                    or state.get("Health", {}).get("Status") != "healthy"):
        raise DeploymentError("deployment_image_unavailable")
    return {"status": "image_healthy" if running else "image_configuration_verified",
            "configuration_verified": True, "target_platform_probes": "separate_evidence_required"}


def initialize_volume(path: Path, role: str, deployment: str, uid: int) -> None:
    require_path(path)
    marker = path / ".assetlibrary-owner.json"
    expected = {"product": PRODUCT, "deployment_id": deployment, "volume_role": role}
    if marker.exists():
        if read_json(marker) != expected:
            raise DeploymentError("deployment_volume_owner_mismatch")
        if path.stat().st_uid != uid or stat.S_IMODE(path.stat().st_mode) & 0o077:
            raise DeploymentError("deployment_volume_permissions_changed")
        if marker.stat().st_uid != 0 or stat.S_IMODE(marker.stat().st_mode) & 0o077:
            raise DeploymentError("deployment_volume_marker_permissions_changed")
        return
    if any(path.iterdir()):
        raise DeploymentError("deployment_foreign_volume_rejected")
    os.chmod(path, 0o700)
    write_json(marker, expected)
    os.chown(path, uid, uid)


def openssl(*arguments: str) -> None:
    result = subprocess.run(["openssl", *arguments], capture_output=True, timeout=60, check=False)
    if result.returncode:
        raise DeploymentError("deployment_certificate_generation_failed")


def create_certificates(value: dict) -> None:
    passwords = read_json(CONTROL / "passwords.json")
    with tempfile.TemporaryDirectory(prefix="pki-", dir=CONTROL) as temporary:
        work = Path(temporary)
        ca_key, ca_cert = work / "ca.key", work / "ca.crt"
        openssl("req", "-x509", "-newkey", "rsa:3072", "-sha256", "-nodes", "-days", "3650",
                "-subj", "/CN=AssetLibrary private PostgreSQL CA", "-keyout", str(ca_key), "-out", str(ca_cert),
                "-addext", "basicConstraints=critical,CA:TRUE", "-addext", "keyUsage=critical,keyCertSign,cRLSign")
        pg_key, pg_request, pg_cert = work / "pg.key", work / "pg.csr", work / "pg.crt"
        openssl("req", "-new", "-newkey", "rsa:3072", "-nodes", "-subj", "/CN=postgres",
                "-keyout", str(pg_key), "-out", str(pg_request))
        extensions = work / "pg.ext"
        extensions.write_text("subjectAltName=DNS:postgres\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n", encoding="ascii")
        openssl("x509", "-req", "-in", str(pg_request), "-CA", str(ca_cert), "-CAkey", str(ca_key),
                "-set_serial", "0x" + secrets.token_hex(16), "-days", "825", "-sha256", "-extfile", str(extensions), "-out", str(pg_cert))
        hostname = urlsplit(value["public_origin"]).hostname
        try:
            ipaddress.ip_address(hostname)
            san = "IP:" + hostname
        except ValueError:
            san = "DNS:" + hostname
        web_key, web_cert, web_pfx = work / "web.key", work / "web.crt", work / "web.pfx"
        openssl("req", "-x509", "-newkey", "rsa:3072", "-sha256", "-nodes", "-days", "90",
                "-subj", "/CN=AssetLibrary NAS HTTPS", "-keyout", str(web_key), "-out", str(web_cert),
                "-addext", "subjectAltName=" + san, "-addext", "basicConstraints=critical,CA:FALSE",
                "-addext", "keyUsage=critical,digitalSignature,keyEncipherment", "-addext", "extendedKeyUsage=serverAuth")
        pfx_password = work / "pfx-password"
        pfx_password.write_text(passwords["tls"], encoding="ascii")
        openssl("pkcs12", "-export", "-inkey", str(web_key), "-in", str(web_cert), "-out", str(web_pfx), "-passout", "file:" + str(pfx_password))
        for path, content, uid in (
            (CONTROL / "postgres-ca.key", ca_key.read_bytes(), 0),
            (CONTROL / "postgres-ca.crt", ca_cert.read_bytes(), 0),
            (PG_TLS / "server.key", pg_key.read_bytes(), PG_UID),
            (PG_TLS / "server.crt", pg_cert.read_bytes(), PG_UID),
            (PG_TLS / "ca.crt", ca_cert.read_bytes(), PG_UID),
            (APP / "secrets/postgres-ca.crt", ca_cert.read_bytes(), APP_UID),
            (APP / "tls/server.pfx", web_pfx.read_bytes(), APP_UID),
            (APP / "tls/browser-certificate.crt", web_cert.read_bytes(), APP_UID),
            (APP / "secrets/tls-password", passwords["tls"].encode(), APP_UID),
        ):
            private_write(path, content, uid)


def prepare(value: dict, deployment: str) -> dict:
    for path, role, uid in ((CONTROL, "setup", 0), (APP, "core", APP_UID), (PG_DATA, "postgres-data", PG_UID), (PG_TLS, "postgres-tls", PG_UID)):
        initialize_volume(path, role, deployment, uid)
    marker = CONTROL / "prepared.json"
    expected = {"deployment_id": deployment, "settings_sha256": settings_digest(value)}
    if marker.exists():
        if read_json(marker) != expected:
            raise DeploymentError("deployment_configuration_drift")
        return {"status": "already_prepared"}
    for name in ("secrets", "tls", "keys", "data-protection"):
        path = APP / name
        require_path(path)
        path.mkdir(mode=0o700, exist_ok=True)
        os.chown(path, APP_UID, APP_UID)
    passwords_path = CONTROL / "passwords.json"
    if not passwords_path.exists():
        write_json(passwords_path, {name: secrets.token_urlsafe(40) for name in (*LOGIN_ROLES, "cluster_admin", "tls")})
    if any((path / filename).exists() for path, filename in ((CONTROL, "postgres-ca.key"), (APP, "tls/server.pfx"))):
        raise DeploymentError("deployment_incomplete_pki_preserved")
    create_certificates(value)
    passwords = read_json(passwords_path)
    private_write(PG_TLS / "superuser-password", passwords["cluster_admin"].encode(), PG_UID)
    private_write(PG_TLS / "pg_hba.conf", b"local all all scram-sha-256\nhostssl all all 0.0.0.0/0 scram-sha-256\nhostssl all all ::/0 scram-sha-256\nhostnossl all all 0.0.0.0/0 reject\nhostnossl all all ::/0 reject\n", PG_UID)
    database = {}
    for name in LOGIN_ROLES:
        if name == "migrator":
            continue
        path = APP / f"secrets/{name}.connection"
        connection = f"Host=postgres;Port=5432;Database=assetlibrary;Username=assetlibrary_nas_{name};Password={passwords[name]};SSL Mode=VerifyFull;Root Certificate={APP}/secrets/postgres-ca.crt;Timeout=5;Command Timeout=15"
        private_write(path, connection.encode(), APP_UID)
        database[name + "_connection_file"] = str(path)
    configuration = {
        "format_version": 1, "deployment_id": deployment, "public_origin": value["public_origin"], "bind_host": "0.0.0.0",
        "state_path": str(APP), "tls_certificate_file": str(APP / "tls/server.pfx"),
        "tls_certificate_password_file": str(APP / "secrets/tls-password"), "decryption_certificates": [],
        "data_protection_path": str(APP / "data-protection"), "authorization_key_file": str(APP / "keys/authorization.json"),
        "web_root": "/app/wwwroot", "database": database,
        "storage_sources": [{"source_key": source["source_key"], "display_name": source["display_name"],
                             "storage_source_id": str(uuid.uuid4()), "allowed_root": "/assets/" + source["source_key"], "case_sensitive": True}
                            for source in value["storage_sources"]],
    }
    write_json(APP / "trial.json", configuration, APP_UID)
    write_json(marker, expected)
    return {"status": "prepared", "asset_writes": False, "certificate_trust": "manual_required"}


def migration_module():
    spec = importlib.util.spec_from_file_location("nas_migrations", "/bootstrap/migrations/migration_tool.py")
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def migrate(deployment: str) -> dict:
    module = migration_module()
    manifest = module.load_manifest(Path("/bootstrap/migrations/manifest.json"))
    passwords = read_json(CONTROL / "passwords.json")
    def connect(name: str):
        connection = module.Connection("postgres", 5432, "assetlibrary_nas_" + name, "assetlibrary", "assetlibrary", Path("/usr/lib/postgresql/16/bin"))
        tools = module.PostgresTools(manifest, connection)
        tools.environment.update({"PGPASSWORD": passwords[name], "PGSSLMODE": "verify-full", "PGSSLROOTCERT": str(CONTROL / "postgres-ca.crt")})
        return tools
    admin = connect("cluster_admin")
    system_id = admin.psql("SELECT system_identifier FROM pg_control_system();").strip()
    marker = CONTROL / "cluster.json"
    identity = {"deployment_id": deployment, "system_identifier": system_id}
    if marker.exists() and read_json(marker) != identity:
        raise DeploymentError("deployment_postgres_identity_changed")
    if not marker.exists():
        write_json(marker, identity)
    with contextlib.redirect_stdout(io.StringIO()):
        module.provision_roles(admin, manifest, explicit=True)
    statements = ["BEGIN;"]
    for name, role in LOGIN_ROLES.items():
        login = "assetlibrary_nas_" + name
        exists = admin.psql(f"SELECT 1 FROM pg_roles WHERE rolname='{login}';").strip() == "1"
        if exists:
            safe = admin.psql(f"SELECT rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_roles WHERE rolname='{login}';").strip()
            grants = admin.psql(f"SELECT r.rolname, m.inherit_option, m.set_option, m.admin_option FROM pg_auth_members m JOIN pg_roles r ON r.oid=m.roleid JOIN pg_roles u ON u.oid=m.member WHERE u.rolname='{login}';").strip()
            if safe != "t" or grants not in ("", f"{role}|f|t|f"):
                raise DeploymentError("deployment_existing_login_unsafe")
            connect(name).psql("SELECT 1;")
        else:
            statements.append(f"CREATE ROLE {login} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD {module.sql_literal(passwords[name])};")
        statements += [f"GRANT {role} TO {login} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;", f"GRANT CONNECT ON DATABASE assetlibrary TO {login};"]
    admin.psql("\n".join([*statements, "COMMIT;"]))
    for name, role in LOGIN_ROLES.items():
        login = "assetlibrary_nas_" + name
        attributes = admin.psql(f"SELECT rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_roles WHERE rolname='{login}';").strip()
        membership = admin.psql(f"SELECT r.rolname, m.inherit_option, m.set_option, m.admin_option FROM pg_auth_members m JOIN pg_roles r ON r.oid=m.roleid JOIN pg_roles u ON u.oid=m.member WHERE u.rolname='{login}';").strip()
        if attributes != "t" or membership != f"{role}|f|t|f":
            raise DeploymentError("deployment_runtime_role_unsafe")
        connect(name).psql(f"SET ROLE {role}; SELECT 1;")
    with contextlib.redirect_stdout(io.StringIO()):
        module.apply_migrations(connect("migrator"), manifest, CONTROL / "backups")
    return {"status": "migrated", "migrations": len(manifest.migrations), "module_logins": 6}


def drop_to_application() -> None:
    os.setgroups([])
    os.setgid(APP_UID)
    os.setuid(APP_UID)


def operator(args: argparse.Namespace) -> int:
    request = ""
    if args.operation in ("bootstrap", "recover"):
        if not args.account or (args.operation == "bootstrap" and not args.display_name):
            raise DeploymentError("deployment_operator_account_required")
        attempt_file = CONTROL / "operator-attempt.json"
        if attempt_file.exists() and not args.new_attempt:
            attempt = read_json(attempt_file)
            if attempt["operation"] != args.operation or attempt["request"]["account_name"] != args.account:
                raise DeploymentError("deployment_operator_attempt_conflict")
            if args.operation == "bootstrap" and attempt["request"].get("display_name") != args.display_name:
                raise DeploymentError("deployment_operator_attempt_conflict")
        else:
            payload = {"authorization_id": str(uuid.uuid4()), "operation_id": str(uuid.uuid4()), "account_name": args.account,
                       "expires_at": (datetime.now(timezone.utc) + timedelta(minutes=10)).isoformat()}
            if args.operation == "bootstrap":
                payload["display_name"] = args.display_name
            attempt = {"operation": args.operation, "request": payload}
            write_json(attempt_file, attempt, replace=attempt_file.exists())
        if datetime.fromisoformat(attempt["request"]["expires_at"]) <= datetime.now(timezone.utc):
            raise DeploymentError("deployment_operator_attempt_expired_use_new_attempt")
        password = sys.stdin.buffer.read(16385).decode("utf-8").rstrip("\r\n") if args.password_stdin else getpass.getpass("管理员口令（至少15字符，不显示输入）: ")
        if len(password.encode()) > 16384:
            raise DeploymentError("deployment_operator_input_too_large")
        attempt["request"]["password"] = password
        request = json.dumps(attempt["request"], ensure_ascii=True)
    result = subprocess.run(["/app/AssetLibrary.CoreServer.Host", "--trial-operator", str(APP / "trial.json"), args.operation],
                            input=request, text=True, encoding="utf-8", timeout=30, preexec_fn=drop_to_application, check=False)
    return result.returncode


def main() -> int:
    os.umask(0o077)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("identity", "render-env", "render-mounts", "verify-image-container", "prepare", "migrate", "ensure-key", "operator", "certificate"))
    parser.add_argument("--deployment-id")
    parser.add_argument("--deployment-name")
    parser.add_argument("--image-id")
    parser.add_argument("--source-revision")
    parser.add_argument("--running", action="store_true")
    parser.add_argument("--operation", choices=("initialize-key", "rotate-key", "bootstrap", "recover"))
    parser.add_argument("--account")
    parser.add_argument("--display-name")
    parser.add_argument("--new-attempt", action="store_true")
    parser.add_argument("--password-stdin", action="store_true")
    args = parser.parse_args()
    try:
        if args.action == "identity":
            print(uuid.uuid4())
            return 0
        if args.action == "verify-image-container":
            data = sys.stdin.buffer.read(65537)
            if len(data) > 65536:
                raise DeploymentError("deployment_image_inspection_too_large")
            print(json.dumps(verify_image_container(json.loads(data), args.deployment_id, args.deployment_name,
                                                   args.image_id, args.source_revision, running=args.running)))
            return 0
        if args.action.startswith("render-"):
            data = sys.stdin.buffer.read(65537)
            if len(data) > 65536:
                raise DeploymentError("deployment_settings_too_large")
            value = settings(json.loads(data))
            if args.action == "render-env":
                deployment = str(uuid.UUID(args.deployment_id))
                print(f"ASSETLIBRARY_DEPLOYMENT_NAME={value['deployment_name']}\nASSETLIBRARY_DEPLOYMENT_ID={deployment}\nASSETLIBRARY_BIND_ADDRESS={value['bind_address']}\nASSETLIBRARY_HTTPS_PORT={urlsplit(value['public_origin']).port}\nASSETLIBRARY_SETTINGS_SHA256={settings_digest(value)}")
            else:
                print(json.dumps({"services": {"core": {"volumes": [{"type": "bind", "source": source["host_path"], "target": "/assets/" + source["source_key"], "read_only": True, "bind": {"create_host_path": False}} for source in value["storage_sources"]]}}}))
            return 0
        deployment = str(uuid.UUID(os.environ["ASSETLIBRARY_DEPLOYMENT_ID"]))
        value = settings(read_json(Path("/bootstrap/settings.json")))
        if args.action == "prepare":
            result = prepare(value, deployment)
        else:
            if read_json(CONTROL / "prepared.json") != {"deployment_id": deployment, "settings_sha256": settings_digest(value)}:
                raise DeploymentError("deployment_configuration_drift")
            if args.action == "migrate":
                result = migrate(deployment)
            elif args.action == "ensure-key":
                key = APP / "keys/authorization.json"
                require_path(key)
                if key.exists():
                    if key.stat().st_uid != APP_UID or stat.S_IMODE(key.stat().st_mode) & 0o077:
                        raise DeploymentError("deployment_authorization_key_permissions_invalid")
                    result = {"status": "authorization_key_present"}
                else:
                    args.operation = "initialize-key"
                    return operator(args)
            elif args.action == "operator":
                if not args.operation:
                    raise DeploymentError("deployment_operator_action_required")
                return operator(args)
            else:
                sys.stdout.write((APP / "tls/browser-certificate.crt").read_text(encoding="ascii"))
                return 0
        print(json.dumps(result, sort_keys=True))
        return 0
    except DeploymentError as error:
        print(json.dumps({"status": "failed", "code": str(error)}), file=sys.stderr)
        return 1
    except Exception:
        print('{"status":"failed","code":"deployment_operation_failed"}', file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
