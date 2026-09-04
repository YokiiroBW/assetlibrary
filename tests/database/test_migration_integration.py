from __future__ import annotations

import concurrent.futures
import hashlib
import importlib.util
import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.error
import urllib.request
import uuid
from pathlib import Path
from typing import NoReturn


ROOT = Path(__file__).resolve().parents[2]
TOOL_PATH = ROOT / "database/migrations/production/migration_tool.py"
SPEC = importlib.util.spec_from_file_location("assetlibrary_migration_integration_tool", TOOL_PATH)
assert SPEC and SPEC.loader
MIGRATIONS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MIGRATIONS
SPEC.loader.exec_module(MIGRATIONS)


class PostgreSqlIntegrationTests(unittest.TestCase):
    ADMIN = "postgres"
    RUNNER = "assetlibrary_v01003_test_runner"
    RUNTIME = "assetlibrary_v01003_test_runtime"
    AUDITOR = "assetlibrary_v01003_test_auditor"

    @classmethod
    def setUpClass(cls) -> None:
        cls.required = os.environ.get("ASSETLIBRARY_TEST_POSTGRES_REQUIRED") == "1"
        configured_bin = os.environ.get("ASSETLIBRARY_TEST_POSTGRES_BIN")
        if not configured_bin:
            cls.unavailable("ASSETLIBRARY_TEST_POSTGRES_BIN is not configured")
        cls.pg_bin = Path(configured_bin).resolve()
        suffix = ".exe" if os.name == "nt" else ""
        for name in ("psql", "pg_dump", "pg_restore"):
            if not (cls.pg_bin / f"{name}{suffix}").is_file():
                cls.unavailable(f"missing PostgreSQL tool: {name}")
        cls.environment = os.environ.copy()
        cls.environment["PGCONNECT_TIMEOUT"] = "5"
        cls.external = os.environ.get("ASSETLIBRARY_TEST_POSTGRES_EXTERNAL") == "1"
        cls.cluster_temp: tempfile.TemporaryDirectory[str] | None = None
        cls.pg_ctl: Path | None = None
        cls.cluster_data: Path | None = None
        cls.addClassCleanup(cls.cleanup_cluster)

        if cls.external:
            cls.host = os.environ.get("ASSETLIBRARY_TEST_POSTGRES_HOST", "127.0.0.1")
            cls.port = int(os.environ.get("ASSETLIBRARY_TEST_POSTGRES_PORT", "5432"))
            cls.admin = os.environ.get("ASSETLIBRARY_TEST_POSTGRES_ADMIN", cls.ADMIN)
        else:
            initdb = cls.pg_bin / f"initdb{suffix}"
            pg_ctl = cls.pg_bin / f"pg_ctl{suffix}"
            if not initdb.is_file() or not pg_ctl.is_file():
                cls.unavailable(
                    "initdb/pg_ctl are required for a self-hosted integration cluster"
                )
            runtime_parent = Path(
                os.environ.get(
                    "ASSETLIBRARY_TEST_RUNTIME",
                    ROOT / ".runtime/sandbox-storage/V01-006",
                )
            ).resolve()
            runtime_parent.mkdir(parents=True, exist_ok=True)
            cls.cluster_temp = tempfile.TemporaryDirectory(
                prefix="postgres-integration-", dir=runtime_parent
            )
            cluster_root = Path(cls.cluster_temp.name)
            data = cluster_root / "data"
            log = cluster_root / "postgres.log"
            cls.pg_ctl = pg_ctl
            cls.cluster_data = data
            cls.host = "127.0.0.1"
            cls.port = cls._unused_port()
            cls.admin = cls.ADMIN
            cls._run(
                [
                    str(initdb),
                    "--pgdata", str(data),
                    "--username", cls.admin,
                    "--auth", "trust",
                    "--encoding", "UTF8",
                    "--no-locale",
                ],
                timeout=60,
            )
            cls._run(
                [
                    str(pg_ctl),
                    "--pgdata", str(data),
                    "--log", str(log),
                    "--wait",
                    "--timeout", "30",
                    "--options",
                    (
                        f"-h {cls.host} -p {cls.port} "
                        "-c fsync=off -c synchronous_commit=off "
                        "-c full_page_writes=off -c max_connections=30"
                    ),
                    "start",
                ],
                timeout=45,
                capture_output=False,
            )
        cls.manifest = MIGRATIONS.load_manifest()
        server_version_num = cls.sql(
            "postgres", cls.admin, "SHOW server_version_num;"
        ).stdout.strip()
        expected_version_num = str(
            cls.manifest.postgresql_major * 10000
            + int(cls.manifest.verified_patch.split(".", 1)[1])
        )
        if server_version_num != expected_version_num:
            cls.unavailable(
                f"integration requires PostgreSQL {cls.manifest.verified_patch}, got {server_version_num}"
            )
        seed = cls.database_name("seed")
        cls.create_database(seed)
        try:
            admin_tools = MIGRATIONS.PostgresTools(
                cls.manifest, cls.connection(seed, cls.admin)
            )
            MIGRATIONS.provision_roles(admin_tools, cls.manifest, explicit=True)
            cls.sql(
                "postgres",
                cls.admin,
                cls.login_role_sql(),
            )
            cls.grant_test_database_access(seed)
        finally:
            cls.drop_database(seed)

    @classmethod
    def unavailable(cls, message: str) -> NoReturn:
        if cls.required:
            raise AssertionError(f"required PostgreSQL integration is unavailable: {message}")
        raise unittest.SkipTest(message)

    @classmethod
    def cleanup_cluster(cls) -> None:
        pg_ctl = getattr(cls, "pg_ctl", None)
        data = getattr(cls, "cluster_data", None)
        if pg_ctl is not None and data is not None:
            subprocess.run(
                [
                    str(pg_ctl),
                    "--pgdata", str(data),
                    "--wait",
                    "--timeout", "30",
                    "--mode", "immediate",
                    "stop",
                ],
                text=True,
                encoding="utf-8",
                errors="replace",
                capture_output=True,
                env=cls.environment,
                timeout=45,
                check=False,
            )
        cluster_temp = getattr(cls, "cluster_temp", None)
        if cluster_temp is not None:
            cluster_temp.cleanup()

    @staticmethod
    def _unused_port() -> int:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as candidate:
            candidate.bind(("127.0.0.1", 0))
            return int(candidate.getsockname()[1])

    @classmethod
    def host_command(cls) -> list[str]:
        configured_dotnet = os.environ.get("ASSETLIBRARY_TEST_DOTNET")
        dotnet = configured_dotnet or shutil.which("dotnet")
        if not dotnet:
            cls.unavailable("dotnet is required for the CoreServer database runtime test")
        configured_host = os.environ.get("ASSETLIBRARY_TEST_HOST_DLL")
        host = Path(configured_host).resolve() if configured_host else (
            ROOT
            / "services/core-server/Host/bin/Release/net10.0/AssetLibrary.CoreServer.Host.dll"
        )
        if not host.is_file():
            cls.unavailable(f"CoreServer host build is missing: {host}")
        return [str(dotnet), str(host)]

    @staticmethod
    def http_payload(
        port: int,
        *,
        path: str = "/readyz",
        timeout_seconds: float = 1,
    ) -> tuple[int, dict[str, object]]:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        request = urllib.request.Request(
            f"http://127.0.0.1:{port}{path}",
            headers={"Connection": "close"},
        )
        try:
            with opener.open(request, timeout=timeout_seconds) as response:
                return response.status, json.loads(response.read())
        except urllib.error.HTTPError as error:
            return error.code, json.loads(error.read())

    @classmethod
    def wait_for_readiness(
        cls,
        process: subprocess.Popen[str],
        port: int,
        expected_status: int,
        expected_reason: str | None = None,
    ) -> dict[str, object]:
        last_error: Exception | None = None
        for _ in range(100):
            if process.poll() is not None:
                stdout, stderr = process.communicate(timeout=1)
                raise AssertionError(
                    f"CoreServer exited before readiness check: {stdout} {stderr}"
                )
            try:
                status, payload = cls.http_payload(port)
                if status == expected_status and (
                    expected_reason is None or payload.get("reason") == expected_reason
                ):
                    return payload
            except (OSError, ValueError, urllib.error.URLError) as error:
                last_error = error
            time.sleep(0.1)
        raise AssertionError(f"CoreServer readiness did not converge: {last_error}")

    @staticmethod
    def stop_host(process: subprocess.Popen[str]) -> tuple[str, str]:
        if process.poll() is None:
            process.terminate()
        try:
            return process.communicate(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            return process.communicate(timeout=5)

    @classmethod
    def _run(
        cls,
        command: list[str],
        *,
        input_text: str | None = None,
        timeout: int = 30,
        check: bool = True,
        capture_output: bool = True,
    ) -> subprocess.CompletedProcess[str]:
        output_options = (
            {"capture_output": True}
            if capture_output
            else {"stdout": subprocess.DEVNULL, "stderr": subprocess.DEVNULL}
        )
        result = subprocess.run(
            command,
            input=input_text,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=cls.environment,
            timeout=timeout,
            check=False,
            **output_options,
        )
        if check and result.returncode != 0:
            raise AssertionError(
                f"command failed ({result.returncode}): {result.stderr or result.stdout}"
            )
        return result

    @classmethod
    def psql_path(cls) -> str:
        suffix = ".exe" if os.name == "nt" else ""
        return str(cls.pg_bin / f"psql{suffix}")

    @classmethod
    def sql(
        cls,
        database: str,
        username: str,
        statement: str,
        *,
        check: bool = True,
    ) -> subprocess.CompletedProcess[str]:
        command = [
            cls.psql_path(),
            "--no-psqlrc",
            "--set", "ON_ERROR_STOP=1",
            "--quiet",
            "--no-align",
            "--tuples-only",
            "--host", cls.host,
            "--port", str(cls.port),
            "--username", username,
            "--dbname", database,
            "--no-password",
        ]
        return cls._run(command, input_text=statement, check=check)

    @classmethod
    def database_name(cls, label: str) -> str:
        return f"v01003_{label}_{uuid.uuid4().hex[:10]}"

    @classmethod
    def create_database(cls, database: str) -> None:
        cls.sql("postgres", cls.admin, f'CREATE DATABASE "{database}";')

    @classmethod
    def drop_database(cls, database: str) -> None:
        cls.sql(
            "postgres",
            cls.admin,
            f'DROP DATABASE IF EXISTS "{database}" WITH (FORCE);',
            check=False,
        )

    @classmethod
    def login_role_sql(cls) -> str:
        return f"""
DO $test_logins$
DECLARE role_name text;
BEGIN
  FOREACH role_name IN ARRAY ARRAY['{cls.RUNNER}', '{cls.RUNTIME}', '{cls.AUDITOR}']
  LOOP
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
      EXECUTE format(
        'CREATE ROLE %I LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS',
        role_name
      );
    END IF;
  END LOOP;
END
$test_logins$;
GRANT assetlibrary_migration_executor TO {cls.RUNNER}
  WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
GRANT assetlibrary_library_storage_runtime TO {cls.RUNTIME}
  WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
GRANT assetlibrary_database_auditor TO {cls.AUDITOR}
  WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
"""

    @classmethod
    def connection(cls, database: str, username: str) -> MIGRATIONS.Connection:
        return MIGRATIONS.Connection(
            host=cls.host,
            port=cls.port,
            username=username,
            database=database,
            confirm_database=database,
            postgres_bin=cls.pg_bin,
        )

    @classmethod
    def provision_database(cls, database: str) -> None:
        admin_tools = MIGRATIONS.PostgresTools(
            cls.manifest, cls.connection(database, cls.admin)
        )
        MIGRATIONS.provision_roles(admin_tools, cls.manifest, explicit=True)
        cls.grant_test_database_access(database)

    @classmethod
    def grant_test_database_access(cls, database: str) -> None:
        cls.sql(
            database,
            cls.admin,
            (
                f"GRANT CONNECT ON DATABASE \"{database}\" "
                f"TO {cls.RUNNER}, {cls.RUNTIME}, {cls.AUDITOR};"
            ),
        )

    def fresh_database(self, label: str) -> str:
        database = self.database_name(label)
        self.create_database(database)
        self.addCleanup(self.drop_database, database)
        self.provision_database(database)
        return database

    def runner_tools(
        self,
        database: str,
        manifest: MIGRATIONS.Manifest | None = None,
    ) -> MIGRATIONS.PostgresTools:
        selected = manifest or self.manifest
        return MIGRATIONS.PostgresTools(selected, self.connection(database, self.RUNNER))

    def backup_directory(self, label: str) -> Path:
        root = Path(
            os.environ.get(
                "ASSETLIBRARY_TEST_RUNTIME",
                ROOT / ".runtime/sandbox-storage/V01-006",
            )
        ).resolve()
        path = root / "integration-backups" / f"{label}-{uuid.uuid4().hex}"
        path.mkdir(parents=True, exist_ok=False)
        self.addCleanup(shutil.rmtree, path, True)
        return path

    def task_health_database(self, label: str) -> str:
        database = self.fresh_database(label)
        MIGRATIONS.apply_migrations(
            self.runner_tools(database),
            self.manifest,
            self.backup_directory(label),
        )
        self.sql(
            database,
            self.admin,
            f"GRANT assetlibrary_task_health_runtime TO {self.RUNTIME} "
            "WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;",
        )
        return database

    def task_health_sql(
        self,
        database: str,
        statement: str,
        *,
        check: bool = True,
    ) -> subprocess.CompletedProcess[str]:
        return self.sql(
            database,
            self.RUNTIME,
            "SET ROLE assetlibrary_task_health_runtime;\n" + statement,
            check=check,
        )

    def web_gateway_database(self, label: str) -> str:
        database = self.fresh_database(label)
        MIGRATIONS.apply_migrations(
            self.runner_tools(database),
            self.manifest,
            self.backup_directory(label),
        )
        self.sql(
            database,
            self.admin,
            f"GRANT assetlibrary_asset_identity_runtime, "
            f"assetlibrary_gateway_auth_runtime TO {self.RUNTIME} "
            "WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;",
        )
        return database

    def authentication_database(self, label: str) -> str:
        return self.web_gateway_database(label)

    def seed_local_account(
        self,
        database: str,
        *,
        account_name: str = "test-user",
        subject_key: str = "local:test-user",
        is_system_administrator: bool = False,
    ) -> dict[str, object]:
        principal_id = uuid.uuid4()
        salt = bytes(range(1, 17))
        test_secret = "V01-011 synthetic sign in secret"
        digest = hashlib.pbkdf2_hmac(
            "sha256",
            test_secret.encode("utf-8"),
            salt,
            600_000,
            dklen=32,
        )
        self.sql(
            database,
            self.admin,
            f"""
SET ROLE assetlibrary_gateway_auth_owner;
INSERT INTO gateway_auth.authenticated_principal (
  principal_id, subject_key, display_name, is_system_administrator, created_at
) VALUES (
  '{principal_id}', '{subject_key}', 'Synthetic test user',
  {str(is_system_administrator).lower()}, clock_timestamp()
);
INSERT INTO gateway_auth.local_account_credential (
  principal_id, account_name, secret_algorithm, secret_iterations,
  secret_salt, secret_digest, credential_version, created_at, changed_at
) VALUES (
  '{principal_id}', '{account_name}', 'pbkdf2-sha256', 600000,
  decode('{salt.hex()}', 'hex'), decode('{digest.hex()}', 'hex'), 7,
  clock_timestamp(), clock_timestamp()
);
""",
        )
        return {
            "principal_id": principal_id,
            "account_name": account_name,
            "subject_key": subject_key,
            "test_secret": test_secret,
            "salt": salt,
            "digest": digest,
        }

    def gateway_sql(
        self,
        database: str,
        statement: str,
        *,
        check: bool = True,
    ) -> subprocess.CompletedProcess[str]:
        return self.sql(
            database,
            self.RUNTIME,
            "SET ROLE assetlibrary_gateway_auth_runtime;\n" + statement,
            check=check,
        )

    @staticmethod
    def token_digest(value: int) -> str:
        return hashlib.sha256(bytes([value]) * 32).hexdigest()

    @staticmethod
    def enqueue_task_statement(
        task_id: uuid.UUID,
        idempotency_key: str,
        *,
        payload: str = '{"value": 1}',
        priority: int = 2,
        max_attempts: int = 3,
    ) -> str:
        return f"""
SELECT task_id, created
FROM task_health.enqueue_durable_task(
  '{task_id}', '{idempotency_key}', 'scan.initial', '{payload}'::jsonb,
  {priority}::smallint, {max_attempts}, clock_timestamp(), clock_timestamp()
);
"""

    @staticmethod
    def enqueue_outbox_statement(
        event_id: uuid.UUID,
        *,
        payload: str = '{"value": 1}',
        max_attempts: int = 3,
    ) -> str:
        return f"""
SELECT task_health.enqueue_outbox_event(
  '{event_id}', 'AssetIdentity', 'asset.indexed', NULL, 1, '{payload}'::jsonb,
  '2026-09-03T01:00:00Z', {max_attempts}, clock_timestamp(), clock_timestamp()
);
"""

    def staged_manifest(
        self,
        name: str,
        body: str,
        *,
        module: str = "LibraryStorage",
    ) -> MIGRATIONS.Manifest:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        destination = Path(temporary.name) / "production"
        shutil.copytree(TOOL_PATH.parent, destination)
        data_path = destination / "manifest.json"
        data = json.loads(data_path.read_text(encoding="utf-8"))
        version = len(data["migrations"]) + 1
        sql_path = destination / f"{version:04d}_{name}.sql"
        sql_path.write_text(body, encoding="utf-8", newline="\n")
        owner = next(
            item["owner_role"] for item in data["modules"] if item["module"] == module
        )
        data["migrations"].append(
            {
                "version": version,
                "name": name,
                "module": module,
                "owner_role": owner,
                "path": sql_path.name,
                "sha256": hashlib.sha256(sql_path.read_bytes()).hexdigest(),
            }
        )
        data_path.write_text(
            json.dumps(data, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        return MIGRATIONS.load_manifest(data_path)

    def test_apply_reapply_and_least_privilege(self) -> None:
        database = self.fresh_database("ownership")
        backups = self.backup_directory("ownership")
        tools = self.runner_tools(database)

        backup = MIGRATIONS.apply_migrations(tools, self.manifest, backups)
        self.assertIsNotNone(backup)
        _, rows = MIGRATIONS.ledger_rows(tools, self.manifest)
        self.assertEqual([row["version"] for row in rows], list(range(1, 12)))
        before = sorted(path.name for path in backups.iterdir())
        self.assertIsNone(MIGRATIONS.apply_migrations(tools, self.manifest, backups))
        self.assertEqual(sorted(path.name for path in backups.iterdir()), before)

        projection = self.sql(
            database,
            self.AUDITOR,
            "SET ROLE assetlibrary_database_auditor; "
            "SELECT count(*), count(*) FILTER (WHERE is_clean) "
            "FROM migration.module_privilege_projection;",
        ).stdout.strip()
        self.assertEqual(projection, "11|11")

        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_library_storage_owner; "
            "CREATE TABLE library_storage.runtime_probe(id integer PRIMARY KEY, value text NOT NULL); "
            "RESET ROLE; "
            "SET ROLE assetlibrary_asset_identity_owner; "
            "CREATE TABLE asset_identity.private_probe(id integer PRIMARY KEY);",
        )
        runtime = (
            "SET ROLE assetlibrary_library_storage_runtime; "
            "INSERT INTO library_storage.runtime_probe VALUES (1, 'ok'); "
            "UPDATE library_storage.runtime_probe SET value='updated' WHERE id=1; "
            "SELECT value FROM library_storage.runtime_probe WHERE id=1; "
            "DELETE FROM library_storage.runtime_probe WHERE id=1;"
        )
        self.assertIn("updated", self.sql(database, self.RUNTIME, runtime).stdout)
        for denied in (
            "SET ROLE assetlibrary_library_storage_runtime; CREATE TABLE library_storage.denied(id int);",
            "SET ROLE assetlibrary_library_storage_runtime; SELECT * FROM asset_identity.private_probe;",
            "SET ROLE assetlibrary_library_storage_runtime; SELECT * FROM migration.ledger;",
            "SET ROLE assetlibrary_library_storage_owner; SELECT 1;",
            "SET ROLE assetlibrary_library_storage_runtime; CREATE TABLE public.denied(id int);",
            "SET ROLE assetlibrary_library_storage_runtime; CREATE SCHEMA runtime_denied;",
        ):
            self.assertNotEqual(
                self.sql(database, self.RUNTIME, denied, check=False).returncode,
                0,
                denied,
            )

    def test_web_gateway_filters_permissions_pages_and_revocation_in_database(self) -> None:
        database = self.web_gateway_database("web_gateway")
        source_id = uuid.uuid4()
        alpha_library = uuid.uuid4()
        beta_library = uuid.uuid4()
        hidden_library = uuid.uuid4()
        normal_principal = uuid.uuid4()
        admin_principal = uuid.uuid4()
        disabled_principal = uuid.uuid4()
        created_at = "2026-09-04T00:00:00Z"

        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_library_storage_runtime;
INSERT INTO library_storage.storage_source (
  storage_source_id, display_name, availability, root_case_sensitive, availability_observed_at
) VALUES ('{source_id}', 'sandbox', 'online', false, '{created_at}');
SELECT library_storage.register_library_root(
  '{alpha_library}', '{source_id}', 'Alpha', 'C:/sandbox/alpha', '{created_at}'
);
SELECT library_storage.register_library_root(
  '{beta_library}', '{source_id}', 'Beta', 'C:/sandbox/beta', '{created_at}'
);
SELECT library_storage.register_library_root(
  '{hidden_library}', '{source_id}', 'Hidden', 'C:/sandbox/hidden', '{created_at}'
);
""",
        )
        self.sql(
            database,
            self.admin,
            f"""
SET ROLE assetlibrary_gateway_auth_owner;
INSERT INTO gateway_auth.authenticated_principal (
  principal_id, subject_key, display_name, is_system_administrator, created_at, disabled_at
) VALUES
  ('{normal_principal}', 'oidc:user-1', 'Normal user', false, '{created_at}', NULL),
  ('{admin_principal}', 'oidc:admin-1', 'System administrator', true, '{created_at}', NULL),
  ('{disabled_principal}', 'oidc:disabled-1', 'Disabled user', false, '{created_at}', '{created_at}');
RESET ROLE;
SET ROLE assetlibrary_library_storage_owner;
INSERT INTO library_storage.library_permission (
  library_id, principal_id, access_level, granted_at, updated_at
) VALUES
  ('{alpha_library}', '{normal_principal}', 'read_only', '{created_at}', '{created_at}'),
  ('{beta_library}', '{normal_principal}', 'organize', '{created_at}', '{created_at}'),
  ('{alpha_library}', '{disabled_principal}', 'read_only', '{created_at}', '{created_at}');
""",
        )

        alpha_scan = uuid.uuid4()
        beta_scan = uuid.uuid4()
        hidden_scan = uuid.uuid4()
        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_asset_identity_runtime;
INSERT INTO asset_identity.scan_observation_stage (
  scan_id, entry_id, library_id, normalized_relative_path, kind,
  content_length, last_write_time_utc, staged_at
) VALUES
  ('{alpha_scan}', '{uuid.uuid4()}', '{alpha_library}', 'Alpha.jpg', 'file', 10, '{created_at}', '{created_at}'),
  ('{alpha_scan}', '{uuid.uuid4()}', '{alpha_library}', 'beta.txt', 'file', 20, '{created_at}', '{created_at}'),
  ('{alpha_scan}', '{uuid.uuid4()}', '{alpha_library}', 'folder', 'directory', NULL, '{created_at}', '{created_at}'),
  ('{alpha_scan}', '{uuid.uuid4()}', '{alpha_library}', 'folder/secret-blue.png', 'file', 30, '{created_at}', '{created_at}'),
  ('{beta_scan}', '{uuid.uuid4()}', '{beta_library}', 'other.txt', 'file', 40, '{created_at}', '{created_at}'),
  ('{hidden_scan}', '{uuid.uuid4()}', '{hidden_library}', 'hidden-secret.txt', 'file', 50, '{created_at}', '{created_at}');
SELECT asset_identity.commit_initial_scan('{alpha_scan}', '{alpha_library}', '{created_at}');
SELECT asset_identity.commit_initial_scan('{beta_scan}', '{beta_library}', '{created_at}');
SELECT asset_identity.commit_initial_scan('{hidden_scan}', '{hidden_library}', '{created_at}');
""",
        )

        normal_libraries = self.gateway_sql(
            database,
            "SELECT display_name || ':' || access_level "
            "FROM gateway_auth.list_authorized_libraries('oidc:user-1', NULL, NULL, 101);",
        ).stdout.strip().splitlines()
        self.assertEqual(normal_libraries, ["Alpha:read_only", "Beta:organize"])
        admin_libraries = self.gateway_sql(
            database,
            "SELECT display_name || ':' || access_level "
            "FROM gateway_auth.list_authorized_libraries('oidc:admin-1', NULL, NULL, 101);",
        ).stdout.strip().splitlines()
        self.assertEqual(
            admin_libraries,
            [
                "Alpha:library_administrator",
                "Beta:library_administrator",
                "Hidden:library_administrator",
            ],
        )
        for subject in ("oidc:disabled-1", "oidc:unknown"):
            result = self.gateway_sql(
                database,
                "SELECT library_id FROM gateway_auth.list_authorized_libraries("
                f"'{subject}', NULL, NULL, 101);",
            ).stdout.strip()
            self.assertEqual(result, "")

        first_page = self.gateway_sql(
            database,
            "SELECT relative_path, sort_name, entry_id "
            "FROM gateway_auth.browse_authorized_entries("
            f"'oidc:user-1', '{alpha_library}', '', NULL, NULL, 2);",
        ).stdout.strip().splitlines()
        self.assertEqual([row.split("|")[0] for row in first_page], ["Alpha.jpg", "beta.txt"])
        _, last_sort, last_entry = first_page[-1].split("|")
        second_page = self.gateway_sql(
            database,
            "SELECT relative_path FROM gateway_auth.browse_authorized_entries("
            f"'oidc:user-1', '{alpha_library}', '', '{last_sort}', '{last_entry}', 2);",
        ).stdout.strip().splitlines()
        self.assertEqual(second_page, ["folder"])

        hidden_browse = self.gateway_sql(
            database,
            "SELECT relative_path FROM gateway_auth.browse_authorized_entries("
            f"'oidc:user-1', '{hidden_library}', '', NULL, NULL, 101);",
        ).stdout.strip()
        nonexistent_browse = self.gateway_sql(
            database,
            "SELECT relative_path FROM gateway_auth.browse_authorized_entries("
            f"'oidc:user-1', '{uuid.uuid4()}', '', NULL, NULL, 101);",
        ).stdout.strip()
        self.assertEqual(hidden_browse, nonexistent_browse)
        self.assertEqual(hidden_browse, "")

        search = self.gateway_sql(
            database,
            "SELECT library_display_name, relative_path, hit_reason "
            "FROM gateway_auth.search_authorized_entries("
            "'oidc:user-1', 'secret', NULL, NULL, NULL, 101);",
        ).stdout.strip().splitlines()
        self.assertEqual(search, ["Alpha|folder/secret-blue.png|name"])
        self.assertNotIn("hidden-secret.txt", "\n".join(search))

        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_library_storage_owner; "
            "DELETE FROM library_storage.library_permission "
            f"WHERE library_id = '{alpha_library}' AND principal_id = '{normal_principal}';",
        )
        after_revoke = self.gateway_sql(
            database,
            "SELECT display_name FROM gateway_auth.list_authorized_libraries("
            "'oidc:user-1', NULL, NULL, 101);",
        ).stdout.strip().splitlines()
        self.assertEqual(after_revoke, ["Beta"])
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT relative_path FROM gateway_auth.search_authorized_entries("
                "'oidc:user-1', 'secret', NULL, NULL, NULL, 101);",
            ).stdout.strip(),
            "",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT relative_path FROM gateway_auth.browse_authorized_entries("
                f"'oidc:user-1', '{alpha_library}', '', NULL, NULL, 101);",
            ).stdout.strip(),
            "",
        )

        for invalid in (
            "SELECT * FROM gateway_auth.search_authorized_entries("
            "'oidc:user-1', NULL, NULL, NULL, NULL, 10);",
            "SELECT * FROM gateway_auth.list_authorized_libraries("
            "'oidc:user-1', NULL, NULL, 102);",
            "SELECT * FROM gateway_auth.browse_authorized_entries("
            f"'oidc:user-1', '{beta_library}', '', 'orphan', NULL, 10);",
        ):
            self.assertNotEqual(
                self.gateway_sql(database, invalid, check=False).returncode,
                0,
                invalid,
            )

        for denied in (
            "SELECT * FROM gateway_auth.authenticated_principal;",
            "SELECT * FROM library_storage.library_permission_read_projection;",
            "SELECT * FROM asset_identity.entry_read_projection;",
            "INSERT INTO gateway_auth.authenticated_principal ("
            "principal_id, subject_key, display_name, created_at) VALUES ("
            f"'{uuid.uuid4()}', 'oidc:injected', 'Injected', clock_timestamp());",
        ):
            self.assertNotEqual(
                self.gateway_sql(database, denied, check=False).returncode,
                0,
                denied,
            )

    def test_web_gateway_100k_directory_uses_browse_and_search_indexes(self) -> None:
        database = self.web_gateway_database("web_gateway_scale")
        source_id = uuid.uuid4()
        library_id = uuid.uuid4()
        principal_id = uuid.uuid4()
        scan_id = uuid.uuid4()
        created_at = "2026-09-04T00:00:00Z"

        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_library_storage_runtime;
