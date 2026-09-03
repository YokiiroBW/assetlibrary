from __future__ import annotations

import importlib.util
import re
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PRODUCTION = ROOT / "database/migrations/production"
TOOL_PATH = PRODUCTION / "migration_tool.py"
SPEC = importlib.util.spec_from_file_location("assetlibrary_web_gateway_migration_tool", TOOL_PATH)
assert SPEC and SPEC.loader
MIGRATIONS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MIGRATIONS
SPEC.loader.exec_module(MIGRATIONS)


class WebGatewayMigrationTests(unittest.TestCase):
    def test_web_gateway_migrations_are_contiguous_and_module_owned(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        web_gateway = manifest.migrations[6:9]

        self.assertEqual([item.version for item in web_gateway], [7, 8, 9])
        self.assertEqual(
            [(item.module, item.owner_role) for item in web_gateway],
            [
                ("LibraryStorage", "assetlibrary_library_storage_owner"),
                ("AssetIdentity", "assetlibrary_asset_identity_owner"),
                ("GatewayAuth", "assetlibrary_gateway_auth_owner"),
            ],
        )

    def test_runtime_entry_points_are_permission_filtered_and_bounded(self) -> None:
        sql = (PRODUCTION / "0009_gateway_auth_read_api.sql").read_text(encoding="utf-8")

        self.assertEqual(sql.count("SECURITY DEFINER"), 4)
        self.assertEqual(sql.count("requested_limit NOT BETWEEN 1 AND 101"), 3)
        self.assertGreaterEqual(sql.count("principal.disabled_at IS NULL"), 4)
        self.assertGreaterEqual(sql.count("permission.access_level IS NOT NULL"), 4)
        self.assertIn("REVOKE ALL ON ALL FUNCTIONS IN SCHEMA gateway_auth FROM PUBLIC", sql)
        self.assertNotRegex(sql.lower(), r"\boffset\b")

    def test_web_gateway_projections_exclude_sensitive_and_historical_fields(self) -> None:
        identity_sql = (PRODUCTION / "0008_asset_identity_read_projection.sql").read_text(
            encoding="utf-8"
        )
        projection = identity_sql.split("CREATE VIEW asset_identity.entry_read_projection", 1)[1]
        projection = projection.split("GRANT USAGE", 1)[0].lower()

        self.assertIn("where entry.state = 'present'", projection)
        for forbidden in ("content_hash", "last_seen_scan_id", "first_seen_at", "missing_since"):
            self.assertNotIn(forbidden, projection)

    def test_browse_and_search_use_keyset_and_indexed_filters(self) -> None:
        identity_sql = (PRODUCTION / "0008_asset_identity_read_projection.sql").read_text(
            encoding="utf-8"
        )
        gateway_sql = (PRODUCTION / "0009_gateway_auth_read_api.sql").read_text(
            encoding="utf-8"
        )

        query_sql = f"{identity_sql}\n{gateway_sql}"
        self.assertIn("filesystem_entry_browse_index", identity_sql)
        self.assertIn("filesystem_entry_path_search_index", identity_sql)
        self.assertGreaterEqual(len(re.findall(r"\)\s*>\s*\(", query_sql)), 3)
        self.assertIn("@@ plainto_tsquery", identity_sql)
        self.assertIn("asset_identity.browse_read_entries", gateway_sql)
        self.assertIn("asset_identity.search_read_entries", gateway_sql)

    def test_migrations_never_touch_physical_asset_files(self) -> None:
        sql = "\n".join(
            (PRODUCTION / filename).read_text(encoding="utf-8").lower()
            for filename in (
                "0007_library_storage_read_permissions.sql",
                "0008_asset_identity_read_projection.sql",
                "0009_gateway_auth_read_api.sql",
            )
        )

        for forbidden in ("copy program", "lo_import", "pg_write_file", "pg_file_write"):
            self.assertNotIn(forbidden, sql)


if __name__ == "__main__":
    unittest.main()
