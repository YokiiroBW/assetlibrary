from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
AUDIT_COMMAND = "python -I -B scripts/validate_v0_1_alpha.py"
RELEASE_COMMAND = f"{AUDIT_COMMAND} --require-ready"

EXPECTED_CAPABILITIES = (
    "build-and-dependency-foundation",
    "assetlink-generated-sdks",
    "postgres-migration-foundation",
    "library-asset-scan-read-core",
    "task-health-outbox-core",
    "read-only-permission-gateway-web",
    "transfer-operation-sandbox",
    "single-core-native-packaging",
    "production-authentication",
    "production-database-composition",
    "host-business-api",
    "tls-and-secret-management",
    "metadata-tags-ratings-colors",
    "exact-dedup",
    "software-backup-restore",
    "production-file-operations",
    "scale-and-fault-release-evidence",
    "windows-service-runtime-evidence",
    "linux-systemd-runtime-evidence",
    "docker-runtime-evidence",
)


class V01AlphaFoundationTests(unittest.TestCase):
    def test_policy_is_complete_and_keeps_partial_or_missing_work_blocking(self) -> None:
        policy = json.loads(
            (ROOT / "eng/v0.1-alpha-readiness.json").read_text(encoding="utf-8")
        )

        self.assertEqual(policy["contract"], "v01-009/1")
        self.assertEqual(policy["declared_decision"], "blocked")
        self.assertEqual(policy["accepted_partial_inputs"], ["V01-008"])
        self.assertEqual(
            tuple(item["id"] for item in policy["capabilities"]),
            EXPECTED_CAPABILITIES,
        )
        v01_008 = next(item for item in policy["task_inputs"] if item["id"] == "V01-008")
        self.assertEqual(v01_008["registry_status"], "partial")
        self.assertEqual(v01_008["handoff_status"], "partial")
        self.assertEqual(v01_008["acceptance"], "adjudication_only")

        for item in policy["capabilities"]:
            self.assertTrue(item["owner"])
            self.assertTrue(item["evidence"])
            self.assertNotIn(item["status"], {"skipped", "partial"})
            if item["status"] == "passed":
                self.assertEqual(item["blockers"], [])
                self.assertIsNone(item["follow_up_task"])
            else:
                self.assertTrue(item["blockers"])
                self.assertTrue(item["follow_up_task"])

    def test_fast_merge_runs_read_only_audit_and_repository_verifier_includes_it(self) -> None:
        workflow = (ROOT / ".github/workflows/handoff-quality.yml").read_text(
            encoding="utf-8"
        )
        verifier = (ROOT / "scripts/verify_repository.py").read_text(encoding="utf-8")
        tiers = json.loads(
            (ROOT / "tests/architecture/ci-tiers.json").read_text(encoding="utf-8")
        )
        fast_merge = next(tier for tier in tiers["tiers"] if tier["id"] == "fast-merge")

        self.assertIn(f"run: {AUDIT_COMMAND}", workflow)
        self.assertIn("validate_v0_1_alpha.py", verifier)
        self.assertIn(AUDIT_COMMAND, fast_merge["commands"])
        self.assertNotIn(RELEASE_COMMAND, fast_merge["commands"])
        self.assertNotRegex(workflow.lower(), r"continue-on-error\s*:\s*true")

    def test_manual_scheduled_release_uses_aggregate_require_ready_gate(self) -> None:
        workflow = (ROOT / ".github/workflows/scheduled-quality.yml").read_text(
            encoding="utf-8"
        )
        release_section = workflow.split("  release-readiness:", 1)[1]
        tiers = json.loads(
            (ROOT / "tests/architecture/ci-tiers.json").read_text(encoding="utf-8")
        )
        scheduled = next(
            tier for tier in tiers["tiers"] if tier["id"] == "scheduled-release"
        )

        self.assertIn(f"run: {RELEASE_COMMAND}", release_section)
        self.assertIn(RELEASE_COMMAND, scheduled["commands"])
        self.assertNotIn(AUDIT_COMMAND, scheduled["commands"])
        self.assertNotIn("check_release_gates.py --target v0.1-release", release_section)
        self.assertNotRegex(release_section.lower(), r"continue-on-error\s*:\s*true")

    def test_release_ledger_and_host_policy_remain_fail_closed(self) -> None:
        ledger = json.loads(
            (ROOT / "tests/architecture/m0-gates.json").read_text(encoding="utf-8")
        )
        statuses = {gate["id"]: gate["status"] for gate in ledger["gates"]}
        for gate_id in (
            "M0-004-G1",
            "M0-004-G2",
            "M0-006-G1",
            "M0-006-G2",
            "M0-006-G3",
        ):
            self.assertEqual(statuses[gate_id], "open")

        server_policy = json.loads(
            (ROOT / "eng/server-release-policy.json").read_text(encoding="utf-8")
        )
        self.assertIs(server_policy["host"]["business_api_ready"], False)
        self.assertIs(server_policy["host"]["production_file_writes_enabled"], False)

    def test_readiness_document_separates_audit_task_and_release_outcomes(self) -> None:
        document = (ROOT / "docs/releases/V0.1_ALPHA_READINESS.md").read_text(
            encoding="utf-8"
        )

        for statement in (
            "完整 V0.1 Alpha 发布仍为 blocked",
            "component_only",
            "V01-008 继续为 partial",
            "READ_ONLY_TRIAL.md",
            "--require-ready",
            "返回 3",
            "返回 2",
            "外部环境门禁",
        ):
            self.assertIn(statement, document)


if __name__ == "__main__":
    unittest.main()