INSERT INTO library_storage.storage_source (
  storage_source_id, display_name, availability, root_case_sensitive, availability_observed_at
) VALUES ('{source_id}', 'scale sandbox', 'online', false, '{created_at}');
SELECT library_storage.register_library_root(
  '{library_id}', '{source_id}', 'Scale', 'C:/sandbox/scale', '{created_at}'
);
""",
        )
        self.sql(
            database,
            self.admin,
            f"""
SET ROLE assetlibrary_gateway_auth_owner;
INSERT INTO gateway_auth.authenticated_principal (
  principal_id, subject_key, display_name, created_at
) VALUES ('{principal_id}', 'oidc:scale-user', 'Scale user', '{created_at}');
RESET ROLE;
SET ROLE assetlibrary_library_storage_owner;
INSERT INTO library_storage.library_permission (
  library_id, principal_id, access_level, granted_at, updated_at
) VALUES ('{library_id}', '{principal_id}', 'read_only', '{created_at}', '{created_at}');
RESET ROLE;
SET ROLE assetlibrary_asset_identity_owner;
INSERT INTO asset_identity.filesystem_entry (
  entry_id, library_id, normalized_relative_path, kind, content_length,
  last_write_time_utc, first_seen_scan_id, last_seen_scan_id, first_seen_at, last_seen_at
)
SELECT
  md5(item::text || '-v01-006')::uuid,
  '{library_id}',
  format('huge/photo_%s.jpg', lpad(item::text, 6, '0')),
  'file',
  item,
  '{created_at}',
  '{scan_id}',
  '{scan_id}',
  '{created_at}',
  '{created_at}'
