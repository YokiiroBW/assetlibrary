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
import unittest
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
                    ROOT / ".runtime/sandbox-storage/V01-003",
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
                ROOT / ".runtime/sandbox-storage/V01-003",
            )
        ).resolve()
        path = root / "integration-backups" / f"{label}-{uuid.uuid4().hex}"
        path.mkdir(parents=True, exist_ok=False)
        self.addCleanup(shutil.rmtree, path, True)
        return path

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
        self.assertEqual([row["version"] for row in rows], [1, 2])
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
        self.assertEqual([row["version"] for row in rows], [1, 2])

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
            "2",
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


if __name__ == "__main__":
    unittest.main()
