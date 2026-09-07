from __future__ import annotations

import copy
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "validate_v0_1_alpha.py"
POLICY_PATH = ROOT / "eng" / "v0.1-alpha-readiness.json"


def load_validator():
    spec = importlib.util.spec_from_file_location("assetlibrary_v01_009_validator", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


VALIDATOR = load_validator()
BASE_POLICY = json.loads(POLICY_PATH.read_text(encoding="utf-8"))


def capability(policy: dict[str, object], capability_id: str) -> dict[str, object]:
    capabilities = policy["capabilities"]
    assert isinstance(capabilities, list)
    return next(item for item in capabilities if item["id"] == capability_id)


def task_input(policy: dict[str, object], task_id: str) -> dict[str, object]:
    inputs = policy["task_inputs"]
    assert isinstance(inputs, list)
    return next(item for item in inputs if item["id"] == task_id)


class AlphaReadinessTests(unittest.TestCase):
    def validate_mutation(self, mutation) -> tuple[list[str], dict[str, object]]:
        policy = copy.deepcopy(BASE_POLICY)
        mutation(policy)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "readiness.json"
            path.write_text(json.dumps(policy), encoding="utf-8")
            return VALIDATOR.validate_repository(ROOT, path)

    def test_repository_audit_passes_but_release_is_blocked(self) -> None:
        errors, report = VALIDATOR.validate_repository(ROOT)

        self.assertEqual(errors, [])
        self.assertEqual(report["audit_status"], "passed")
        self.assertEqual(report["decision"], "blocked")
        self.assertEqual(
            report["capability_counts"],
            {
                "blocked_missing_environment": 3,
                "blocked_missing_implementation": 3,
                "component_only": 6,
                "deferred_fail_closed": 2,
                "passed": 6,
            },
        )
        blocker_ids = {(item["kind"], item["id"]) for item in report["blockers"]}
        self.assertIn(("capability", "production-authentication"), blocker_ids)
        self.assertIn(("release_target", "v0.1-release"), blocker_ids)
        self.assertIn(("partial_input", "V01-008"), blocker_ids)

    def test_cli_distinguishes_audit_success_from_release_block(self) -> None:
        default = subprocess.run(
            [sys.executable, "-I", "-B", str(SCRIPT)],
            cwd=ROOT,
            text=True,
            capture_output=True,
            timeout=15,
            check=False,
        )
        required = subprocess.run(
            [sys.executable, "-I", "-B", str(SCRIPT), "--require-ready"],
            cwd=ROOT,
            text=True,
            capture_output=True,
            timeout=15,
            check=False,
        )

        self.assertEqual(default.returncode, 0, default.stderr)
        self.assertIn("ALPHA_INTEGRATION_AUDIT_OK decision=blocked", default.stdout)
        self.assertEqual(required.returncode, VALIDATOR.RELEASE_BLOCKED_EXIT, required.stderr)
        self.assertIn("capability:production-authentication", required.stdout)
        self.assertIn(
            "release_target:v0.1-release gates=M0-006-G1,M0-006-G2",
            required.stdout,
        )
        self.assertNotIn("capability:docker-runtime-evidence", required.stdout)
        self.assertNotIn("release_target:docker-release", required.stdout)
        self.assertEqual(default.stderr, "")
        self.assertEqual(required.stderr, "")

    def test_default_main_never_calls_report_writer(self) -> None:
        with (
            mock.patch.object(sys, "argv", [str(SCRIPT)]),
            mock.patch.object(VALIDATOR, "write_report") as writer,
            mock.patch("builtins.print"),
        ):
            result = VALIDATOR.main()

        self.assertEqual(result, 0)
        writer.assert_not_called()

    def test_required_capability_membership_and_status_are_fail_closed(self) -> None:
        errors, _ = self.validate_mutation(
            lambda policy: policy["capabilities"].pop()
        )
        self.assertIn("capabilities: required_order_or_membership_mismatch", errors)

        def forge_passed(policy: dict[str, object]) -> None:
            item = capability(policy, "production-authentication")
            item["status"] = "passed"
            item["blockers"] = []

        errors, _ = self.validate_mutation(forge_passed)
        self.assertIn("capabilities.production-authentication: status_mismatch", errors)

        def use_skipped(policy: dict[str, object]) -> None:
            capability(policy, "docker-runtime-evidence")["status"] = "skipped"

        errors, _ = self.validate_mutation(use_skipped)
        self.assertIn("capabilities.docker-runtime-evidence: invalid_status", errors)

    def test_trial_evidence_cannot_satisfy_full_release_or_be_omitted(self) -> None:
        for capability_id in (
            "production-authentication",
            "production-database-composition",
            "host-business-api",
            "tls-and-secret-management",
        ):
            with self.subTest(capability=capability_id):
                item = capability(BASE_POLICY, capability_id)
                self.assertEqual(item["status"], "component_only")
                self.assertEqual(item["source_task"], "V01-015")
                errors, _ = self.validate_mutation(
                    lambda policy: capability(policy, capability_id).update(
                        status="passed", blockers=[]
                    )
                )
                self.assertIn(f"capabilities.{capability_id}: status_mismatch", errors)

        errors, _ = self.validate_mutation(
            lambda policy: policy["task_inputs"].remove(task_input(policy, "V01-020"))
        )
        self.assertIn("task_inputs: required_order_or_membership_mismatch", errors)

    def test_partial_input_cannot_be_promoted_by_policy_or_registry_drift(self) -> None:
        def promote_input(policy: dict[str, object]) -> None:
            item = task_input(policy, "V01-008")
            item["registry_status"] = "completed"
            item["handoff_status"] = "ready_for_review"

        errors, _ = self.validate_mutation(promote_input)
        self.assertIn("task_inputs.V01-008: registry_status_mismatch", errors)
        self.assertIn("task_inputs.V01-008: handoff_status_mismatch", errors)

        original_load = VALIDATOR.load_json

        def drifted_registry(path: Path, label: str, errors: list[str]):
            value = original_load(path, label, errors)
            if label == "task_registry" and value is not None:
                value = copy.deepcopy(value)
                item = next(task for task in value["tasks"] if task["id"] == "V01-008")
                item["status"] = "completed"
            return value

        with mock.patch.object(VALIDATOR, "load_json", side_effect=drifted_registry):
            errors, _ = VALIDATOR.validate_repository(ROOT)
        self.assertIn("task_inputs.V01-008: registry_status_drift", errors)

    def test_evidence_and_result_paths_are_exact_and_contained(self) -> None:
        def substitute_evidence(policy: dict[str, object]) -> None:
            capability(policy, "build-and-dependency-foundation")["evidence"] = ["README.md"]

        errors, _ = self.validate_mutation(substitute_evidence)
        self.assertIn(
            "capabilities.build-and-dependency-foundation: evidence_set_mismatch",
            errors,
        )

        def escape_evidence(policy: dict[str, object]) -> None:
            capability(policy, "exact-dedup")["evidence"] = ["../outside.json"]

        errors, _ = self.validate_mutation(escape_evidence)
        self.assertIn("capabilities.exact-dedup.evidence[0]: invalid_repository_relative_path", errors)

        def escape_result(policy: dict[str, object]) -> None:
            task_input(policy, "V01-008")["result"] = "../V01-008/result.json"

        errors, _ = self.validate_mutation(escape_result)
        self.assertIn("task_inputs.V01-008: result_mismatch", errors)
        self.assertIn("task_inputs.V01-008.result: invalid_repository_relative_path", errors)

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            evidence_directory = root / "evidence"
            evidence_directory.mkdir()
            (evidence_directory / "result.json").write_text("{}", encoding="utf-8")
            path_errors: list[str] = []
            with mock.patch.object(
                VALIDATOR,
                "is_reparse_point",
                side_effect=lambda path: path == evidence_directory,
            ):
                result = VALIDATOR.repository_file(
                    root,
                    "evidence/result.json",
                    "evidence",
                    path_errors,
                )
            self.assertIsNone(result)
            self.assertEqual(path_errors, ["evidence: reparse_path_rejected"])

    def test_policy_shape_owner_follow_up_blocker_and_decision_are_enforced(self) -> None:
        errors, _ = self.validate_mutation(lambda policy: policy.update({"extra": True}))
        self.assertIn("alpha_policy: field_set_mismatch", errors)

        def remove_owner(policy: dict[str, object]) -> None:
            capability(policy, "production-authentication").pop("owner")

        errors, _ = self.validate_mutation(remove_owner)
        self.assertIn("capabilities.production-authentication: field_set_mismatch", errors)
        self.assertIn("capabilities.production-authentication: owner_mismatch", errors)

        def remove_follow_up(policy: dict[str, object]) -> None:
            capability(policy, "production-authentication")["follow_up_task"] = None

        errors, _ = self.validate_mutation(remove_follow_up)
        self.assertIn("capabilities.production-authentication: follow_up_task_mismatch", errors)
        self.assertIn("capabilities.production-authentication: follow_up_task_required", errors)

        def remove_blocker(policy: dict[str, object]) -> None:
            capability(policy, "production-authentication")["blockers"] = []

        errors, _ = self.validate_mutation(remove_blocker)
        self.assertIn("capabilities.production-authentication: blocker_text_required", errors)

        errors, _ = self.validate_mutation(
            lambda policy: policy.update({"declared_decision": "ready"})
        )
        self.assertIn("alpha_policy: declared_decision_does_not_match_computed_decision", errors)

        with tempfile.TemporaryDirectory() as directory:
            duplicate = Path(directory) / "duplicate.json"
            duplicate.write_text('{"version": 1, "version": 2}', encoding="utf-8")
            duplicate_errors: list[str] = []
            self.assertIsNone(
                VALIDATOR.load_json(duplicate, "duplicate", duplicate_errors)
            )
            self.assertEqual(duplicate_errors, ["duplicate: unreadable_or_invalid_json"])

    def test_host_policy_cannot_claim_business_or_write_readiness(self) -> None:
        original_load = VALIDATOR.load_json

        def forged_host(path: Path, label: str, errors: list[str]):
            value = original_load(path, label, errors)
            if label == "server_release_policy" and value is not None:
                value = copy.deepcopy(value)
                value["host"]["business_api_ready"] = True
                value["host"]["production_file_writes_enabled"] = True
            return value

        with mock.patch.object(VALIDATOR, "load_json", side_effect=forged_host):
            errors, _ = VALIDATOR.validate_repository(ROOT)

        self.assertIn(
            "server_release_policy.host: business_api_ready_must_remain_false",
            errors,
        )
        self.assertIn(
            "server_release_policy.host: production_file_writes_enabled_must_remain_false",
            errors,
        )

    def test_invalid_ledger_timeout_and_unexpected_gate_output_are_audit_errors(self) -> None:
        invalid = SimpleNamespace(
            returncode=VALIDATOR.RELEASE_BLOCKED_EXIT,
            stdout="RELEASE_GATE_BLOCKED: v0.1-release\n- LEDGER_INVALID: invalid\n",
            stderr="",
        )
        with mock.patch.object(VALIDATOR.subprocess, "run", return_value=invalid):
            outcome, error = VALIDATOR.run_release_gate(ROOT, "v0.1-release")
        self.assertIsNone(outcome)
        self.assertEqual(error, "invalid_gate_ledger")

        with mock.patch.object(
            VALIDATOR.subprocess,
            "run",
            side_effect=subprocess.TimeoutExpired(["python"], 5),
        ):
            outcome, error = VALIDATOR.run_release_gate(ROOT, "v0.1-release")
        self.assertIsNone(outcome)
        self.assertEqual(error, "execution_failed_or_timed_out")

        unexpected = SimpleNamespace(returncode=0, stdout="READY\n", stderr="")
        with mock.patch.object(VALIDATOR.subprocess, "run", return_value=unexpected):
            outcome, error = VALIDATOR.run_release_gate(ROOT, "v0.1-release")
        self.assertIsNone(outcome)
        self.assertEqual(error, "unexpected_exit_or_output")

    def test_gate_execution_failure_keeps_decision_blocked_and_fails_audit(self) -> None:
        with mock.patch.object(
            VALIDATOR,
            "run_release_gate",
            return_value=(None, "execution_failed_or_timed_out"),
        ):
            errors, report = VALIDATOR.validate_repository(ROOT)

        self.assertTrue(errors)
        self.assertEqual(report["audit_status"], "failed")
        self.assertEqual(report["decision"], "blocked")

    def test_report_output_is_deterministic_sanitized_and_task_owned(self) -> None:
        report = {
            "contract": "v01-009/1",
            "audit_status": "passed",
            "decision": "blocked",
            "capability_counts": {"passed": 5},
            "blockers": [{"kind": "partial_input", "id": "V01-008"}],
        }
        output = ".runtime/sandbox-storage/V01-009/reports/alpha.json"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            VALIDATOR.write_report(root, output, report)
            target = root / Path(output)
            first = target.read_bytes()
            VALIDATOR.write_report(root, output, report)
            second = target.read_bytes()

            self.assertEqual(first, second)
            self.assertEqual(json.loads(first), report)
            self.assertEqual(
                (root / VALIDATOR.OUTPUT_ROOT / VALIDATOR.OUTPUT_MARKER).read_text(
                    encoding="utf-8"
                ),
                VALIDATOR.OUTPUT_MARKER_VALUE,
            )
            rendered = first.decode("utf-8")
            self.assertNotIn(str(ROOT), rendered)
            self.assertNotIn("ComputerName", rendered)
            self.assertNotIn("User", rendered)

    def test_output_rejects_outside_root_unc_device_parent_and_controls_without_writes(self) -> None:
        invalid_paths = (
            "report.json",
            ".runtime/sandbox-storage/V01-009",
            "//server/share/report.json",
            r"\\server\share\report.json",
            "C:/Users/example/report.json",
            ".runtime/sandbox-storage/V01-009/../V01-008/report.json",
            ".runtime/sandbox-storage/V01-009/report\n.json",
            ".runtime/sandbox-storage/V01-009/NUL.json",
            ".runtime/sandbox-storage/V01-009/COM1/output.json",
            ".runtime/sandbox-storage/V01-009/report.json:stream",
            ".runtime/sandbox-storage/V01-009/trailing./report.json",
        )
        for output in invalid_paths:
            with self.subTest(output=repr(output)), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                with self.assertRaises(VALIDATOR.OutputBoundaryError):
                    VALIDATOR.prepare_output_path(root, output)
                self.assertFalse((root / ".runtime").exists())

    def test_output_rejects_reparse_parent_before_creating_marker_or_report(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            task_root = root / VALIDATOR.OUTPUT_ROOT
            redirect = task_root / "redirect"
            redirect.mkdir(parents=True)

            with mock.patch.object(
                VALIDATOR,
                "is_reparse_point",
                side_effect=lambda path: path == redirect,
            ):
                with self.assertRaises(VALIDATOR.OutputBoundaryError):
                    VALIDATOR.prepare_output_path(
                        root,
                        ".runtime/sandbox-storage/V01-009/redirect/report.json",
                    )

            self.assertFalse((task_root / VALIDATOR.OUTPUT_MARKER).exists())
            self.assertFalse((redirect / "report.json").exists())


if __name__ == "__main__":
    unittest.main()