FROM generate_series(1, 100000) AS item;
ANALYZE asset_identity.filesystem_entry;
""",
        )

        browse_plan = self.sql(
            database,
            self.admin,
            f"""
SET ROLE assetlibrary_asset_identity_owner;
EXPLAIN (ANALYZE, TIMING OFF)
SELECT entry_id
FROM asset_identity.filesystem_entry
WHERE library_id = '{library_id}'
  AND state = 'present'
  AND parent_relative_path = 'huge'
ORDER BY lower(entry_name), entry_id
LIMIT 101;
""",
        ).stdout
        self.assertIn("filesystem_entry_browse_index", browse_plan)

        search_plan = self.sql(
            database,
            self.admin,
            f"""
SET ROLE assetlibrary_asset_identity_owner;
EXPLAIN (ANALYZE, TIMING OFF)
SELECT entry_id
FROM asset_identity.filesystem_entry
WHERE library_id = '{library_id}'
  AND state = 'present'
  AND path_search_document @@ plainto_tsquery('simple'::regconfig, '099999')
ORDER BY lower(entry_name), library_id, entry_id
LIMIT 101;
""",
        ).stdout
        self.assertIn("filesystem_entry_path_search_index", search_plan)

        bounded_browse = self.gateway_sql(
            database,
            "SELECT count(*) FROM gateway_auth.browse_authorized_entries("
            f"'oidc:scale-user', '{library_id}', 'huge', NULL, NULL, 101);",
        ).stdout.strip()
        self.assertEqual(bounded_browse, "101")
        bounded_search = self.gateway_sql(
            database,
            "SELECT relative_path FROM gateway_auth.search_authorized_entries("
            "'oidc:scale-user', '099999', NULL, NULL, NULL, 101);",
        ).stdout.strip()
        self.assertEqual(bounded_search, "huge/photo_099999.jpg")

    def test_web_gateway_permissions_and_projection_survive_database_restart(self) -> None:
        if self.external:
            self.skipTest("restart persistence requires the disposable self-hosted cluster")
        database = self.web_gateway_database("web_gateway_restart")
        source_id = uuid.uuid4()
        library_id = uuid.uuid4()
        principal_id = uuid.uuid4()
        scan_id = uuid.uuid4()
        entry_id = uuid.uuid4()
        created_at = "2026-09-04T00:00:00Z"

        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_library_storage_runtime;
INSERT INTO library_storage.storage_source (
  storage_source_id, display_name, availability, root_case_sensitive, availability_observed_at
) VALUES ('{source_id}', 'restart sandbox', 'online', false, '{created_at}');
SELECT library_storage.register_library_root(
  '{library_id}', '{source_id}', 'Restart', 'C:/sandbox/restart', '{created_at}'
);
RESET ROLE;
SET ROLE assetlibrary_asset_identity_runtime;
INSERT INTO asset_identity.scan_observation_stage (
  scan_id, entry_id, library_id, normalized_relative_path, kind,
  content_length, last_write_time_utc, staged_at
) VALUES (
  '{scan_id}', '{entry_id}', '{library_id}', 'persisted.txt', 'file', 7,
  '{created_at}', '{created_at}'
);
SELECT asset_identity.commit_initial_scan('{scan_id}', '{library_id}', '{created_at}');
""",
        )
        self.sql(
            database,
            self.admin,
            f"""
SET ROLE assetlibrary_gateway_auth_owner;
INSERT INTO gateway_auth.authenticated_principal (
  principal_id, subject_key, display_name, created_at
) VALUES ('{principal_id}', 'oidc:restart-user', 'Restart user', '{created_at}');
RESET ROLE;
SET ROLE assetlibrary_library_storage_owner;
INSERT INTO library_storage.library_permission (
  library_id, principal_id, access_level, granted_at, updated_at
) VALUES ('{library_id}', '{principal_id}', 'read_only', '{created_at}', '{created_at}');
""",
        )
        assert self.pg_ctl is not None and self.cluster_data is not None
        self._run(
            [
                str(self.pg_ctl),
                "--pgdata",
                str(self.cluster_data),
                "--wait",
                "--timeout",
                "30",
                "--mode",
                "fast",
                "restart",
            ],
            timeout=45,
            capture_output=False,
        )

        persisted = self.gateway_sql(
            database,
            "SELECT library_display_name, relative_path FROM "
            "gateway_auth.search_authorized_entries("
            "'oidc:restart-user', 'persisted', NULL, NULL, NULL, 101);",
        ).stdout.strip()
        self.assertEqual(persisted, "Restart|persisted.txt")

    def test_local_authentication_session_lifecycle_and_least_privilege(self) -> None:
        database = self.authentication_database("local_auth")
        account = self.seed_local_account(database)
        principal_id = account["principal_id"]
        session_digest = self.token_digest(0x31)
        csrf_digest = self.token_digest(0x42)
        wrong_csrf_digest = self.token_digest(0x43)

        material = self.gateway_sql(
            database,
            "SELECT subject_key, secret_algorithm, secret_iterations, "
            "octet_length(secret_salt), octet_length(secret_digest), "
            "credential_version, can_attempt "
            "FROM gateway_auth.read_local_sign_in_material('test-user');",
        ).stdout.strip()
        self.assertEqual(
            material,
            "local:test-user|pbkdf2-sha256|600000|16|32|7|t",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.read_local_sign_in_material('unknown-user');",
            ).stdout.strip(),
            "0",
        )

        for denied in (
            "SELECT * FROM gateway_auth.local_account_credential;",
            "SELECT * FROM gateway_auth.browser_session;",
            "UPDATE gateway_auth.authenticated_principal SET session_version = 2;",
            "INSERT INTO gateway_auth.browser_session (session_digest) VALUES (decode('00', 'hex'));",
        ):
            self.assertNotEqual(
                self.gateway_sql(database, denied, check=False).returncode,
                0,
                denied,
            )

        first_failure = self.gateway_sql(
            database,
            "SELECT gateway_auth.record_local_sign_in_failure('test-user', 7);",
        ).stdout.strip()
        immediate_failure = self.gateway_sql(
            database,
            "SELECT gateway_auth.record_local_sign_in_failure('test-user', 7);",
        ).stdout.strip()
        self.assertEqual(first_failure, "t")
        self.assertEqual(immediate_failure, "f")
        throttled = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT failed_attempt_count, retry_not_before > clock_timestamp() "
            "FROM gateway_auth.local_account_credential WHERE account_name = 'test-user';",
        ).stdout.strip()
        self.assertEqual(throttled, "1|t")
        self.assertTrue(
            self.gateway_sql(
                database,
                "SELECT can_attempt FROM gateway_auth.read_local_sign_in_material('test-user');",
            ).stdout.strip()
            == "f"
        )

        for _ in range(10):
            self.sql(
                database,
                self.admin,
                "SET ROLE assetlibrary_gateway_auth_owner; "
                "UPDATE gateway_auth.local_account_credential "
                "SET retry_not_before = clock_timestamp() - interval '1 second' "
                "WHERE account_name = 'test-user';",
            )
            self.assertEqual(
                self.gateway_sql(
                    database,
                    "SELECT gateway_auth.record_local_sign_in_failure('test-user', 7);",
                ).stdout.strip(),
                "t",
            )
        bounded_backoff = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT failed_attempt_count, "
            "extract(epoch FROM (retry_not_before - clock_timestamp())) BETWEEN 298 AND 301 "
            "FROM gateway_auth.local_account_credential WHERE account_name = 'test-user';",
        ).stdout.strip()
        self.assertEqual(bounded_backoff, "11|t")

        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "UPDATE gateway_auth.local_account_credential "
            "SET retry_not_before = clock_timestamp() - interval '1 second' "
            "WHERE account_name = 'test-user';",
        )
        created = self.gateway_sql(
            database,
            "SELECT outcome, "
            "round(extract(epoch FROM (idle_expires_at - issued_at))), "
            "round(extract(epoch FROM (absolute_expires_at - issued_at))) "
            "FROM gateway_auth.create_browser_session("
            f"'{principal_id}', 1, 'local', 7, "
            f"decode('{session_digest}', 'hex'), decode('{csrf_digest}', 'hex'));",
        ).stdout.strip()
        self.assertEqual(created, "created|1800|43200")

        stored = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT encode(session_digest, 'hex'), encode(csrf_digest, 'hex'), "
            "authentication_method, principal_session_version, local_credential_version "
            "FROM gateway_auth.browser_session;",
        ).stdout.strip()
        self.assertEqual(stored, f"{session_digest}|{csrf_digest}|local|1|7")
        self.assertNotIn((bytes([0x31]) * 32).hex(), stored)
        self.assertNotIn((bytes([0x42]) * 32).hex(), stored)
        reset = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT failed_attempt_count, retry_not_before IS NULL "
            "FROM gateway_auth.local_account_credential WHERE account_name = 'test-user';",
        ).stdout.strip()
        self.assertEqual(reset, "0|t")

        read_identity = self.gateway_sql(
            database,
            "SELECT subject_key, authentication_method "
            "FROM gateway_auth.authenticate_browser_session("
            f"decode('{session_digest}', 'hex'), NULL, false);",
        ).stdout.strip()
        self.assertEqual(read_identity, "local:test-user|local")
        wrong_csrf = self.gateway_sql(
            database,
            "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
            f"decode('{session_digest}', 'hex'), decode('{wrong_csrf_digest}', 'hex'), true);",
        ).stdout.strip()
        correct_csrf = self.gateway_sql(
            database,
            "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
            f"decode('{session_digest}', 'hex'), decode('{csrf_digest}', 'hex'), true);",
        ).stdout.strip()
        self.assertEqual(wrong_csrf, "0")
        self.assertEqual(correct_csrf, "1")
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT gateway_auth.revoke_browser_session("
                f"decode('{session_digest}', 'hex'), decode('{wrong_csrf_digest}', 'hex'));",
            ).stdout.strip(),
            "f",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT gateway_auth.revoke_browser_session("
                f"decode('{session_digest}', 'hex'), decode('{csrf_digest}', 'hex'));",
            ).stdout.strip(),
            "t",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
                f"decode('{session_digest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "0",
        )

        for invalid in (
            "SELECT * FROM gateway_auth.read_local_sign_in_material('../invalid');",
            "SELECT gateway_auth.record_local_sign_in_failure('test-user', 0);",
            "SELECT * FROM gateway_auth.create_browser_session("
            f"'{principal_id}', 1, 'local', NULL, decode('{session_digest}', 'hex'), "
            f"decode('{csrf_digest}', 'hex'));",
            "SELECT * FROM gateway_auth.authenticate_browser_session(decode('00', 'hex'), NULL, false);",
        ):
            self.assertNotEqual(
                self.gateway_sql(database, invalid, check=False).returncode,
                0,
                invalid,
            )

    def test_local_account_lifecycle_requires_a_database_verified_administrator(self) -> None:
        database = self.authentication_database("account_lifecycle_authz")
        regular = self.seed_local_account(database)
        administrator = self.seed_local_account(
            database,
            account_name="admin-one",
            subject_key="local:admin-one",
            is_system_administrator=True,
        )

        unauthorized = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed FROM "
            "gateway_auth.read_local_account_for_administrator("
            f"'{regular['principal_id']}', 1, 'test-user');",
        ).stdout.strip()
        stale = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed FROM "
            "gateway_auth.read_local_account_for_administrator("
            f"'{administrator['principal_id']}', 2, 'test-user');",
        ).stdout.strip()
        forged = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed FROM "
            "gateway_auth.read_local_account_for_administrator("
            f"'{uuid.uuid4()}', 1, 'test-user');",
        ).stdout.strip()
        unauthorized_provision = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed FROM gateway_auth.provision_local_account("
            f"'{regular['principal_id']}', 1, '{uuid.uuid4()}', '{uuid.uuid4()}', "
            "'blocked-user', 'Blocked user', false, 'pbkdf2-sha256', 600000, "
            f"decode('{'ab' * 16}', 'hex'), decode('{'cd' * 32}', 'hex'));",
        ).stdout.strip()
        authorized = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed, account_name, is_enabled, "
            "credential_version, principal_session_version FROM "
            "gateway_auth.read_local_account_for_administrator("
            f"'{administrator['principal_id']}', 1, 'test-user');",
        ).stdout.strip()

        self.assertEqual(unauthorized, "unauthorized|f")
        self.assertEqual(stale, "unauthorized|f")
        self.assertEqual(forged, "unauthorized|f")
        self.assertEqual(unauthorized_provision, "unauthorized|f")
        self.assertEqual(authorized, "applied|f|test-user|t|7|1")
        self.assertEqual(
            self.sql(
                database,
                self.admin,
                "SET ROLE assetlibrary_gateway_auth_owner; "
                "SELECT count(*) FROM gateway_auth.local_account_lifecycle_operation;",
            ).stdout.strip(),
            "0",
        )

        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "UPDATE gateway_auth.local_account_credential "
            "SET disabled_at = clock_timestamp() "
            f"WHERE principal_id = '{administrator['principal_id']}'; "
            "UPDATE gateway_auth.authenticated_principal "
            "SET disabled_at = clock_timestamp() "
            f"WHERE principal_id = '{administrator['principal_id']}';",
        )
        disabled = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed FROM "
            "gateway_auth.read_local_account_for_administrator("
            f"'{administrator['principal_id']}', 1, 'test-user');",
        ).stdout.strip()
        self.assertEqual(disabled, "unauthorized|f")

        for denied in (
            "SELECT * FROM gateway_auth.local_account_lifecycle_operation;",
            "SELECT gateway_auth.local_account_actor_is_authorized("
            f"'{administrator['principal_id']}', 1);",
        ):
            self.assertNotEqual(
                self.gateway_sql(database, denied, check=False).returncode,
                0,
                denied,
            )

    def test_local_account_lifecycle_is_idempotent_and_invalidates_sessions(self) -> None:
        database = self.authentication_database("account_lifecycle")
        administrator = self.seed_local_account(
            database,
            account_name="admin-one",
            subject_key="local:admin-one",
            is_system_administrator=True,
        )
        actor_id = administrator["principal_id"]
        target_id = uuid.uuid4()
        provision_id = uuid.uuid4()
        initial_salt = "11" * 16
        initial_digest = "22" * 32

        provision = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed, account_name, is_enabled, "
            "credential_version, principal_session_version FROM "
            "gateway_auth.provision_local_account("
            f"'{actor_id}', 1, '{provision_id}', '{target_id}', "
            "'managed-user', 'Managed user', false, 'pbkdf2-sha256', 600000, "
            f"decode('{initial_salt}', 'hex'), decode('{initial_digest}', 'hex'));",
        ).stdout.strip()
        replay = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed, principal_id, credential_version, "
            "principal_session_version FROM gateway_auth.provision_local_account("
            f"'{actor_id}', 1, '{provision_id}', '{uuid.uuid4()}', "
            "'managed-user', 'Managed user', false, 'pbkdf2-sha256', 600000, "
            f"decode('{'33' * 16}', 'hex'), decode('{'44' * 32}', 'hex'));",
        ).stdout.strip()
        conflicting_reuse = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed FROM gateway_auth.provision_local_account("
            f"'{actor_id}', 1, '{provision_id}', '{uuid.uuid4()}', "
            "'different-user', 'Managed user', false, 'pbkdf2-sha256', 600000, "
            f"decode('{'33' * 16}', 'hex'), decode('{'44' * 32}', 'hex'));",
        ).stdout.strip()

        self.assertEqual(provision, "applied|f|managed-user|t|1|1")
        self.assertEqual(replay, f"applied|t|{target_id}|1|1")
        self.assertEqual(conflicting_reuse, "operation_conflict|f")
        self.assertEqual(
            self.sql(
                database,
                self.admin,
                "SET ROLE assetlibrary_gateway_auth_owner; "
                "SELECT encode(secret_digest, 'hex') FROM "
                "gateway_auth.local_account_credential "
                "WHERE account_name = 'managed-user';",
            ).stdout.strip(),
            initial_digest,
        )

        duplicate_id = uuid.uuid4()
        duplicate = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed, principal_id FROM "
            "gateway_auth.provision_local_account("
            f"'{actor_id}', 1, '{duplicate_id}', '{uuid.uuid4()}', "
            "'managed-user', 'Replacement display', true, 'pbkdf2-sha256', 600000, "
            f"decode('{'55' * 16}', 'hex'), decode('{'66' * 32}', 'hex'));",
        ).stdout.strip()
        self.assertEqual(duplicate, f"account_conflict|f|{target_id}")
        self.assertEqual(
            self.sql(
                database,
                self.admin,
                "SET ROLE assetlibrary_gateway_auth_owner; "
                "SELECT display_name, is_system_administrator FROM "
                "gateway_auth.authenticated_principal "
                f"WHERE principal_id = '{target_id}';",
            ).stdout.strip(),
            "Managed user|f",
        )

        session_digest = self.token_digest(0x71)
        csrf_digest = self.token_digest(0x72)
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT outcome FROM gateway_auth.create_browser_session("
                f"'{target_id}', 1, 'local', 1, "
                f"decode('{session_digest}', 'hex'), decode('{csrf_digest}', 'hex'));",
            ).stdout.strip(),
            "created",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT gateway_auth.record_local_sign_in_failure('managed-user', 1);",
            ).stdout.strip(),
            "t",
        )

        replacement_id = uuid.uuid4()
        replacement_digest = "88" * 32
        replacement = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed, is_enabled, credential_version, "
            "principal_session_version FROM gateway_auth.replace_local_account_credential("
            f"'{actor_id}', 1, '{replacement_id}', 'managed-user', 1, true, "
            "'pbkdf2-sha256', 600000, "
            f"decode('{'77' * 16}', 'hex'), decode('{replacement_digest}', 'hex'));",
        ).stdout.strip()
        replacement_replay = self.gateway_sql(
            database,
            "SELECT outcome, was_replayed, credential_version, "
            "principal_session_version FROM gateway_auth.replace_local_account_credential("
            f"'{actor_id}', 1, '{replacement_id}', 'managed-user', 1, true, "
            "'pbkdf2-sha256', 600000, "
            f"decode('{'99' * 16}', 'hex'), decode('{'aa' * 32}', 'hex'));",
        ).stdout.strip()
        self.assertEqual(replacement, "applied|f|t|2|2")
        self.assertEqual(replacement_replay, "applied|t|2|2")
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
                f"decode('{session_digest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "0",
        )
        credential_state = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT encode(secret_digest, 'hex'), failed_attempt_count, "
            "retry_not_before IS NULL FROM gateway_auth.local_account_credential "
            "WHERE account_name = 'managed-user';",
        ).stdout.strip()
        self.assertEqual(credential_state, f"{replacement_digest}|0|t")

        disable_id = uuid.uuid4()
        disabled = self.gateway_sql(
            database,
            "SELECT outcome, is_enabled, principal_session_version FROM "
            "gateway_auth.set_local_account_enabled("
            f"'{actor_id}', 1, '{disable_id}', 'managed-user', 2, false);",
        ).stdout.strip()
        enable_id = uuid.uuid4()
        enabled = self.gateway_sql(
            database,
            "SELECT outcome, is_enabled, principal_session_version FROM "
            "gateway_auth.set_local_account_enabled("
            f"'{actor_id}', 1, '{enable_id}', 'managed-user', 3, true);",
        ).stdout.strip()
        self.assertEqual(disabled, "applied|f|3")
        self.assertEqual(enabled, "applied|t|4")

        audit_shape = self.sql(
            database,
            self.admin,
            "SELECT count(*) FROM information_schema.columns "
            "WHERE table_schema = 'gateway_auth' "
            "AND table_name = 'local_account_lifecycle_operation' "
            "AND column_name ~ '(secret|salt|digest|token|csrf)';",
        ).stdout.strip()
        audit_count = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT count(*) FROM gateway_auth.local_account_lifecycle_operation;",
        ).stdout.strip()
        self.assertEqual(audit_shape, "0")
        self.assertEqual(audit_count, "5")

    def test_last_enabled_administrator_is_preserved_under_concurrency(self) -> None:
        database = self.authentication_database("last_admin")
        first = self.seed_local_account(
            database,
            account_name="admin-one",
            subject_key="local:admin-one",
            is_system_administrator=True,
        )
        second = self.seed_local_account(
            database,
            account_name="admin-two",
            subject_key="local:admin-two",
            is_system_administrator=True,
        )

        def disable(account: dict[str, object]) -> str:
            return self.gateway_sql(
                database,
                "SELECT outcome FROM gateway_auth.set_local_account_enabled("
                f"'{account['principal_id']}', 1, '{uuid.uuid4()}', "
                f"'{account['account_name']}', 1, false);",
            ).stdout.strip()

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            results = list(executor.map(disable, (first, second)))

        self.assertEqual(results.count("applied"), 1)
        self.assertEqual(results.count("last_administrator"), 1)
        active_administrators = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT count(*) FROM gateway_auth.authenticated_principal "
            "WHERE is_system_administrator AND disabled_at IS NULL;",
        ).stdout.strip()
        self.assertEqual(active_administrators, "1")

    def test_local_authentication_failure_is_atomic_under_concurrency(self) -> None:
        database = self.authentication_database("local_auth_concurrency")
        self.seed_local_account(database)

        def record_failure() -> str:
            return self.gateway_sql(
                database,
                "SELECT gateway_auth.record_local_sign_in_failure('test-user', 7);",
            ).stdout.strip()

        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            results = list(executor.map(lambda _: record_failure(), range(8)))

        self.assertEqual(results.count("t"), 1)
        self.assertEqual(results.count("f"), 7)
        state = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT failed_attempt_count, retry_not_before > clock_timestamp() "
            "FROM gateway_auth.local_account_credential WHERE account_name = 'test-user';",
        ).stdout.strip()
        self.assertEqual(state, "1|t")

    def test_browser_sessions_are_bounded_and_invalidated_by_owner_state(self) -> None:
        database = self.authentication_database("session_bound")
        account = self.seed_local_account(database)
        principal_id = account["principal_id"]

        for value in range(1, 22):
            session_digest = self.token_digest(value)
            csrf_digest = self.token_digest(value + 40)
            outcome = self.gateway_sql(
                database,
                "SELECT outcome FROM gateway_auth.create_browser_session("
                f"'{principal_id}', 1, 'local', 7, "
                f"decode('{session_digest}', 'hex'), decode('{csrf_digest}', 'hex'));",
            ).stdout.strip()
            self.assertEqual(outcome, "created")

        count = self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "SELECT count(*) FROM gateway_auth.browser_session "
            f"WHERE principal_id = '{principal_id}';",
        ).stdout.strip()
        self.assertEqual(count, "20")
        oldest = self.token_digest(1)
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
                f"decode('{oldest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "0",
        )

        newest = self.token_digest(21)
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
                f"decode('{newest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "1",
        )
        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "UPDATE gateway_auth.local_account_credential SET credential_version = 8 "
            f"WHERE principal_id = '{principal_id}';",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
                f"decode('{newest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "0",
        )
        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "UPDATE gateway_auth.authenticated_principal SET session_version = 2 "
            f"WHERE principal_id = '{principal_id}';",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
                f"decode('{newest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "0",
        )

        oidc_digest = self.token_digest(90)
        oidc_csrf = self.token_digest(91)
        oidc_created = self.gateway_sql(
            database,
            "SELECT outcome FROM gateway_auth.create_browser_session("
            f"'{principal_id}', 2, 'oidc', NULL, "
            f"decode('{oidc_digest}', 'hex'), decode('{oidc_csrf}', 'hex'));",
        ).stdout.strip()
        self.assertEqual(oidc_created, "created")
        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "UPDATE gateway_auth.authenticated_principal SET disabled_at = clock_timestamp() "
            f"WHERE principal_id = '{principal_id}';",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
                f"decode('{oidc_digest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "0",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.read_local_sign_in_material('test-user');",
            ).stdout.strip(),
            "0",
        )

    def test_browser_session_persists_restart_and_expires_fail_closed(self) -> None:
        if self.external:
            self.skipTest("restart persistence requires the disposable self-hosted cluster")
        database = self.authentication_database("session_restart")
        account = self.seed_local_account(database)
        principal_id = account["principal_id"]
        session_digest = self.token_digest(101)
        csrf_digest = self.token_digest(102)
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT outcome FROM gateway_auth.create_browser_session("
                f"'{principal_id}', 1, 'local', 7, "
                f"decode('{session_digest}', 'hex'), decode('{csrf_digest}', 'hex'));",
            ).stdout.strip(),
            "created",
        )

        assert self.pg_ctl is not None and self.cluster_data is not None
        self._run(
            [
                str(self.pg_ctl),
                "--pgdata",
                str(self.cluster_data),
                "--wait",
                "--timeout",
                "30",
                "--mode",
                "fast",
                "restart",
            ],
            timeout=45,
            capture_output=False,
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT subject_key FROM gateway_auth.authenticate_browser_session("
                f"decode('{session_digest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "local:test-user",
        )

        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_gateway_auth_owner; "
            "UPDATE gateway_auth.browser_session SET "
            "issued_at = clock_timestamp() - interval '1 hour', "
            "last_seen_at = clock_timestamp() - interval '31 minutes', "
            "idle_expires_at = clock_timestamp() - interval '1 minute', "
            "absolute_expires_at = clock_timestamp() + interval '1 hour' "
            f"WHERE session_digest = decode('{session_digest}', 'hex');",
        )
        self.assertEqual(
            self.gateway_sql(
                database,
                "SELECT count(*) FROM gateway_auth.authenticate_browser_session("
                f"decode('{session_digest}', 'hex'), NULL, false);",
            ).stdout.strip(),
            "0",
        )

    def test_dotnet_postgres_authentication_store_round_trip(self) -> None:
        configured_dotnet = os.environ.get("ASSETLIBRARY_TEST_DOTNET")
        dotnet = configured_dotnet or shutil.which("dotnet")
        if not dotnet:
            self.unavailable("dotnet is required for the authentication store test")

        test_assembly = (
            ROOT
            / "tests/dotnet/AssetLibrary.WebGateway.Tests/"
            "bin/Release/net10.0/AssetLibrary.WebGateway.Tests.dll"
        )
        if not test_assembly.is_file():
            self.unavailable(f"authentication test build is missing: {test_assembly}")

        database = self.authentication_database("dotnet_auth_store")
        account = self.seed_local_account(database, is_system_administrator=True)
        environment = self.environment.copy()
        environment["ASSETLIBRARY_TEST_AUTH_CONNECTION"] = (
            f"Host={self.host};Port={self.port};Database={database};"
            f"Username={self.RUNTIME};Pooling=false;Timeout=5;Command Timeout=5;"
            "SSL Mode=Disable"
        )
        environment["ASSETLIBRARY_TEST_AUTH_SECRET"] = str(account["test_secret"])
        environment["DOTNET_NOLOGO"] = "1"
        result = subprocess.run(
            [
                str(dotnet),
                "test",
                str(
                    ROOT
                    / "tests/dotnet/AssetLibrary.WebGateway.Tests/"
                    "AssetLibrary.WebGateway.Tests.csproj"
                ),
                "--configuration",
                "Release",
                "--no-build",
                "--no-restore",
                "--filter",
                (
                    "FullyQualifiedName=AssetLibrary.WebGateway.Tests."
                    "PostgresAuthenticationStoreIntegrationTests."
                    "PostgresStoreCompletesLocalSignInCsrfAndRevocationRoundTrip"
                ),
                "--logger",
                "console;verbosity=minimal",
            ],
            cwd=ROOT,
            env=environment,
            text=True,
            encoding="utf-8",
            errors="replace",
            capture_output=True,
            timeout=120,
            check=False,
        )
        self.assertEqual(
            result.returncode,
            0,
            result.stderr or result.stdout,
        )

    def test_read_core_roots_initial_commit_and_scan_status_are_fail_closed(self) -> None:
        database = self.fresh_database("read_core")
        backups = self.backup_directory("read_core")
        MIGRATIONS.apply_migrations(self.runner_tools(database), self.manifest, backups)
        self.sql(
            database,
            self.admin,
            (
                f"GRANT assetlibrary_asset_identity_runtime, "
                f"assetlibrary_scan_reconciliation_runtime TO {self.RUNTIME} "
                "WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;"
            ),
        )

        source_id = uuid.uuid4()
        library_id = uuid.uuid4()
        scan_id = uuid.uuid4()
        empty_library_id = uuid.uuid4()
        empty_scan_id = uuid.uuid4()
        incomplete_scan_id = uuid.uuid4()
        mixed_scan_id = uuid.uuid4()
        entry_id = uuid.uuid4()
        folder_id = uuid.uuid4()
        created_at = "2026-09-03T12:00:00Z"
        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_library_storage_runtime;
