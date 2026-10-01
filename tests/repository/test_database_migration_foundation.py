from __future__ import annotations

import hashlib
import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class DatabaseMigrationFoundationRepositoryTests(unittest.TestCase):
    def test_m0_postgres_spike_inputs_remain_frozen_lf_or_crlf(self) -> None:
        # Retain the original Windows bytes and the published d7b43ec Git LF bytes.
        # Only these two exact serializations are accepted; content is not normalized.
        expected = {
            "database/migrations/001_core.sql": (
                "cb6f58bc839748167b31ce67ecf31002b4c9008cd1310a986a67edbec14518c7",
                "bdec272b3bbb5ed965cb472fb31914f0794db2b9d5cdf423dbaf8b706a2501f7"),
            "database/migrations/002_ledger.sql": (
                "aa1b037efe268891dced12431a21456e8cd51bcfafe3b0abc7d1c8dbc5acced4",
                "0b18f63ddf48344e7b10e4a59bc1f36d7ba9587822ea73cc66518174716adca1"),
            "tests/spikes/postgres/migration_runner.py": (
                "5b3c731217c90d0ddfe081a8ed9a7922fa45a36bda90d155e172751e380659af",
                "2d5b1bffb00cb363f24890cd9685afe757dfc3a8446a94a046faf49ec9c99b85"),
            "tests/spikes/postgres/test_postgres_spike.py": (
                "3ff51d6976349570045e82bb76e0972e51587c3ded9c1c58b24ba810f86bfd5a",
                "492c9bc7ba6a557886b3f2a94aefa0d64c5e9df7809c2c97bf1ee1ad59a905a3"),
        }

        for relative, checksums in expected.items():
            actual = hashlib.sha256((ROOT / relative).read_bytes()).hexdigest()
            self.assertIn(actual, checksums, relative)

    def test_production_manifest_is_the_only_documented_entry_point(self) -> None:
        readme = (ROOT / "database/migrations/README.md").read_text(encoding="utf-8")
        manifest = json.loads(
            (ROOT / "database/migrations/production/manifest.json").read_text(encoding="utf-8")
        )

        self.assertIn("only production migration entry point", readme)
        self.assertNotIn("001_core.sql", {item["path"] for item in manifest["migrations"]})
        self.assertNotIn("002_ledger.sql", {item["path"] for item in manifest["migrations"]})

        attributes = (ROOT / ".gitattributes").read_text(encoding="utf-8")
        self.assertIn("database/migrations/production/*.sql text eol=lf", attributes)
        self.assertIn(
            "database/migrations/production/manifest.json text eol=lf", attributes
        )

    def test_no_database_password_or_production_connection_is_committed(self) -> None:
        production = ROOT / "database/migrations/production"
        text = "\n".join(
            path.read_text(encoding="utf-8")
            for path in production.iterdir()
            if path.suffix in {".py", ".sql", ".json"}
        )

        self.assertNotIn("--password", text)
        self.assertNotRegex(text, r"postgres(?:ql)?://[^\s]+:[^\s]+@")
        self.assertNotRegex(text, r"(?i)password\s*=\s*['\"][^'\"]+['\"]")

    def test_fast_merge_runs_exact_postgresql_integration_job(self) -> None:
        contract = json.loads(
            (ROOT / "tests/architecture/ci-tiers.json").read_text(encoding="utf-8")
        )
        tier = next(item for item in contract["tiers"] if item["id"] == "fast-merge")
        self.assertEqual(tier["runners"], ["ubuntu-latest", "windows-latest"])
        self.assertIn(
            "python -B -m unittest discover -s tests/database -p test_*.py -v",
            tier["commands"],
        )

        workflow = (ROOT / ".github/workflows/handoff-quality.yml").read_text(
            encoding="utf-8"
        )
        for marker in (
            "fast-merge-database-postgresql-16.15",
            "image: postgres:16.15-bookworm",
            "ASSETLIBRARY_TEST_POSTGRES_REQUIRED: '1'",
            "ASSETLIBRARY_TEST_POSTGRES_EXTERNAL: '1'",
            "ASSETLIBRARY_TEST_RUNTIME: ${{ github.workspace }}/.runtime/sandbox-storage/V01-006",
            "python -B -m unittest discover -s tests/database -p test_*.py -v",
        ):
            self.assertIn(marker, workflow)
        self.assertIsNone(
            re.search(r"continue-on-error\s*:\s*true", workflow, flags=re.IGNORECASE)
        )

    def test_repository_verification_runs_connection_free_database_gates(self) -> None:
        script = (ROOT / "scripts/verify_repository.py").read_text(encoding="utf-8")

        self.assertIn("database/migrations/production/migration_tool.py", script)
        self.assertIn("test_migration_manifest.py", script)
        self.assertNotIn("test_migration_integration.py", script)


if __name__ == "__main__":
    unittest.main()
