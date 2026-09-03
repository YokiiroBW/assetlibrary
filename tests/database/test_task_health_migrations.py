from __future__ import annotations

import importlib.util
import re
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PRODUCTION = ROOT / "database/migrations/production"
TOOL_PATH = PRODUCTION / "migration_tool.py"
MIGRATION_PATH = PRODUCTION / "0006_task_health_core.sql"
SPEC = importlib.util.spec_from_file_location("assetlibrary_task_health_migration_tool", TOOL_PATH)
assert SPEC and SPEC.loader
MIGRATIONS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MIGRATIONS
SPEC.loader.exec_module(MIGRATIONS)


class TaskHealthMigrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.sql = MIGRATION_PATH.read_text(encoding="utf-8")
        cls.lower_sql = cls.sql.lower()

    def test_manifest_assigns_the_contiguous_migration_to_task_health(self) -> None:
        manifest = MIGRATIONS.load_manifest()
        migration = manifest.migrations[-1]

        self.assertEqual(migration.version, 6)
        self.assertEqual(migration.module, "TaskHealth")
        self.assertEqual(migration.owner_role, "assetlibrary_task_health_owner")
        self.assertEqual(migration.path, MIGRATION_PATH)

    def test_objects_and_writes_stay_inside_the_owned_schema(self) -> None:
        governed = {item[1] for item in MIGRATIONS.EXPECTED_MODULES}
        references = set(re.findall(r"\b([a-z][a-z0-9_]*)\.[a-z][a-z0-9_]*\b", self.lower_sql))

        self.assertEqual(references & governed, {"task_health"})
        self.assertNotIn("references ", self.lower_sql)
        self.assertNotIn("create extension", self.lower_sql)
        self.assertNotIn("delete from", self.lower_sql)
        for forbidden in ("copy program", "lo_import", "pg_write_file", "pg_file_write"):
            self.assertNotIn(forbidden, self.lower_sql)

    def test_task_claim_and_reclaim_are_indexed_bounded_and_lock_skipping(self) -> None:
        self.assertIn("durable_task_claim_index", self.sql)
        self.assertIn("durable_task_expired_lease_index", self.sql)
        self.assertGreaterEqual(self.sql.count("FOR UPDATE SKIP LOCKED"), 4)
        self.assertGreaterEqual(self.sql.count("LIMIT requested_batch_size"), 4)
        self.assertGreaterEqual(self.sql.count("requested_batch_size IS NULL"), 4)
        self.assertIn("requested_batch_size NOT BETWEEN 1 AND 256", self.sql)
        self.assertIn("requested_batch_size NOT BETWEEN 1 AND 1024", self.sql)

    def test_every_lease_mutation_uses_the_complete_fence_and_expiry(self) -> None:
        for marker in (
            "lease_owner = requested_worker",
            "lease_token = requested_lease_token",
            "lease_generation = requested_lease_generation",
            "lease_owner = requested_publisher",
            "lease_until >= clock_timestamp()",
        ):
            self.assertIn(marker, self.sql)
        self.assertGreaterEqual(self.sql.count("lease_until >= clock_timestamp()"), 4)
        self.assertEqual(self.sql.count("lease_generation = requested_lease_generation"), 4)
        self.assertEqual(self.sql.count("lease_generation = claimed.lease_generation + 1"), 2)
        self.assertIn("gen_random_uuid()", self.sql)

    def test_task_state_machine_covers_idempotency_cancellation_retry_and_expiry(self) -> None:
        self.assertIn("UNIQUE", self.sql)
        self.assertIn("idempotency key is already bound to a different task request", self.sql)
        self.assertIn("cancellation_requested_at", self.sql)
        self.assertIn("requested_outcome = 'retryable_failure'", self.sql)
        self.assertIn("'lease_expired'", self.sql)
        self.assertIn("requested_retry_delay_seconds IS NULL", self.sql)
        self.assertIn("attempts < candidate.max_attempts", self.sql)

    def test_outbox_is_durable_at_least_once_with_stable_identity_and_dead_lettering(self) -> None:
        self.assertRegex(self.lower_sql, r"create table task_health\.outbox_event\s*\(\s*event_id uuid primary key")
        self.assertIn("event ID is already bound to a different outbox event", self.sql)
        self.assertIn("outbox_event_claim_index", self.sql)
        self.assertIn("'dead_lettered'::task_health.outbox_state", self.sql)
        self.assertIn("published_at", self.sql)
        self.assertNotIn("exactly-once", self.lower_sql)

    def test_payloads_and_attempts_have_hard_storage_limits(self) -> None:
        self.assertGreaterEqual(self.sql.count("octet_length(payload::text) <= 262144"), 2)
        self.assertGreaterEqual(self.sql.count("jsonb_typeof(payload) = 'object'"), 2)
        self.assertGreaterEqual(self.sql.count("BETWEEN 1 AND 100"), 2)
        self.assertIn("requested_retry_delay_seconds NOT BETWEEN 0 AND 86400", self.sql)
        self.assertGreaterEqual(
            self.sql.count("<> '00000000-0000-0000-0000-000000000000'::uuid"),
            5,
        )

    def test_health_scope_and_reason_invariants_are_database_enforced(self) -> None:
        self.assertIn("CREATE TABLE task_health.health_status", self.sql)
        self.assertIn("scope_kind = 'system' AND scope_id IS NULL", self.sql)
        self.assertIn("scope_kind IN ('library', 'asset')", self.sql)
        self.assertIn("state = 'normal' AND reason_code IS NULL", self.sql)
        self.assertIn("EXCLUDED.observed_at >= health_status.observed_at", self.sql)
        self.assertIn("GREATEST(health_status.updated_at, EXCLUDED.updated_at)", self.sql)

    def test_runtime_can_only_read_tables_and_execute_fenced_functions(self) -> None:
        function_count = len(re.findall(r"^CREATE FUNCTION task_health\.", self.sql, re.MULTILINE))

        self.assertEqual(function_count, 12)
        self.assertEqual(self.sql.count("SECURITY DEFINER"), function_count)
        self.assertEqual(
            self.sql.count("SET search_path = pg_catalog, task_health"),
            function_count,
        )
        self.assertIn(
            "REVOKE ALL ON ALL TABLES IN SCHEMA task_health FROM assetlibrary_task_health_runtime",
            self.sql,
        )
        self.assertIn(
            "GRANT SELECT ON ALL TABLES IN SCHEMA task_health TO assetlibrary_task_health_runtime",
            self.sql,
        )
        self.assertIn("REVOKE ALL ON ALL FUNCTIONS IN SCHEMA task_health FROM PUBLIC", self.sql)
        self.assertIn(
            "GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA task_health TO assetlibrary_task_health_runtime",
            self.sql,
        )


if __name__ == "__main__":
    unittest.main()