INSERT INTO library_storage.storage_source (
  storage_source_id, display_name, availability, root_case_sensitive, availability_observed_at
) VALUES ('{source_id}', 'sandbox', 'online', false, '{created_at}');
SELECT library_storage.register_library_root(
  '{library_id}', '{source_id}', 'photos', 'C:/assets', '{created_at}'
);
SELECT library_storage.register_library_root(
  '{empty_library_id}', '{source_id}', 'empty', 'C:/empty', '{created_at}'
);
""",
        )
        overlap = self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_library_storage_runtime;
SELECT library_storage.register_library_root(
  '{uuid.uuid4()}', '{source_id}', 'nested', 'c:/ASSETS/nested', '{created_at}'
);
""",
            check=False,
        )
        self.assertNotEqual(overlap.returncode, 0)
        exact_overlap = self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_library_storage_runtime;
SELECT library_storage.register_library_root(
  '{uuid.uuid4()}', '{source_id}', 'same', 'C:/ASSETS', '{created_at}'
);
""",
            check=False,
        )
        self.assertNotEqual(exact_overlap.returncode, 0)
        parent_overlap = self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_library_storage_runtime;
SELECT library_storage.register_library_root(
  '{uuid.uuid4()}', '{source_id}', 'parent', 'C:/', '{created_at}'
);
""",
            check=False,
        )
        self.assertNotEqual(parent_overlap.returncode, 0)
        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_library_storage_runtime;
