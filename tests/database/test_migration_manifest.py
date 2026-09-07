from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
TOOL_PATH = ROOT / "database/migrations/production/migration_tool.py"
SPEC = importlib.util.spec_from_file_location("assetlibrary_migration_tool", TOOL_PATH)
assert SPEC and SPEC.loader
MIGRATIONS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MIGRATIONS
SPEC.loader.exec_module(MIGRATIONS)


class ManifestFixture:
    def __init__(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / "production"
        shutil.copytree(TOOL_PATH.parent, self.root)
        self.manifest_path = self.root / "manifest.json"

    def close(self) -> None:
        self.temp.cleanup()

    def manifest(self) -> dict:
        return json.loads(self.manifest_path.read_text(encoding="utf-8"))

    def write_manifest(self, value: dict) -> None:
        self.manifest_path.write_text(
            json.dumps(value, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    def rewrite_sql_and_hash(self, filename: str, text: str) -> None:
        path = self.root / filename
        path.write_text(text, encoding="utf-8", newline="\n")
        data = self.manifest()
        if data["bootstrap"]["path"] == filename:
            data["bootstrap"]["sha256"] = MIGRATIONS.sha256_path(path)
        elif data["role_provisioning"]["path"] == filename:
            data["role_provisioning"]["sha256"] = MIGRATIONS.sha256_path(path)
        else:
            item = next(entry for entry in data["migrations"] if entry["path"] == filename)
            item["sha256"] = MIGRATIONS.sha256_path(path)
        self.write_manifest(data)


class MigrationManifestTests(unittest.TestCase):
    def setUp(self) -> None:
        self.fixture = ManifestFixture()
        self.addCleanup(self.fixture.close)

    def assert_manifest_error(self, marker: str) -> None:
        with self.assertRaises(MIGRATIONS.MigrationError) as caught:
            MIGRATIONS.load_manifest(self.fixture.manifest_path)
        self.assertIn(marker, str(caught.exception))

    def test_repository_manifest_is_valid_and_frozen(self) -> None:
        manifest = MIGRATIONS.load_manifest()

        self.assertEqual(manifest.postgresql_major, 16)
        self.assertEqual(manifest.verified_patch, "16.15")
        self.assertEqual(len(manifest.modules), 11)
        self.assertEqual(len(manifest.migrations), 21)
        self.assertEqual(
            [
                (item.version, item.path.name, item.module, item.owner_role)
                for item in manifest.migrations
            ],
            [
                (
                    1,
                    "0001_module_schema_ownership.sql",
                    "database-migration-foundation",
                    "assetlibrary_migration_owner",
                ),
                (
                    2,
                    "0002_module_privilege_projection.sql",
                    "database-migration-foundation",
                    "assetlibrary_migration_owner",
                ),
                (
                    3,
                    "0003_library_storage_core.sql",
                    "LibraryStorage",
                    "assetlibrary_library_storage_owner",
                ),
                (
                    4,
                    "0004_asset_identity_core.sql",
                    "AssetIdentity",
                    "assetlibrary_asset_identity_owner",
                ),
                (
                    5,
                    "0005_scan_reconciliation_core.sql",
                    "ScanReconciliation",
                    "assetlibrary_scan_reconciliation_owner",
                ),
                (
                    6,
                    "0006_task_health_core.sql",
                    "TaskHealth",
                    "assetlibrary_task_health_owner",
                ),
                (
                    7,
                    "0007_library_storage_read_permissions.sql",
                    "LibraryStorage",
                    "assetlibrary_library_storage_owner",
                ),
                (
                    8,
                    "0008_asset_identity_read_projection.sql",
                    "AssetIdentity",
                    "assetlibrary_asset_identity_owner",
                ),
                (
                    9,
                    "0009_gateway_auth_read_api.sql",
                    "GatewayAuth",
                    "assetlibrary_gateway_auth_owner",
                ),
                (
                    10,
                    "0010_gateway_auth_local_sessions.sql",
                    "GatewayAuth",
                    "assetlibrary_gateway_auth_owner",
                ),
                (
                    11,
                    "0011_gateway_auth_account_lifecycle.sql",
                    "GatewayAuth",
                    "assetlibrary_gateway_auth_owner",
                ),
                (
                    12,
                    "0012_gateway_auth_admin_bootstrap_recovery.sql",
                    "GatewayAuth",
                    "assetlibrary_gateway_auth_owner",
                ),
                (
                    13,
                    "0013_asset_identity_search_token_alignment.sql",
                    "AssetIdentity",
                    "assetlibrary_asset_identity_owner",
                ),
                (14, "0014_gateway_auth_recovery_preparation.sql", "GatewayAuth", "assetlibrary_gateway_auth_owner"),
                (15, "0015_library_storage_trial_registration.sql", "LibraryStorage", "assetlibrary_library_storage_owner"),
                (16, "0016_asset_identity_initial_scan_recovery.sql", "AssetIdentity", "assetlibrary_asset_identity_owner"),
                (17, "0017_scan_reconciliation_durable_requests.sql", "ScanReconciliation", "assetlibrary_scan_reconciliation_owner"),
                (18, "0018_task_health_scan_commit_guard.sql", "TaskHealth", "assetlibrary_task_health_owner"),
                (19, "0019_library_storage_categories.sql", "LibraryStorage", "assetlibrary_library_storage_owner"),
                (20, "0020_asset_identity_interactive_reads.sql", "AssetIdentity", "assetlibrary_asset_identity_owner"),
                (21, "0021_gateway_auth_interactive_reads.sql", "GatewayAuth", "assetlibrary_gateway_auth_owner"),
            ],
        )
        self.assertEqual(
            tuple((item.module, item.schema, item.owner_role, item.runtime_role) for item in manifest.modules),
            MIGRATIONS.EXPECTED_MODULES,
        )

    def test_validate_cli_is_connection_free(self) -> None:
        result = subprocess.run(
            [sys.executable, "-B", str(TOOL_PATH), "validate"],
            cwd=ROOT,
            text=True,
            encoding="utf-8",
            capture_output=True,
            check=False,
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("MIGRATION_MANIFEST_OK", result.stdout)

    def test_checksum_drift_is_rejected(self) -> None:
        migration = self.fixture.root / "0001_module_schema_ownership.sql"
        migration.write_text(
            migration.read_text(encoding="utf-8") + "-- drift\n",
            encoding="utf-8",
            newline="\n",
        )

        self.assert_manifest_error("migration 1 checksum drift")

    def test_unlisted_sql_is_rejected(self) -> None:
        (self.fixture.root / "9999_unreviewed.sql").write_text(
            "SELECT 1;\n", encoding="utf-8", newline="\n"
        )

        self.assert_manifest_error("unlisted production SQL files")

    def test_module_reordering_is_rejected(self) -> None:
        data = self.fixture.manifest()
        data["modules"][0], data["modules"][1] = data["modules"][1], data["modules"][0]
        self.fixture.write_manifest(data)

        self.assert_manifest_error("exactly match the 11 frozen ownership mappings")

    def test_malformed_checksum_types_are_reported_without_a_traceback(self) -> None:
        data = self.fixture.manifest()
        data["role_provisioning"]["sha256"] = 1
        data["bootstrap"]["sha256"] = []
        data["migrations"][0]["sha256"] = {}
        self.fixture.write_manifest(data)

        with self.assertRaises(MIGRATIONS.MigrationError) as caught:
            MIGRATIONS.load_manifest(self.fixture.manifest_path)

        message = str(caught.exception)
        self.assertIn("role_provisioning.sha256", message)
        self.assertIn("bootstrap.sha256", message)
        self.assertIn("migrations[0].sha256", message)

    def test_migration_owner_mismatch_is_rejected(self) -> None:
        data = self.fixture.manifest()
        data["migrations"][0]["module"] = "LibraryStorage"
        self.fixture.write_manifest(data)

        self.assert_manifest_error("owner_role does not match its module")

    def test_forbidden_sql_is_rejected_even_with_matching_hash(self) -> None:
        self.fixture.rewrite_sql_and_hash(
            "0002_module_privilege_projection.sql",
            "DROP DATABASE assetlibrary;\n",
        )

        self.assert_manifest_error("forbidden DROP DATABASE")

    def test_identity_switching_sql_is_rejected_even_with_matching_hash(self) -> None:
        for statement, marker in (
            ("RESET ROLE;\n", "forbidden RESET ROLE"),
            (
                "SET SESSION AUTHORIZATION assetlibrary_database_migration_owner;\n",
                "forbidden SESSION AUTHORIZATION",
            ),
        ):
            with self.subTest(statement=statement):
                self.fixture.rewrite_sql_and_hash(
                    "0002_module_privilege_projection.sql",
                    statement,
                )
                self.assert_manifest_error(marker)

    def test_role_provisioning_cannot_enable_login_or_store_password(self) -> None:
        original = (self.fixture.root / "roles.sql").read_text(encoding="utf-8")
        self.fixture.rewrite_sql_and_hash(
            "roles.sql",
            original + "ALTER ROLE assetlibrary_database_auditor LOGIN PASSWORD 'unsafe';\n",
        )

        self.assert_manifest_error("must not create or enable LOGIN roles")
        self.assert_manifest_error("must not contain passwords")

    def test_role_provisioning_rejects_dangerous_administrative_sql(self) -> None:
        original = (self.fixture.root / "roles.sql").read_text(encoding="utf-8")
        self.fixture.rewrite_sql_and_hash(
            "roles.sql",
            original + "DROP DATABASE assetlibrary;\n",
        )

        self.assert_manifest_error("role provisioning SQL contains forbidden DROP DATABASE")

    def test_role_provisioning_requires_explicit_cluster_change_flag(self) -> None:
        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "allow-cluster-role-changes"):
            MIGRATIONS.provision_roles(None, MIGRATIONS.load_manifest(), explicit=False)

    def test_non_matching_database_confirmation_fails_before_tool_lookup(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        connection = MIGRATIONS.Connection(
            host="127.0.0.1",
            port=5432,
            username="test",
            database="intended",
            confirm_database="different",
            postgres_bin=Path(self.fixture.temp.name) / "missing",
        )

        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "exactly match"):
            MIGRATIONS.PostgresTools(manifest, connection)

    def test_filesystem_root_cannot_be_a_backup_directory(self) -> None:
        root = Path(Path.cwd().anchor)

        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "filesystem root"):
            MIGRATIONS.safe_backup_directory(root)

    def test_backup_failure_leaves_no_published_artifact(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        backup_directory = Path(self.fixture.temp.name) / "backups"
        tools = SimpleNamespace(
            executables={"pg_dump": "pg_dump", "pg_restore": "pg_restore"},
            executable=lambda name: name,
            connection=SimpleNamespace(database="assetlibrary_test"),
            connection_args=lambda: [],
            run=mock.Mock(side_effect=MIGRATIONS.MigrationError("injected pg_dump failure")),
        )

        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "injected pg_dump failure"):
            MIGRATIONS.create_backup(tools, manifest, backup_directory, 160015, [])

        self.assertEqual(list(backup_directory.iterdir()), [])

    def test_client_major_mismatch_is_rejected_before_database_access(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        tools = MIGRATIONS.PostgresTools.__new__(MIGRATIONS.PostgresTools)
        tools.manifest = manifest
        tools.connection = MIGRATIONS.Connection(
            host="127.0.0.1",
            port=5432,
            username="test",
            database="assetlibrary_test",
            confirm_database="assetlibrary_test",
            postgres_bin=None,
        )
        tools.executables = {
            "psql": "psql",
            "pg_dump": "pg_dump",
            "pg_restore": "pg_restore",
        }
        tools.environment = {}
        completed = subprocess.CompletedProcess(
            ["psql", "--version"], 0, "psql (PostgreSQL) 17.0\n", ""
        )

        with mock.patch.object(tools, "run", return_value=completed):
            with self.assertRaisesRegex(MIGRATIONS.MigrationError, "psql major version must be 16"):
                tools.verify_versions(require_backup_tools=False)

    def test_psql_only_commands_do_not_require_backup_tools(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        postgres_bin = Path(self.fixture.temp.name) / "postgres-bin"
        postgres_bin.mkdir()
        suffix = ".exe" if sys.platform == "win32" else ""
        (postgres_bin / f"psql{suffix}").write_bytes(b"")
        connection = MIGRATIONS.Connection(
            host="127.0.0.1",
            port=5432,
            username="test",
            database="assetlibrary_test",
            confirm_database="assetlibrary_test",
            postgres_bin=postgres_bin,
        )

        tools = MIGRATIONS.PostgresTools(manifest, connection)

        self.assertEqual(set(tools.executables), {"psql"})

    def test_role_membership_contract_keeps_login_boundaries_non_inheriting(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        memberships = MIGRATIONS.expected_role_memberships(manifest)

        self.assertEqual(len(memberships), 13)
        self.assertIn(
            (
                manifest.migration_owner,
                manifest.migration_executor,
                "f",
                "t",
                "f",
            ),
            memberships,
        )
        self.assertTrue(
            all(
                membership[2:] == ("t", "t", "f")
                for membership in memberships
                if membership[1] == manifest.migration_owner
            )
        )

    def test_backup_metadata_rejects_path_traversal_and_hash_drift(self) -> None:
        metadata_path = Path(self.fixture.temp.name) / "backup.json"
        metadata_path.write_text(
            json.dumps(
                {
                    "format_version": 1,
                    "archive_file": "../escape.dump",
                    "archive_sha256": "0" * 64,
                }
            ),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "local filename"):
            MIGRATIONS.load_backup_metadata(metadata_path)

        archive = metadata_path.parent / "backup.dump"
        archive.write_bytes(b"archive")
        metadata_path.write_text(
            json.dumps(
                {
                    "format_version": 1,
                    "archive_file": archive.name,
                    "archive_sha256": "0" * 64,
                }
            ),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(MIGRATIONS.MigrationError, "does not match"):
            MIGRATIONS.load_backup_metadata(metadata_path)

    def test_backup_contract_requires_exact_manifest_and_role_hashes(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        metadata = {
            "format_version": 1,
            "backup_id": "assetlibrary-test-backup",
            "source_database": "assetlibrary_test",
            "server_version_num": 160015,
            "postgresql_major": 16,
            "migration_manifest_sha256": manifest.raw_checksum,
            "role_provisioning_sha256": manifest.role_checksum,
            "ledger": [],
        }
        self.assertEqual(MIGRATIONS.validate_backup_contract(metadata, manifest), [])

        for field_name in (
            "migration_manifest_sha256",
            "role_provisioning_sha256",
        ):
            with self.subTest(field_name=field_name):
                drifted = dict(metadata)
                drifted[field_name] = "0" * 64
                with self.assertRaisesRegex(MIGRATIONS.MigrationError, "differs"):
                    MIGRATIONS.validate_backup_contract(drifted, manifest)

    def test_production_migrations_do_not_promote_spike_business_tables(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        sql = "\n".join(item.path.read_text(encoding="utf-8") for item in manifest.migrations)

        for marker in (
            "library.asset",
            "library.permission_scope",
            "tasks.durable_task",
            "events.outbox",
            "CREATE EXTENSION",
        ):
            self.assertNotIn(marker, sql)
        self.assertIn("migration.module_ownership", sql)
        self.assertIn("migration.module_privilege_projection", sql)


if __name__ == "__main__":
    unittest.main()
