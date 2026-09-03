from __future__ import annotations

import importlib.util
import re
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PRODUCTION = ROOT / "database/migrations/production"
TOOL_PATH = PRODUCTION / "migration_tool.py"
SPEC = importlib.util.spec_from_file_location("assetlibrary_read_core_migration_tool", TOOL_PATH)
assert SPEC and SPEC.loader
MIGRATIONS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MIGRATIONS
SPEC.loader.exec_module(MIGRATIONS)


class ReadCoreMigrationTests(unittest.TestCase):
    def test_read_core_migrations_are_contiguous_and_module_owned(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        read_core = manifest.migrations[2:]

        self.assertEqual([item.version for item in read_core], [3, 4, 5])
        self.assertEqual(
            [(item.module, item.owner_role) for item in read_core],
            [
                ("LibraryStorage", "assetlibrary_library_storage_owner"),
                ("AssetIdentity", "assetlibrary_asset_identity_owner"),
                ("ScanReconciliation", "assetlibrary_scan_reconciliation_owner"),
            ],
        )

    def test_each_migration_creates_objects_only_in_its_owned_schema(self) -> None:
        owned = {
            "0003_library_storage_core.sql": "library_storage",
            "0004_asset_identity_core.sql": "asset_identity",
            "0005_scan_reconciliation_core.sql": "scan_reconciliation",
        }
        governed = set(owned.values()) | {"migration", "task_health"}

        for filename, expected_schema in owned.items():
            sql = (PRODUCTION / filename).read_text(encoding="utf-8").lower()
            referenced = set(re.findall(r"\b([a-z][a-z0-9_]*)\.[a-z][a-z0-9_]*\b", sql))
            self.assertIn(expected_schema, referenced, filename)
            self.assertEqual(referenced & governed, {expected_schema}, filename)

    def test_first_scan_schema_is_bounded_and_fail_closed(self) -> None:
        library_sql = (PRODUCTION / "0003_library_storage_core.sql").read_text(encoding="utf-8")
        identity_sql = (PRODUCTION / "0004_asset_identity_core.sql").read_text(encoding="utf-8")
        scan_sql = (PRODUCTION / "0005_scan_reconciliation_core.sql").read_text(encoding="utf-8")

        self.assertIn("pg_advisory_xact_lock", library_sql)
        self.assertIn("library root overlaps an existing root", library_sql)
        self.assertIn("scan_observation_stage", identity_sql)
        self.assertIn("library_index_snapshot", identity_sql)
        self.assertIn("initial scan cannot replace an existing asset index", identity_sql)
        self.assertIn("abort_initial_scan", identity_sql)
        self.assertIn("WHERE content_hash IS NOT NULL", identity_sql)
        self.assertIn("WHERE status = 'running'", scan_sql)
        self.assertIn("committed_entries <= observed_entries", scan_sql)

    def test_initial_migrations_contain_no_physical_file_write_vocabulary(self) -> None:
        sql = "\n".join(
            (PRODUCTION / filename).read_text(encoding="utf-8").lower()
            for filename in (
                "0003_library_storage_core.sql",
                "0004_asset_identity_core.sql",
                "0005_scan_reconciliation_core.sql",
            )
        )

        for forbidden in ("copy program", "lo_import", "pg_write_file", "pg_file_write"):
            self.assertNotIn(forbidden, sql)


if __name__ == "__main__":
    unittest.main()