SELECT library_storage.register_library_root(
  '{uuid.uuid4()}', '{source_id}', 'prefix-lookalike', 'C:/assetshop', '{created_at}'
);
""",
        )
        root_count = self.sql(
            database,
            self.RUNTIME,
            "SET ROLE assetlibrary_library_storage_runtime; "
            "SELECT count(*) FROM library_storage.library_root;",
        ).stdout.strip()
        self.assertEqual(root_count, "3")

        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_asset_identity_runtime;
INSERT INTO asset_identity.scan_observation_stage (
  scan_id, entry_id, library_id, normalized_relative_path, kind,
  content_length, last_write_time_utc, staged_at
) VALUES
  ('{scan_id}', '{folder_id}', '{library_id}', 'photos', 'directory', NULL, '{created_at}', '{created_at}'),
  ('{scan_id}', '{entry_id}', '{library_id}', 'photos/image.jpg', 'file', 12, '{created_at}', '{created_at}');
""",
        )
        committed = self.sql(
            database,
            self.RUNTIME,
            f"SET ROLE assetlibrary_asset_identity_runtime; "
            f"SELECT asset_identity.commit_initial_scan('{scan_id}', '{library_id}', '{created_at}');",
        ).stdout.strip()
        self.assertEqual(committed, "2")
        counts = self.sql(
            database,
            self.RUNTIME,
            "SET ROLE assetlibrary_asset_identity_runtime; "
            "SELECT (SELECT count(*) FROM asset_identity.filesystem_entry), "
            "(SELECT count(*) FROM asset_identity.scan_observation_stage), "
            "(SELECT entry_count FROM asset_identity.library_index_snapshot "
            f"WHERE library_id = '{library_id}');",
        ).stdout.strip()
        self.assertEqual(counts, "2|0|2")
        repeated = self.sql(
            database,
            self.RUNTIME,
            f"SET ROLE assetlibrary_asset_identity_runtime; "
            f"SELECT asset_identity.commit_initial_scan('{uuid.uuid4()}', '{library_id}', '{created_at}');",
            check=False,
        )
        self.assertNotEqual(repeated.returncode, 0)

        empty_committed = self.sql(
            database,
            self.RUNTIME,
            f"SET ROLE assetlibrary_asset_identity_runtime; "
            f"SELECT asset_identity.commit_initial_scan("
            f"'{empty_scan_id}', '{empty_library_id}', '{created_at}');",
        ).stdout.strip()
        self.assertEqual(empty_committed, "0")
        empty_snapshot = self.sql(
            database,
            self.RUNTIME,
            "SET ROLE assetlibrary_asset_identity_runtime; "
            "SELECT entry_count FROM asset_identity.library_index_snapshot "
            f"WHERE library_id = '{empty_library_id}';",
        ).stdout.strip()
        self.assertEqual(empty_snapshot, "0")
        repeated_empty = self.sql(
            database,
            self.RUNTIME,
            f"SET ROLE assetlibrary_asset_identity_runtime; "
            f"SELECT asset_identity.commit_initial_scan("
            f"'{uuid.uuid4()}', '{empty_library_id}', '{created_at}');",
            check=False,
        )
        self.assertNotEqual(repeated_empty.returncode, 0)

        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_asset_identity_runtime;
INSERT INTO asset_identity.scan_observation_stage (
  scan_id, entry_id, library_id, normalized_relative_path, kind,
  content_length, last_write_time_utc, staged_at
) VALUES
  ('{incomplete_scan_id}', '{uuid.uuid4()}', '{library_id}', 'incomplete.bin', 'file', 1, '{created_at}', '{created_at}');
""",
        )
        aborted = self.sql(
            database,
            self.RUNTIME,
            f"SET ROLE assetlibrary_asset_identity_runtime; "
            f"SELECT asset_identity.abort_initial_scan('{incomplete_scan_id}');",
        ).stdout.strip()
        self.assertEqual(aborted, "1")

        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_asset_identity_runtime;
INSERT INTO asset_identity.scan_observation_stage (
  scan_id, entry_id, library_id, normalized_relative_path, kind,
  content_length, last_write_time_utc, staged_at
) VALUES
  ('{mixed_scan_id}', '{uuid.uuid4()}', '{library_id}', 'mixed-a.bin', 'file', 1, '{created_at}', '{created_at}'),
  ('{mixed_scan_id}', '{uuid.uuid4()}', '{empty_library_id}', 'mixed-b.bin', 'file', 1, '{created_at}', '{created_at}');
""",
        )
        mixed_commit = self.sql(
            database,
            self.RUNTIME,
            f"SET ROLE assetlibrary_asset_identity_runtime; "
            f"SELECT asset_identity.commit_initial_scan("
            f"'{mixed_scan_id}', '{library_id}', '{created_at}');",
            check=False,
        )
        self.assertNotEqual(mixed_commit.returncode, 0)
        retained_stage = self.sql(
            database,
            self.RUNTIME,
            "SET ROLE assetlibrary_asset_identity_runtime; "
            "SELECT count(*) FROM asset_identity.scan_observation_stage "
            f"WHERE scan_id = '{mixed_scan_id}';",
        ).stdout.strip()
        self.assertEqual(retained_stage, "2")
        mixed_aborted = self.sql(
            database,
            self.RUNTIME,
            f"SET ROLE assetlibrary_asset_identity_runtime; "
            f"SELECT asset_identity.abort_initial_scan('{mixed_scan_id}');",
        ).stdout.strip()
        self.assertEqual(mixed_aborted, "2")

        self.sql(
            database,
            self.RUNTIME,
            f"""
SET ROLE assetlibrary_scan_reconciliation_runtime;
INSERT INTO scan_reconciliation.scan_run (
  scan_id, library_id, scan_kind, status, started_at
) VALUES ('{scan_id}', '{library_id}', 'initial_read_only', 'running', '{created_at}');
UPDATE scan_reconciliation.scan_run
SET status = 'completed', observed_entries = 2, committed_entries = 2, finished_at = '{created_at}'
WHERE scan_id = '{scan_id}';
""",
        )
        status = self.sql(
            database,
            self.RUNTIME,
            "SET ROLE assetlibrary_scan_reconciliation_runtime; "
            "SELECT status, observed_entries, committed_entries "
            "FROM scan_reconciliation.scan_run;",
        ).stdout.strip()
        self.assertEqual(status, "completed|2|2")

        cross_schema_write = self.sql(
            database,
            self.RUNTIME,
            f"SET ROLE assetlibrary_asset_identity_runtime; "
            f"DELETE FROM scan_reconciliation.scan_run WHERE scan_id = '{scan_id}';",
            check=False,
        )
        self.assertNotEqual(cross_schema_write.returncode, 0)

    def test_task_health_durable_tasks_are_idempotent_fenced_and_cancelable(self) -> None:
        database = self.task_health_database("th_tasks")
        original_id = uuid.uuid4()
        replay_id = uuid.uuid4()

        created = self.task_health_sql(
            database,
            self.enqueue_task_statement(original_id, "idempotent-task"),
        ).stdout.strip()
        replayed = self.task_health_sql(
            database,
            self.enqueue_task_statement(replay_id, "idempotent-task"),
        ).stdout.strip()
        self.assertEqual(created, f"{original_id}|t")
        self.assertEqual(replayed, f"{original_id}|f")
        conflict = self.task_health_sql(
            database,
            self.enqueue_task_statement(
                uuid.uuid4(),
                "idempotent-task",
                payload='{"value": 2}',
            ),
            check=False,
        )
        self.assertNotEqual(conflict.returncode, 0)
        self.assertIn("different task request", conflict.stderr)

        low_id = uuid.uuid4()
        high_id = uuid.uuid4()
        self.task_health_sql(
            database,
            self.enqueue_task_statement(low_id, "priority-low", priority=5),
        )
        self.task_health_sql(
            database,
            self.enqueue_task_statement(high_id, "priority-high", priority=0),
        )
        high_lease = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.priority', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(high_lease[0], str(high_id))
        self.assertEqual(high_lease[4], "1")
        self.assertEqual(high_lease[5], "3")
        self.assertEqual(high_lease[7], "1")
        wrong_token = self.task_health_sql(
            database,
            "SELECT * FROM task_health.heartbeat_durable_task("
            f"'{high_id}', 'worker.priority', '{uuid.uuid4()}', {high_lease[7]}, 60);",
        ).stdout.strip()
        self.assertEqual(wrong_token, "")
        wrong_generation = self.task_health_sql(
            database,
            "SELECT task_health.finish_durable_task("
            f"'{high_id}', 'worker.priority', '{high_lease[6]}', 999, 'succeeded');",
        ).stdout.strip()
        self.assertEqual(wrong_generation, "")
        completed = self.task_health_sql(
            database,
            "SELECT task_health.finish_durable_task("
            f"'{high_id}', 'worker.priority', '{high_lease[6]}', {high_lease[7]}, 'succeeded');",
        ).stdout.strip()
        self.assertEqual(completed, "succeeded")

        leased = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.cancel', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(leased[0], str(original_id))
        self.assertEqual(
            self.task_health_sql(
                database,
                f"SELECT task_health.request_durable_task_cancellation('{original_id}');",
            ).stdout.strip(),
            "requested",
        )
        heartbeat = self.task_health_sql(
            database,
            "SELECT cancellation_requested FROM task_health.heartbeat_durable_task("
            f"'{original_id}', 'worker.cancel', '{leased[6]}', {leased[7]}, 60);",
        ).stdout.strip()
        self.assertEqual(heartbeat, "t")
        cancelled = self.task_health_sql(
            database,
            "SELECT task_health.finish_durable_task("
            f"'{original_id}', 'worker.cancel', '{leased[6]}', {leased[7]}, "
            "'retryable_failure', 'transient.io', 0);",
        ).stdout.strip()
        self.assertEqual(cancelled, "cancelled")
        self.assertEqual(
            self.task_health_sql(
                database,
                f"SELECT task_health.request_durable_task_cancellation('{low_id}');",
            ).stdout.strip(),
            "cancelled",
        )

        success_race_id = uuid.uuid4()
        self.task_health_sql(
            database,
            self.enqueue_task_statement(success_race_id, "success-cancel-race", priority=1),
        )
        success_race_lease = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.success', 1, 60);",
        ).stdout.strip().split("|")
        self.task_health_sql(
            database,
            f"SELECT task_health.request_durable_task_cancellation('{success_race_id}');",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.finish_durable_task("
                f"'{success_race_id}', 'worker.success', '{success_race_lease[6]}', "
                f"{success_race_lease[7]}, 'succeeded');",
            ).stdout.strip(),
            "succeeded",
        )

        stale_id = uuid.uuid4()
        self.task_health_sql(
            database,
            self.enqueue_task_statement(stale_id, "stale-worker-task"),
        )
        old_lease = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.old', 1, 60);",
        ).stdout.strip().split("|")
        self.sql(
            database,
            self.admin,
            "UPDATE task_health.durable_task SET lease_until = clock_timestamp() - interval '1 second' "
            f"WHERE task_id = '{stale_id}';",
        )
        expired_finish = self.task_health_sql(
            database,
            "SELECT task_health.finish_durable_task("
            f"'{stale_id}', 'worker.old', '{old_lease[6]}', {old_lease[7]}, 'succeeded');",
        ).stdout.strip()
        self.assertEqual(expired_finish, "")
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.reclaim_expired_durable_tasks(10);",
            ).stdout.strip(),
            "1",
        )
        new_lease = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.new', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(new_lease[0], str(stale_id))
        self.assertEqual(new_lease[4], "2")
        self.assertEqual(new_lease[7], "2")
        self.assertNotEqual(old_lease[6], new_lease[6])
        stale_finish = self.task_health_sql(
            database,
            "SELECT task_health.finish_durable_task("
            f"'{stale_id}', 'worker.old', '{old_lease[6]}', {old_lease[7]}, 'succeeded');",
        ).stdout.strip()
        self.assertEqual(stale_finish, "")
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.finish_durable_task("
                f"'{stale_id}', 'worker.new', '{new_lease[6]}', {new_lease[7]}, 'succeeded');",
            ).stdout.strip(),
            "succeeded",
        )

        retry_id = uuid.uuid4()
        self.task_health_sql(
            database,
            self.enqueue_task_statement(
                retry_id,
                "attempt-limited-task",
                priority=1,
                max_attempts=2,
            ),
        )
        first = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.retry', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.finish_durable_task("
                f"'{retry_id}', 'worker.retry', '{first[6]}', {first[7]}, "
                "'retryable_failure', 'transient.io', 0);",
            ).stdout.strip(),
            "queued",
        )
        second = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.retry', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(second[4], "2")
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.finish_durable_task("
                f"'{retry_id}', 'worker.retry', '{second[6]}', {second[7]}, "
                "'retryable_failure', 'transient.io', 0);",
            ).stdout.strip(),
            "failed",
        )

        cancel_expired_id = uuid.uuid4()
        self.task_health_sql(
            database,
            self.enqueue_task_statement(cancel_expired_id, "cancel-expired-task", priority=1),
        )
        cancel_expired_lease = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.cancel.expired', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(cancel_expired_lease[0], str(cancel_expired_id))
        self.task_health_sql(
            database,
            f"SELECT task_health.request_durable_task_cancellation('{cancel_expired_id}');",
        )
        self.sql(
            database,
            self.admin,
            "UPDATE task_health.durable_task SET lease_until = clock_timestamp() - interval '1 second' "
            f"WHERE task_id = '{cancel_expired_id}';",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.reclaim_expired_durable_tasks(10);",
            ).stdout.strip(),
            "1",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                f"SELECT state FROM task_health.durable_task WHERE task_id = '{cancel_expired_id}';",
            ).stdout.strip(),
            "cancelled",
        )

    def test_task_health_concurrent_claims_are_disjoint_and_bounded(self) -> None:
        database = self.task_health_database("th_claim")
        task_ids = [uuid.uuid4() for _ in range(20)]
        enqueue = "\n".join(
            self.enqueue_task_statement(task_id, f"bulk-task-{index}")
            for index, task_id in enumerate(task_ids)
        )
        self.task_health_sql(database, enqueue)

        def claim(worker: str) -> list[str]:
            output = self.task_health_sql(
                database,
                f"SELECT task_id FROM task_health.claim_durable_tasks('{worker}', 10, 60);",
            ).stdout
            return [line for line in output.splitlines() if line]

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            futures = [
                executor.submit(claim, "worker.concurrent.a"),
                executor.submit(claim, "worker.concurrent.b"),
            ]
            claims = [future.result(timeout=30) for future in futures]

        self.assertEqual([len(rows) for rows in claims], [10, 10])
        flattened = claims[0] + claims[1]
        self.assertEqual(len(set(flattened)), 20)
        self.assertEqual(set(flattened), {str(task_id) for task_id in task_ids})
        state_counts = self.task_health_sql(
            database,
            "SELECT state, count(*) FROM task_health.durable_task GROUP BY state ORDER BY state;",
        ).stdout.strip()
        self.assertEqual(state_counts, "leased|20")
        null_batch = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_durable_tasks('worker.invalid', NULL, 60);",
            check=False,
        )
        self.assertNotEqual(null_batch.returncode, 0)

    def test_task_health_outbox_health_and_least_privilege_contracts(self) -> None:
        database = self.task_health_database("th_outbox")
        event_id = uuid.uuid4()
        self.assertEqual(
            self.task_health_sql(
                database,
                self.enqueue_outbox_statement(event_id),
            ).stdout.strip(),
            "t",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                self.enqueue_outbox_statement(event_id),
            ).stdout.strip(),
            "f",
        )
        conflict = self.task_health_sql(
            database,
            self.enqueue_outbox_statement(event_id, payload='{"value": 2}'),
            check=False,
        )
        self.assertNotEqual(conflict.returncode, 0)
        self.assertIn("different outbox event", conflict.stderr)

        first = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_outbox_events('publisher.old', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(first[0], str(event_id))
        self.assertEqual(first[7], "1")
        self.assertEqual(first[10], "1")
        wrong_owner = self.task_health_sql(
            database,
            "SELECT task_health.mark_outbox_event_published("
            f"'{event_id}', 'publisher.wrong', '{first[9]}', {first[10]});",
        ).stdout.strip()
        self.assertEqual(wrong_owner, "f")
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.release_outbox_event("
                f"'{event_id}', 'publisher.old', '{first[9]}', {first[10]}, "
                "'provider.timeout', 0);",
            ).stdout.strip(),
            "pending",
        )
        second = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_outbox_events('publisher.new', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(second[7], "2")
        self.assertEqual(second[10], "2")
        self.assertNotEqual(second[9], first[9])
        stale_publish = self.task_health_sql(
            database,
            "SELECT task_health.mark_outbox_event_published("
            f"'{event_id}', 'publisher.old', '{first[9]}', {first[10]});",
        ).stdout.strip()
        self.assertEqual(stale_publish, "f")
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.mark_outbox_event_published("
                f"'{event_id}', 'publisher.new', '{second[9]}', {second[10]});",
            ).stdout.strip(),
            "t",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                f"SELECT state FROM task_health.outbox_event WHERE event_id = '{event_id}';",
            ).stdout.strip(),
            "published",
        )

        dead_id = uuid.uuid4()
        self.task_health_sql(
            database,
            self.enqueue_outbox_statement(dead_id, max_attempts=2),
        )
        for expected_state in ("pending", "dead_lettered"):
            lease = self.task_health_sql(
                database,
                "SELECT * FROM task_health.claim_outbox_events('publisher.retry', 1, 60);",
            ).stdout.strip().split("|")
            state = self.task_health_sql(
                database,
                "SELECT task_health.release_outbox_event("
                f"'{dead_id}', 'publisher.retry', '{lease[9]}', {lease[10]}, "
                "'provider.timeout', 0);",
            ).stdout.strip()
            self.assertEqual(state, expected_state)

        expired_event_id = uuid.uuid4()
        self.task_health_sql(
            database,
            self.enqueue_outbox_statement(expired_event_id, max_attempts=1),
        )
        expired_event_lease = self.task_health_sql(
            database,
            "SELECT * FROM task_health.claim_outbox_events('publisher.expired', 1, 60);",
        ).stdout.strip().split("|")
        self.assertEqual(expired_event_lease[0], str(expired_event_id))
        self.sql(
            database,
            self.admin,
            "UPDATE task_health.outbox_event SET lease_until = clock_timestamp() - interval '1 second' "
            f"WHERE event_id = '{expired_event_id}';",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.reclaim_expired_outbox_events(10);",
            ).stdout.strip(),
            "1",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT state, last_failure_code FROM task_health.outbox_event "
                f"WHERE event_id = '{expired_event_id}';",
            ).stdout.strip(),
            "dead_lettered|lease_expired",
        )

        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.write_health_status("
                "'system', NULL, 'database', 'normal', NULL, "
                "'2026-09-03T01:00:00Z', '2026-09-03T01:00:01Z');",
            ).stdout.strip(),
            "t",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT task_health.write_health_status("
                "'system', NULL, 'database', 'offline', 'database.offline', "
                "'2026-09-03T00:59:00Z', '2026-09-03T01:00:02Z');",
            ).stdout.strip(),
            "f",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT state, reason_code FROM task_health.health_status "
                "WHERE scope_kind = 'system' AND component = 'database';",
            ).stdout.strip(),
            "normal|",
        )
        library_id = uuid.uuid4()
        asset_id = uuid.uuid4()
        for scope, resource_id in (("library", library_id), ("asset", asset_id)):
            self.assertEqual(
                self.task_health_sql(
                    database,
                    "SELECT task_health.write_health_status("
                    f"'{scope}', '{resource_id}', 'storage', 'offline', 'storage.offline', "
                    "'2026-09-03T01:00:00Z', '2026-09-03T01:00:01Z');",
                ).stdout.strip(),
                "t",
            )
        invalid_scope = self.task_health_sql(
            database,
            "SELECT task_health.write_health_status("
            "'library', NULL, 'storage', 'offline', 'storage.offline', "
            "clock_timestamp(), clock_timestamp());",
            check=False,
        )
        self.assertNotEqual(invalid_scope.returncode, 0)

        for denied in (
            "UPDATE task_health.durable_task SET priority = 0;",
            "DELETE FROM task_health.outbox_event;",
            "INSERT INTO task_health.health_status "
            "(scope_kind, scope_id, component, state, observed_at, updated_at) "
            "VALUES ('system', NULL, 'denied', 'normal', clock_timestamp(), clock_timestamp());",
        ):
            self.assertNotEqual(
                self.task_health_sql(database, denied, check=False).returncode,
                0,
                denied,
            )

    def test_task_health_large_queues_use_bounded_indexed_claims(self) -> None:
        database = self.task_health_database("th_scale")
        task_count = self.task_health_sql(
            database,
            """
SELECT count(*)
FROM generate_series(1, 10000) AS batch(item)
CROSS JOIN LATERAL task_health.enqueue_durable_task(
  gen_random_uuid(), 'scale-task-' || batch.item, 'scan.initial',
  jsonb_build_object('item', batch.item), 2::smallint, 3,
  clock_timestamp(), clock_timestamp()
) AS queued;
""",
        ).stdout.strip()
        event_count = self.task_health_sql(
            database,
            """
SELECT count(*)
FROM generate_series(1, 10000) AS batch(item)
CROSS JOIN LATERAL task_health.enqueue_outbox_event(
  gen_random_uuid(), 'AssetIdentity', 'asset.indexed', NULL, 1,
  jsonb_build_object('item', batch.item), clock_timestamp(), 3,
  clock_timestamp(), clock_timestamp()
) AS queued;
""",
        ).stdout.strip()
        self.assertEqual(task_count, "10000")
        self.assertEqual(event_count, "10000")

        task_plan = self.task_health_sql(
            database,
            """
EXPLAIN (COSTS OFF)
SELECT task_id
FROM task_health.durable_task
WHERE state = 'queued'
  AND cancellation_requested_at IS NULL
  AND available_at <= clock_timestamp()
  AND attempts < max_attempts
ORDER BY priority, available_at, created_at, task_id
LIMIT 256;
""",
        ).stdout
        outbox_plan = self.task_health_sql(
            database,
            """
EXPLAIN (COSTS OFF)
SELECT event_id
FROM task_health.outbox_event
WHERE state = 'pending'
  AND available_at <= clock_timestamp()
  AND publish_attempts < max_publish_attempts
ORDER BY available_at, occurred_at, event_id
LIMIT 256;
""",
        ).stdout
        self.assertIn("durable_task_claim_index", task_plan)
        self.assertIn("outbox_event_claim_index", outbox_plan)
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT count(*) FROM task_health.claim_durable_tasks('worker.scale', 256, 60);",
            ).stdout.strip(),
            "256",
        )
        self.assertEqual(
            self.task_health_sql(
                database,
                "SELECT count(*) FROM task_health.claim_outbox_events('publisher.scale', 256, 60);",
            ).stdout.strip(),
            "256",
        )

    def test_task_health_rows_survive_a_database_restart(self) -> None:
        if self.external:
            self.skipTest("restart persistence requires the disposable self-hosted cluster")
        database = self.task_health_database("th_restart")
        task_id = uuid.uuid4()
        event_id = uuid.uuid4()
        self.task_health_sql(
            database,
            self.enqueue_task_statement(task_id, "restart-task"),
        )
        self.task_health_sql(
            database,
            self.enqueue_outbox_statement(event_id),
        )
        self.task_health_sql(
            database,
            "SELECT task_health.write_health_status("
            "'system', NULL, 'database', 'normal', NULL, "
            "clock_timestamp(), clock_timestamp());",
        )
        assert self.pg_ctl is not None and self.cluster_data is not None

        self._run(
            [
                str(self.pg_ctl),
                "--pgdata", str(self.cluster_data),
                "--wait",
                "--timeout", "30",
                "--mode", "fast",
                "restart",
            ],
            timeout=45,
            capture_output=False,
        )

        persisted = self.task_health_sql(
            database,
            "SELECT "
            "(SELECT count(*) FROM task_health.durable_task), "
            "(SELECT count(*) FROM task_health.outbox_event), "
            "(SELECT count(*) FROM task_health.health_status);",
        ).stdout.strip()
        self.assertEqual(persisted, "1|1|1")

    def test_concurrent_apply_is_serialized(self) -> None:
        database = self.fresh_database("concurrent")
        backups = self.backup_directory("concurrent")

        def run_once() -> None:
            tools = self.runner_tools(database)
            MIGRATIONS.apply_migrations(tools, self.manifest, backups)

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            futures = [executor.submit(run_once) for _ in range(2)]
            for future in futures:
                future.result(timeout=60)

        _, rows = MIGRATIONS.ledger_rows(self.runner_tools(database), self.manifest)
        self.assertEqual([row["version"] for row in rows], list(range(1, 12)))

    def test_provisioning_rejects_unexpected_fixed_role_membership(self) -> None:
        database = self.fresh_database("unexpected_membership")
        unexpected_role = "assetlibrary_v01003_unexpected_parent"
        self.sql(
            "postgres",
            self.admin,
            f"CREATE ROLE {unexpected_role} NOLOGIN; "
            f"GRANT {unexpected_role} TO assetlibrary_library_storage_runtime;",
        )
        self.addCleanup(
            self.sql,
            "postgres",
            self.admin,
            f"DROP ROLE IF EXISTS {unexpected_role};",
        )
        admin_tools = MIGRATIONS.PostgresTools(
            self.manifest, self.connection(database, self.admin)
        )

        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "unexpected role memberships"):
            MIGRATIONS.provision_roles(admin_tools, self.manifest, explicit=True)

    def test_failure_is_atomic_and_ledger_drift_precedes_backup(self) -> None:
        database = self.fresh_database("failure")
        backups = self.backup_directory("failure")
        base_tools = self.runner_tools(database)
        MIGRATIONS.apply_migrations(base_tools, self.manifest, backups)
        failing = self.staged_manifest(
            "intentional_failure",
            "CREATE TABLE library_storage.failure_sentinel(id integer);\nSELECT 1 / 0;\n",
        )
        before_failure = len(list(backups.iterdir()))

        with self.assertRaises(MIGRATIONS.MigrationError):
            MIGRATIONS.apply_migrations(
                self.runner_tools(database, failing), failing, backups
            )
        self.assertEqual(
            self.sql(
                database,
                self.admin,
                "SELECT to_regclass('library_storage.failure_sentinel') IS NULL;",
            ).stdout.strip(),
            "t",
        )
        self.assertEqual(
            self.sql(
                database,
                self.admin,
                "SELECT count(*) FROM migration.ledger;",
            ).stdout.strip(),
            "11",
        )
        self.assertGreater(len(list(backups.iterdir())), before_failure)

        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        drift_root = Path(temporary.name) / "production"
        shutil.copytree(TOOL_PATH.parent, drift_root)
        drift_sql = drift_root / "0002_module_privilege_projection.sql"
        drift_sql.write_text(
            drift_sql.read_text(encoding="utf-8") + "-- rewritten applied migration\n",
            encoding="utf-8",
            newline="\n",
        )
        data_path = drift_root / "manifest.json"
        data = json.loads(data_path.read_text(encoding="utf-8"))
        data["migrations"][1]["sha256"] = hashlib.sha256(drift_sql.read_bytes()).hexdigest()
        data_path.write_text(
            json.dumps(data, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        drift_manifest = MIGRATIONS.load_manifest(data_path)
        before_drift = sorted(path.name for path in backups.iterdir())
        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "ledger drift"):
            MIGRATIONS.apply_migrations(
                self.runner_tools(database, drift_manifest), drift_manifest, backups
            )
        self.assertEqual(sorted(path.name for path in backups.iterdir()), before_drift)

        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_migration_owner; "
            "UPDATE migration.ledger SET name = 'renamed_applied_migration' WHERE version = 2;",
        )
        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "ledger drift"):
            MIGRATIONS.apply_migrations(
                self.runner_tools(database, failing), failing, backups
            )
        self.assertEqual(sorted(path.name for path in backups.iterdir()), before_drift)

        self.sql(
            database,
            self.admin,
            "SET ROLE assetlibrary_migration_owner; "
            "UPDATE migration.ledger SET name = 'module_privilege_projection' WHERE version = 2; "
            "DELETE FROM migration.ledger WHERE version = 1;",
        )
        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "ledger drift"):
            MIGRATIONS.apply_migrations(
                self.runner_tools(database, failing), failing, backups
            )
        self.assertEqual(sorted(path.name for path in backups.iterdir()), before_drift)

    def test_verified_backup_restores_to_empty_database_and_replays_forward(self) -> None:
        source = self.fresh_database("restore_source")
        target = self.fresh_database("restore_target")
        corrupt_target = self.fresh_database("restore_corrupt")
        backups = self.backup_directory("restore")
        base_tools = self.runner_tools(source)
        MIGRATIONS.apply_migrations(base_tools, self.manifest, backups)
        self.sql(
            source,
            self.admin,
            "SET ROLE assetlibrary_library_storage_owner; "
            "CREATE TABLE library_storage.restore_marker(id integer PRIMARY KEY, value text NOT NULL); "
            "INSERT INTO library_storage.restore_marker VALUES (1, 'preserved');",
        )
        staged = self.staged_manifest(
            "restore_forward_probe",
            "CREATE TABLE library_storage.after_restore(id integer PRIMARY KEY);\n",
        )
        staged_tools = self.runner_tools(source, staged)
        backup = MIGRATIONS.apply_migrations(staged_tools, staged, backups)
        self.assertIsNotNone(backup)
        assert backup is not None

        target_tools = self.runner_tools(target, staged)
        MIGRATIONS.restore_backup(target_tools, staged, backup.metadata_path)
        self.assertEqual(
            self.sql(
                target,
                self.admin,
                "SELECT value FROM library_storage.restore_marker WHERE id=1;",
            ).stdout.strip(),
            "preserved",
        )
        self.assertEqual(
            self.sql(
                target,
                self.admin,
                "SELECT to_regclass('library_storage.after_restore') IS NULL;",
            ).stdout.strip(),
            "t",
        )
        replay_backups = self.backup_directory("restore-replay")
        MIGRATIONS.apply_migrations(target_tools, staged, replay_backups)
        self.assertEqual(
            self.sql(
                target,
                self.admin,
                "SELECT to_regclass('library_storage.after_restore') IS NOT NULL;",
            ).stdout.strip(),
            "t",
        )
        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "not empty"):
            MIGRATIONS.restore_backup(target_tools, staged, backup.metadata_path)

        self.sql(
            corrupt_target,
            self.admin,
            "CREATE FUNCTION public.restore_must_refuse() RETURNS integer "
            "LANGUAGE SQL AS $$ SELECT 1 $$;",
        )
        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "not empty"):
            MIGRATIONS.restore_backup(
                self.runner_tools(corrupt_target, staged), staged, backup.metadata_path
            )
        self.assertEqual(
            self.sql(
                corrupt_target,
                self.admin,
                "SELECT public.restore_must_refuse();",
            ).stdout.strip(),
            "1",
        )
        self.sql(
            corrupt_target,
            self.admin,
            "DROP FUNCTION public.restore_must_refuse();",
        )

        corrupt_archive = backups / "corrupt.dump"
        content = backup.archive_path.read_bytes()
        corrupt_archive.write_bytes(content[: max(1, len(content) // 2)])
        corrupt_metadata = backups / "corrupt.json"
        metadata = json.loads(backup.metadata_path.read_text(encoding="utf-8"))
        metadata["archive_file"] = corrupt_archive.name
        metadata["archive_sha256"] = hashlib.sha256(corrupt_archive.read_bytes()).hexdigest()
        corrupt_metadata.write_text(
            json.dumps(metadata, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        with self.assertRaises(MIGRATIONS.MigrationError):
            MIGRATIONS.restore_backup(
                self.runner_tools(corrupt_target, staged), staged, corrupt_metadata
            )
        MIGRATIONS.ensure_empty_restore_target(self.runner_tools(corrupt_target, staged))

    def test_core_server_database_readiness_is_live_and_fail_closed(self) -> None:
        database = self.fresh_database("host_readiness")
        MIGRATIONS.apply_migrations(
            self.runner_tools(database),
            self.manifest,
            self.backup_directory("host-readiness"),
        )
        runtime_parent = Path(
            os.environ.get(
                "ASSETLIBRARY_TEST_RUNTIME",
                ROOT / ".runtime/sandbox-storage/V01-010",
            )
        ).resolve()
        runtime_parent.mkdir(parents=True, exist_ok=True)
        temporary = tempfile.TemporaryDirectory(
            prefix="database-runtime-host-",
            dir=runtime_parent,
        )
        self.addCleanup(temporary.cleanup)
        state = Path(temporary.name) / "state"
        state.mkdir()
        port = self._unused_port()
        secret = "v01-010-integration-secret"
        connection = (
            f"Host={self.host};Port={self.port};Database={database};"
            f"Username={self.AUDITOR};Password={secret}"
        )
        environment = self.environment.copy()
        environment["ASSETLIBRARY_DATABASE_READINESS_CONNECTION"] = connection
        flags = subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0
        process = subprocess.Popen(
            [
                *self.host_command(),
                "--state-path",
                str(state),
                "--environment",
                "Production",
                "--bind-host",
                "127.0.0.1",
                "--port",
                str(port),
            ],
            cwd=temporary.name,
            env=environment,
            text=True,
            encoding="utf-8",
            errors="replace",
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            creationflags=flags,
        )
        try:
            ready = self.wait_for_readiness(process, port, 200)
            self.assertEqual(ready.get("status"), "ready")
            self.assertEqual(ready.get("scope"), "host_database")
            self.assertEqual(ready.get("database_contract"), "v01-010/1")
            self.assertEqual(ready.get("database_schema_version"), 11)
            self.assertIs(ready.get("business_api_ready"), False)
            self.assertIs(ready.get("production_file_writes_enabled"), False)
            health_status, health = self.http_payload(port, path="/healthz")
            self.assertEqual(health_status, 200)
            self.assertEqual(health.get("status"), "ok")

            self.sql(
                database,
                self.admin,
                "SET ROLE assetlibrary_migration_owner; "
                "UPDATE migration.ledger SET name = 'runtime_drift' WHERE version = 9;",
            )
            self.wait_for_readiness(
                process,
                port,
                503,
                "database_not_ready",
            )
            self.sql(
                database,
                self.admin,
                "SET ROLE assetlibrary_migration_owner; "
                "UPDATE migration.ledger SET name = 'gateway_auth_read_api' WHERE version = 9;",
            )
            self.wait_for_readiness(process, port, 200)

            locker = subprocess.Popen(
                [
                    self.psql_path(),
                    "--no-psqlrc",
                    "--set", "ON_ERROR_STOP=1",
                    "--quiet",
                    "--host", self.host,
                    "--port", str(self.port),
                    "--username", self.admin,
                    "--dbname", database,
                    "--no-password",
                    "--command",
                    (
                        "BEGIN; "
                        "LOCK TABLE migration.ledger IN ACCESS EXCLUSIVE MODE; "
                        "SELECT pg_sleep(15); "
                        "ROLLBACK;"
                    ),
                ],
                env=self.environment,
                text=True,
                encoding="utf-8",
                errors="replace",
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )
            try:
                for _ in range(100):
                    lock_state = self.sql(
                        database,
                        self.admin,
                        """
SELECT COUNT(*)
FROM pg_locks
WHERE relation = 'migration.ledger'::regclass
  AND mode = 'AccessExclusiveLock'
  AND granted;
""",
                    ).stdout.strip()
                    if lock_state == "1":
                        break
                    if locker.poll() is not None:
                        locker_stdout, locker_stderr = locker.communicate(timeout=1)
                        raise AssertionError(
                            "migration ledger lock process exited early: "
                            f"{locker_stdout} {locker_stderr}"
                        )
                    time.sleep(0.1)
                else:
                    raise AssertionError("migration ledger lock was not acquired")

                timeout_started = time.monotonic()
                timeout_status, timeout_payload = self.http_payload(
                    port,
                    timeout_seconds=8,
                )
                timeout_elapsed = time.monotonic() - timeout_started
                self.assertEqual(timeout_status, 503)
                self.assertEqual(timeout_payload.get("reason"), "database_not_ready")
                self.assertGreaterEqual(timeout_elapsed, 4)
                self.assertLess(timeout_elapsed, 8)
            finally:
                self.stop_host(locker)
            self.wait_for_readiness(process, port, 200)

            self.drop_database(database)
            self.wait_for_readiness(process, port, 503, "database_not_ready")
        finally:
            stdout, stderr = self.stop_host(process)
        self.assertNotIn(secret, stdout)
        self.assertNotIn(secret, stderr)
        self.assertNotIn(database, stdout)
        self.assertNotIn(database, stderr)

        unauthorized = self.fresh_database("host_unauthorized")
        MIGRATIONS.apply_migrations(
            self.runner_tools(unauthorized),
            self.manifest,
            self.backup_directory("host-unauthorized"),
        )
        denied_environment = self.environment.copy()
        denied_environment["ASSETLIBRARY_DATABASE_READINESS_CONNECTION"] = (
            f"Host={self.host};Port={self.port};Database={unauthorized};"
            f"Username={self.RUNTIME};Password={secret}"
        )
        denied = subprocess.run(
            [
                *self.host_command(),
                "--state-path",
                str(state),
                "--environment",
                "Production",
                "--port",
                str(self._unused_port()),
            ],
            cwd=temporary.name,
            env=denied_environment,
            text=True,
            encoding="utf-8",
            errors="replace",
            capture_output=True,
            timeout=15,
            check=False,
        )
        self.assertEqual(denied.returncode, 69)
        self.assertIn("database_permission_denied", denied.stderr)
        self.assertNotIn(secret, denied.stderr)
        self.assertNotIn(unauthorized, denied.stderr)

        elevated_environment = self.environment.copy()
        elevated_environment["ASSETLIBRARY_DATABASE_READINESS_CONNECTION"] = (
            f"Host={self.host};Port={self.port};Database={unauthorized};"
            f"Username={self.admin};Password={secret}"
        )
        elevated = subprocess.run(
            [
                *self.host_command(),
                "--state-path",
                str(state),
                "--environment",
                "Production",
                "--port",
                str(self._unused_port()),
            ],
            cwd=temporary.name,
            env=elevated_environment,
            text=True,
            encoding="utf-8",
            errors="replace",
            capture_output=True,
            timeout=15,
            check=False,
        )
        self.assertEqual(elevated.returncode, 69)
        self.assertIn("database_permission_denied", elevated.stderr)
        self.assertNotIn(secret, elevated.stderr)
        self.assertNotIn(unauthorized, elevated.stderr)


if __name__ == "__main__":
    unittest.main()
