#!/usr/bin/env python3
"""Audit the V0.1 Alpha evidence set and fail closed for release readiness."""
from __future__ import annotations

import argparse
import json
import os
import stat
import subprocess
import sys
import tempfile
import unicodedata
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_POLICY = ROOT / "eng" / "v0.1-alpha-readiness.json"
REGISTRY = ROOT / ".codex" / "task-registry.json"
SERVER_RELEASE_POLICY = ROOT / "eng" / "server-release-policy.json"
RELEASE_GATE_CHECKER = ROOT / "tests" / "architecture" / "check_release_gates.py"

AUDIT_ERROR_EXIT = 2
RELEASE_BLOCKED_EXIT = 3
GATE_TIMEOUT_SECONDS = 5
OUTPUT_ROOT = PurePosixPath(".runtime/sandbox-storage/V01-009")
OUTPUT_MARKER = ".assetlibrary-v01-009-output"
OUTPUT_MARKER_VALUE = "AssetLibrary/V01-009/alpha-readiness/v1\n"
WINDOWS_RESERVED_NAMES = {
    "CON",
    "PRN",
    "AUX",
    "NUL",
    "CONIN$",
    "CONOUT$",
    *(f"COM{index}" for index in range(1, 10)),
    *(f"LPT{index}" for index in range(1, 10)),
}

ALLOWED_CAPABILITY_STATUSES = {
    "passed",
    "component_only",
    "blocked_missing_implementation",
    "blocked_missing_environment",
    "deferred_fail_closed",
}

POLICY_FIELDS = {
    "version",
    "contract",
    "declared_decision",
    "accepted_partial_inputs",
    "output",
    "task_inputs",
    "release_targets",
    "capabilities",
}
TASK_INPUT_FIELDS = {
    "id",
    "registry_status",
    "handoff_status",
    "acceptance",
    "result",
}
CAPABILITY_FIELDS = {
    "id",
    "status",
    "source_task",
    "release_target",
    "owner",
    "follow_up_task",
    "evidence",
    "blockers",
}


@dataclass(frozen=True)
class TaskRule:
    registry_status: str
    handoff_status: str
    acceptance: str


@dataclass(frozen=True)
class CapabilityRule:
    status: str
    source_task: str | None
    release_target: str | None
    owner: str
    follow_up_task: str | None


@dataclass(frozen=True)
class GateOutcome:
    allowed: bool
    blocker_ids: tuple[str, ...]


TASK_RULES = {
    "V01-001": TaskRule("completed", "ready_for_review", "required"),
    "V01-002": TaskRule("completed", "ready_for_review", "required"),
    "V01-003": TaskRule("completed", "ready_for_review", "required"),
    "V01-004": TaskRule("completed", "ready_for_review", "required"),
    "V01-005": TaskRule("completed", "ready_for_review", "required"),
    "V01-006": TaskRule("completed", "ready_for_review", "required"),
    "V01-007": TaskRule("completed", "ready_for_review", "required"),
    "V01-008": TaskRule("partial", "partial", "adjudication_only"),
    "V01-015": TaskRule("completed", "ready_for_review", "required"),
    "V01-016": TaskRule("completed", "ready_for_review", "required"),
    "V01-017": TaskRule("completed", "ready_for_review", "required"),
    "V01-018": TaskRule("completed", "ready_for_review", "required"),
    "V01-019": TaskRule("completed", "ready_for_review", "required"),
    "V01-020": TaskRule("completed", "ready_for_review", "required"),
    "V01-021": TaskRule("completed", "ready_for_review", "required"),
}

RELEASE_TARGETS = (
    "v0.1-release",
    "windows-server-release",
    "linux-server-release",
    "docker-release",
    "large-file-release",
    "production-file-writes",
)

CAPABILITY_RULES = {
    "build-and-dependency-foundation": CapabilityRule(
        "passed", "V01-001", None, "build-foundation-owner", None
    ),
    "assetlink-generated-sdks": CapabilityRule(
        "passed", "V01-002", None, "sdk-generation-owner", None
    ),
    "postgres-migration-foundation": CapabilityRule(
        "passed", "V01-003", None, "database-migration-owner", None
    ),
    "library-asset-scan-read-core": CapabilityRule(
        "passed", "V01-004", None, "library-asset-scan-owner", None
    ),
    "task-health-outbox-core": CapabilityRule(
        "passed", "V01-005", None, "task-health-owner", None
    ),
    "read-only-permission-gateway-web": CapabilityRule(
        "component_only",
        "V01-006",
        None,
        "web-gateway-owner",
        "V01-AUTH-RUNTIME-INTEGRATION",
    ),
    "transfer-operation-sandbox": CapabilityRule(
        "deferred_fail_closed",
        "V01-007",
        "production-file-writes",
        "transfer-operation-owner",
        "V01-WRITE-DURABILITY-GATE",
    ),
    "single-core-native-packaging": CapabilityRule(
        "component_only",
        "V01-008",
        None,
        "server-packaging-owner",
        "V01-PLATFORM-RELEASE-GATES",
    ),
    "production-authentication": CapabilityRule(
        "component_only",
        "V01-015",
        None,
        "gateway-auth-owner",
        "V01-AUTH-RUNTIME",
    ),
    "production-database-composition": CapabilityRule(
        "component_only",
        "V01-015",
        None,
        "database-migration-owner",
        "V01-DATABASE-RUNTIME",
    ),
    "host-business-api": CapabilityRule(
        "component_only",
        "V01-015",
        None,
        "web-gateway-owner",
        "V01-READ-HOST-INTEGRATION",
    ),
    "tls-and-secret-management": CapabilityRule(
        "component_only",
        "V01-015",
        None,
        "server-packaging-owner",
        "V01-TLS-SECRETS",
    ),
    "metadata-tags-ratings-colors": CapabilityRule(
        "blocked_missing_implementation",
        None,
        None,
        "metadata-sidecar-owner",
        "V01-METADATA-CORE",
    ),
    "exact-dedup": CapabilityRule(
        "blocked_missing_implementation",
        None,
        None,
        "search-dedup-owner",
        "V01-DEDUP-CORE",
    ),
    "software-backup-restore": CapabilityRule(
        "blocked_missing_implementation",
        None,
        None,
        "backup-update-owner",
        "V01-BACKUP-RESTORE",
    ),
    "production-file-operations": CapabilityRule(
        "deferred_fail_closed",
        "V01-007",
        "production-file-writes",
        "operation-trash-owner",
        "V01-WRITE-DURABILITY-GATE",
    ),
    "scale-and-fault-release-evidence": CapabilityRule(
        "blocked_missing_environment",
        "V01-007",
        "large-file-release",
        "test-performance-owner",
        "V01-LARGE-FILE-GATE",
    ),
    "windows-service-runtime-evidence": CapabilityRule(
        "blocked_missing_environment",
        "V01-008",
        "windows-server-release",
        "server-packaging-owner",
        "V01-WINDOWS-SERVICE-GATE",
    ),
    "linux-systemd-runtime-evidence": CapabilityRule(
        "blocked_missing_environment",
        "V01-008",
        "linux-server-release",
        "server-packaging-owner",
        "V01-LINUX-NAMESPACE-GATE",
    ),
    "docker-runtime-evidence": CapabilityRule(
        "passed",
        "V01-021",
        "docker-release",
        "server-packaging-owner",
        None,
    ),
}

CAPABILITY_EVIDENCE = {
    "build-and-dependency-foundation": (
        ".codex/handoffs/V01-001/result.json",
        "global.json",
    ),
    "assetlink-generated-sdks": (
        ".codex/handoffs/V01-002/result.json",
        "contracts/assetlink/handshake.schema.json",
    ),
    "postgres-migration-foundation": (
        ".codex/handoffs/V01-003/result.json",
        "database/migrations/production/manifest.json",
    ),
    "library-asset-scan-read-core": (".codex/handoffs/V01-004/result.json",),
    "task-health-outbox-core": (".codex/handoffs/V01-005/result.json",),
    "read-only-permission-gateway-web": (".codex/handoffs/V01-006/result.json",),
    "transfer-operation-sandbox": (
        ".codex/handoffs/V01-007/result.json",
        "tests/architecture/m0-gates.json",
    ),
    "single-core-native-packaging": (
        ".codex/handoffs/V01-008/result.json",
        "eng/server-release-policy.json",
    ),
    "production-authentication": (
        ".codex/handoffs/V01-015/result.json",
        ".codex/handoffs/V01-016/result.json",
        ".codex/handoffs/V01-020/result.json",
        ".codex/handoffs/V01-021/result.json",
    ),
    "production-database-composition": (
        ".codex/handoffs/V01-015/result.json",
        ".codex/handoffs/V01-017/result.json",
        ".codex/handoffs/V01-020/result.json",
        ".codex/handoffs/V01-021/result.json",
    ),
    "host-business-api": (
        ".codex/handoffs/V01-015/result.json",
        ".codex/handoffs/V01-018/result.json",
        "eng/server-release-policy.json",
        ".codex/handoffs/V01-021/result.json",
    ),
    "tls-and-secret-management": (
        ".codex/handoffs/V01-015/result.json",
        ".codex/handoffs/V01-019/result.json",
        "docs/13_权限分享安全通知与WebDAV.md",
        ".codex/handoffs/V01-021/result.json",
    ),
    "metadata-tags-ratings-colors": ("docs/02_已确认需求基线.md",),
    "exact-dedup": ("docs/02_已确认需求基线.md",),
    "software-backup-restore": (
        "docs/15_性能任务健康容量备份与更新.md",
        ".codex/handoffs/V01-003/result.json",
    ),
    "production-file-operations": (
        ".codex/handoffs/V01-007/result.json",
        "tests/architecture/m0-gates.json",
    ),
    "scale-and-fault-release-evidence": (
        ".codex/handoffs/V01-007/result.json",
        "tests/architecture/m0-gates.json",
    ),
    "windows-service-runtime-evidence": (
        ".codex/handoffs/V01-008/result.json",
        "tests/architecture/m0-gates.json",
    ),
    "linux-systemd-runtime-evidence": (
        ".codex/handoffs/V01-008/result.json",
        "tests/architecture/m0-gates.json",
    ),
    "docker-runtime-evidence": (
        ".codex/handoffs/V01-021/result.json",
        ".codex/handoffs/V01-021/nas-deployment-evidence.json",
        "tests/architecture/m0-gates.json",
    ),
}


class OutputBoundaryError(ValueError):
    """Raised when a report target is outside the fixed task sandbox."""


class DuplicateJsonKeyError(ValueError):
    """Raised when a JSON object repeats a key and would otherwise be ambiguous."""


def unique_json_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise DuplicateJsonKeyError(key)
        value[key] = item
    return value


def contains_control_characters(value: str, *, allow_tab: bool = False) -> bool:
    """Return whether text contains ASCII or Unicode control characters."""
    return any(
        (
            unicodedata.category(character) in {"Cc", "Cf", "Cs", "Zl", "Zp"}
            and not (allow_tab and character == "\t")
        )
        for character in value
    )


def has_unsafe_windows_component(path: PurePosixPath) -> bool:
    for component in path.parts:
        if ":" in component or component.endswith((".", " ")):
            return True
        device_stem = component.split(".", 1)[0].upper()
        if device_stem in WINDOWS_RESERVED_NAMES:
            return True
    return False


def load_json(path: Path, label: str, errors: list[str]) -> dict[str, Any] | None:
    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=unique_json_object,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, DuplicateJsonKeyError):
        errors.append(f"{label}: unreadable_or_invalid_json")
        return None
    if not isinstance(value, dict):
        errors.append(f"{label}: root_must_be_object")
        return None
    return value


def is_reparse_point(path: Path) -> bool:
    try:
        metadata = path.lstat()
    except OSError:
        return False
    attributes = getattr(metadata, "st_file_attributes", 0)
    reparse_attribute = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return path.is_symlink() or bool(attributes & reparse_attribute)


def repository_file(root: Path, value: object, label: str, errors: list[str]) -> Path | None:
    if (
        not isinstance(value, str)
        or not value
        or "\\" in value
        or contains_control_characters(value)
    ):
        errors.append(f"{label}: invalid_repository_relative_path")
        return None
    relative = PurePosixPath(value)
    if (
        relative.is_absolute()
        or relative.as_posix() != value
        or ".." in relative.parts
        or has_unsafe_windows_component(relative)
    ):
        errors.append(f"{label}: invalid_repository_relative_path")
        return None

    current = root
    for part in relative.parts:
        current /= part
        if current.exists() and is_reparse_point(current):
            errors.append(f"{label}: reparse_path_rejected")
            return None
    try:
        resolved_root = root.resolve(strict=True)
        resolved = current.resolve(strict=True)
        resolved.relative_to(resolved_root)
    except (OSError, ValueError):
        errors.append(f"{label}: missing_or_outside_repository")
        return None
    if not resolved.is_file():
        errors.append(f"{label}: evidence_must_be_file")
        return None
    return resolved


def unique_objects(
    value: object,
    label: str,
    errors: list[str],
) -> tuple[list[dict[str, Any]], dict[str, dict[str, Any]]]:
    if not isinstance(value, list):
        errors.append(f"{label}: must_be_array")
        return [], {}
    objects: list[dict[str, Any]] = []
    by_id: dict[str, dict[str, Any]] = {}
    for index, item in enumerate(value):
        if not isinstance(item, dict):
            errors.append(f"{label}[{index}]: must_be_object")
            continue
        identifier = item.get("id")
        if (
            not isinstance(identifier, str)
            or not identifier
            or contains_control_characters(identifier)
        ):
            errors.append(f"{label}[{index}]: invalid_id")
            continue
        if identifier in by_id:
            errors.append(f"{label}: duplicate_id:{identifier}")
            continue
        objects.append(item)
        by_id[identifier] = item
    return objects, by_id


def validate_task_inputs(
    root: Path,
    policy: dict[str, Any],
    registry: dict[str, Any],
    errors: list[str],
) -> dict[str, dict[str, Any]]:
    task_items, task_inputs = unique_objects(policy.get("task_inputs"), "task_inputs", errors)
    if [item.get("id") for item in task_items] != list(TASK_RULES):
        errors.append("task_inputs: required_order_or_membership_mismatch")

    registry_items, registry_by_id = unique_objects(registry.get("tasks"), "registry.tasks", errors)
    if len(registry_items) != len(registry_by_id):
        errors.append("registry.tasks: duplicate_or_invalid_entries")

    for task_id, rule in TASK_RULES.items():
        item = task_inputs.get(task_id)
        if item is None:
            continue
        if set(item) != TASK_INPUT_FIELDS:
            errors.append(f"task_inputs.{task_id}: field_set_mismatch")
        expected_result = f".codex/handoffs/{task_id}/result.json"
        expected_fields = {
            "registry_status": rule.registry_status,
            "handoff_status": rule.handoff_status,
            "acceptance": rule.acceptance,
            "result": expected_result,
        }
        for field, expected in expected_fields.items():
            if item.get(field) != expected:
                errors.append(f"task_inputs.{task_id}: {field}_mismatch")

        registry_item = registry_by_id.get(task_id)
        if registry_item is None or registry_item.get("status") != rule.registry_status:
            errors.append(f"task_inputs.{task_id}: registry_status_drift")

        result_path = repository_file(root, item.get("result"), f"task_inputs.{task_id}.result", errors)
        if result_path is None:
            continue
        result = load_json(result_path, f"task_inputs.{task_id}.result", errors)
        if result is None:
            continue
        if result.get("task_id") != task_id:
            errors.append(f"task_inputs.{task_id}: handoff_task_id_mismatch")
        if result.get("status") != rule.handoff_status:
            errors.append(f"task_inputs.{task_id}: handoff_status_drift")

    v01_009 = registry_by_id.get("V01-009", {})
    if v01_009.get("module") != "release-integration-gate":
        errors.append("registry.V01-009: module_mismatch")
    if v01_009.get("status") not in {"ready", "in_progress", "completed"}:
        errors.append("registry.V01-009: invalid_lifecycle_status")
    if v01_009.get("accepted_partial_dependencies") != ["V01-008"]:
        errors.append("registry.V01-009: accepted_partial_dependencies_mismatch")
    if policy.get("accepted_partial_inputs") != ["V01-008"]:
        errors.append("accepted_partial_inputs: must_equal_V01-008")
    return registry_by_id


def run_release_gate(root: Path, target: str) -> tuple[GateOutcome | None, str | None]:
    checker_errors: list[str] = []
    checker = repository_file(
        root,
        RELEASE_GATE_CHECKER.relative_to(ROOT).as_posix(),
        "release_gate_checker",
        checker_errors,
    )
    if checker is None:
        return None, "checker_missing_or_untrusted"
    command = [
        sys.executable,
        "-I",
        "-B",
        str(checker),
        "--target",
        target,
    ]
    try:
        completed = subprocess.run(
            command,
            cwd=root,
            text=True,
            capture_output=True,
            timeout=GATE_TIMEOUT_SECONDS,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None, "execution_failed_or_timed_out"

    if completed.stderr.strip():
        return None, "unexpected_stderr"
    lines = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
    if completed.returncode == 0 and lines == [f"RELEASE_GATE_ALLOWED: {target}"]:
        return GateOutcome(True, ()), None
    if completed.returncode == RELEASE_BLOCKED_EXIT and lines[:1] == [f"RELEASE_GATE_BLOCKED: {target}"]:
        if len(lines) < 2 or any(
            not line.startswith("- ") or ":" not in line
            for line in lines[1:]
        ):
            return None, "malformed_blocker_output"
        blockers = tuple(
            line[2:].split(":", 1)[0]
            for line in lines[1:]
        )
        if (
            not blockers
            or len(blockers) != len(set(blockers))
            or any(not blocker or contains_control_characters(blocker) for blocker in blockers)
        ):
            return None, "blocked_without_gate_ids"
        if "LEDGER_INVALID" in blockers:
            return None, "invalid_gate_ledger"
        return GateOutcome(False, blockers), None
    return None, "unexpected_exit_or_output"


def validate_release_targets(
    root: Path,
    policy: dict[str, Any],
    errors: list[str],
) -> dict[str, GateOutcome]:
    targets = policy.get("release_targets")
    if targets != list(RELEASE_TARGETS):
        errors.append("release_targets: required_order_or_membership_mismatch")
        return {}
    ledger_path = repository_file(
        root,
        "tests/architecture/m0-gates.json",
        "release_gate_ledger.path",
        errors,
    )
    if ledger_path is None:
        return {}
    if load_json(ledger_path, "release_gate_ledger", errors) is None:
        return {}
    outcomes: dict[str, GateOutcome] = {}
    for target in RELEASE_TARGETS:
        outcome, error = run_release_gate(root, target)
        if error is not None or outcome is None:
            errors.append(f"release_targets.{target}: {error or 'unknown_gate_error'}")
            continue
        outcomes[target] = outcome
    return outcomes


def valid_text_list(value: object, allow_empty: bool) -> bool:
    return isinstance(value, list) and (allow_empty or bool(value)) and all(
        isinstance(item, str)
        and bool(item.strip())
        and len(item) <= 1000
        and not contains_control_characters(item, allow_tab=True)
        for item in value
    )


def validate_capabilities(
    root: Path,
    policy: dict[str, Any],
    registry_by_id: dict[str, dict[str, Any]],
    gate_outcomes: dict[str, GateOutcome],
    errors: list[str],
) -> tuple[list[str], dict[str, int]]:
    capability_items, capabilities = unique_objects(policy.get("capabilities"), "capabilities", errors)
    if [item.get("id") for item in capability_items] != list(CAPABILITY_RULES):
        errors.append("capabilities: required_order_or_membership_mismatch")

    blockers: list[str] = []
    counts = {status: 0 for status in sorted(ALLOWED_CAPABILITY_STATUSES)}
    for capability_id, rule in CAPABILITY_RULES.items():
        item = capabilities.get(capability_id)
        if item is None:
            continue
        if set(item) != CAPABILITY_FIELDS:
            errors.append(f"capabilities.{capability_id}: field_set_mismatch")
        expected_fields = {
            "status": rule.status,
            "source_task": rule.source_task,
            "release_target": rule.release_target,
            "owner": rule.owner,
            "follow_up_task": rule.follow_up_task,
        }
        for field, expected in expected_fields.items():
            if item.get(field) != expected:
                errors.append(f"capabilities.{capability_id}: {field}_mismatch")

        status_value = item.get("status")
        if status_value not in ALLOWED_CAPABILITY_STATUSES:
            errors.append(f"capabilities.{capability_id}: invalid_status")
        else:
            counts[status_value] += 1

        evidence = item.get("evidence")
        if evidence != list(CAPABILITY_EVIDENCE[capability_id]):
            errors.append(f"capabilities.{capability_id}: evidence_set_mismatch")
        if (
            not isinstance(evidence, list)
            or not evidence
            or not all(isinstance(path, str) for path in evidence)
            or len(evidence) != len(set(evidence))
        ):
            errors.append(f"capabilities.{capability_id}: invalid_evidence_list")
        else:
            for index, evidence_path in enumerate(evidence):
                repository_file(
                    root,
                    evidence_path,
                    f"capabilities.{capability_id}.evidence[{index}]",
                    errors,
                )

        blocker_text = item.get("blockers")
        if rule.status == "passed":
            if blocker_text != []:
                errors.append(f"capabilities.{capability_id}: passed_must_have_no_blockers")
            source = registry_by_id.get(rule.source_task or "", {})
            if source.get("status") != "completed":
                errors.append(f"capabilities.{capability_id}: passed_source_not_completed")
        else:
            blockers.append(capability_id)
            if not valid_text_list(blocker_text, allow_empty=False):
                errors.append(f"capabilities.{capability_id}: blocker_text_required")
            follow_up = item.get("follow_up_task")
            if (
                not isinstance(follow_up, str)
                or not follow_up.strip()
                or contains_control_characters(follow_up)
            ):
                errors.append(f"capabilities.{capability_id}: follow_up_task_required")

        if rule.release_target is not None:
            outcome = gate_outcomes.get(rule.release_target)
            if outcome is not None and outcome.allowed and rule.status != "passed":
                errors.append(f"capabilities.{capability_id}: blocked_status_has_allowed_target")
            if outcome is not None and not outcome.allowed and rule.status == "passed":
                errors.append(f"capabilities.{capability_id}: passed_status_has_blocked_target")
    return blockers, counts


def validate_server_policy(root: Path, errors: list[str]) -> None:
    policy_path = repository_file(
        root,
        SERVER_RELEASE_POLICY.relative_to(ROOT).as_posix(),
        "server_release_policy.path",
        errors,
    )
    if policy_path is None:
        return
    policy = load_json(policy_path, "server_release_policy", errors)
    if policy is None:
        return
    host = policy.get("host")
    if not isinstance(host, dict):
        errors.append("server_release_policy.host: must_be_object")
        return
    if host.get("business_api_ready") is not False:
        errors.append("server_release_policy.host: business_api_ready_must_remain_false")
    if host.get("production_file_writes_enabled") is not False:
        errors.append("server_release_policy.host: production_file_writes_enabled_must_remain_false")


def validate_output_contract(policy: dict[str, Any], errors: list[str]) -> None:
    output = policy.get("output")
    expected = {
        "root": OUTPUT_ROOT.as_posix(),
        "marker": OUTPUT_MARKER,
        "marker_value": OUTPUT_MARKER_VALUE,
    }
    if not isinstance(output, dict) or output != expected:
        errors.append("output: fixed_task_boundary_mismatch")


def validate_repository(
    root: Path = ROOT,
    policy_path: Path | None = None,
) -> tuple[list[str], dict[str, Any]]:
    errors: list[str] = []
    if policy_path is None:
        policy_path = repository_file(
            root,
            DEFAULT_POLICY.relative_to(ROOT).as_posix(),
            "alpha_policy.path",
            errors,
        )
    registry_path = repository_file(
        root,
        REGISTRY.relative_to(ROOT).as_posix(),
        "task_registry.path",
        errors,
    )
    policy = load_json(policy_path, "alpha_policy", errors) if policy_path else None
    registry = load_json(registry_path, "task_registry", errors) if registry_path else None
    if policy is None or registry is None:
        return errors, {"contract": "v01-009/1", "decision": "invalid", "blockers": []}

    if policy.get("version") != 1:
        errors.append("alpha_policy: version_must_be_1")
    if set(policy) != POLICY_FIELDS:
        errors.append("alpha_policy: field_set_mismatch")
    if policy.get("contract") != "v01-009/1":
        errors.append("alpha_policy: contract_mismatch")
    if policy.get("declared_decision") not in {"blocked", "ready"}:
        errors.append("alpha_policy: invalid_declared_decision")
    validate_output_contract(policy, errors)

    registry_by_id = validate_task_inputs(root, policy, registry, errors)
    gate_outcomes = validate_release_targets(root, policy, errors)
    capability_blockers, counts = validate_capabilities(
        root,
        policy,
        registry_by_id,
        gate_outcomes,
        errors,
    )
    validate_server_policy(root, errors)

    target_blockers = [target for target in RELEASE_TARGETS if not gate_outcomes.get(target, GateOutcome(False, ())).allowed]
    incomplete_inputs = [
        task_id
        for task_id, rule in TASK_RULES.items()
        if rule.registry_status != "completed"
    ]
    decision = "ready" if not capability_blockers and not target_blockers and not incomplete_inputs else "blocked"
    if policy.get("declared_decision") != decision:
        errors.append("alpha_policy: declared_decision_does_not_match_computed_decision")

    blockers: list[dict[str, Any]] = [
        {"kind": "capability", "id": capability_id}
        for capability_id in capability_blockers
    ]
    blockers.extend(
        {
            "kind": "release_target",
            "id": target,
            "gate_ids": list(gate_outcomes[target].blocker_ids),
        }
        for target in target_blockers
        if target in gate_outcomes
    )
    blockers.extend(
        {"kind": "partial_input", "id": task_id}
        for task_id in incomplete_inputs
    )
    report = {
        "contract": "v01-009/1",
        "audit_status": "passed" if not errors else "failed",
        "decision": decision,
        "capability_counts": counts,
        "blockers": blockers,
    }
    return errors, report


def ensure_directory_chain(root: Path, relative: PurePosixPath) -> Path:
    current = root
    for part in relative.parts:
        current /= part
        if current.exists():
            if not current.is_dir() or is_reparse_point(current):
                raise OutputBoundaryError("output_path_reparse_or_non_directory")
        else:
            try:
                current.mkdir()
            except OSError as error:
                raise OutputBoundaryError("output_directory_creation_failed") from error
            if is_reparse_point(current):
                raise OutputBoundaryError("output_directory_became_reparse_point")
    return current


def prepare_output_path(root: Path, value: str) -> Path:
    if not value or "\\" in value or contains_control_characters(value):
        raise OutputBoundaryError("output_must_be_repository_relative_posix_path")
    relative = PurePosixPath(value)
    if (
        relative.is_absolute()
        or relative.as_posix() != value
        or ".." in relative.parts
        or has_unsafe_windows_component(relative)
    ):
        raise OutputBoundaryError("output_must_be_repository_relative_posix_path")
    if relative.suffix.lower() != ".json":
        raise OutputBoundaryError("output_must_be_json")
    try:
        inside = relative.relative_to(OUTPUT_ROOT)
    except ValueError as error:
        raise OutputBoundaryError("output_outside_task_root") from error
    if not inside.parts:
        raise OutputBoundaryError("output_cannot_be_task_root")

    task_root = ensure_directory_chain(root, OUTPUT_ROOT)
    parent = ensure_directory_chain(task_root, PurePosixPath(*inside.parts[:-1]))
    target = parent / inside.name
    if target.exists() and (not target.is_file() or is_reparse_point(target)):
        raise OutputBoundaryError("output_target_invalid")

    marker = task_root / OUTPUT_MARKER
    if marker.exists():
        if not marker.is_file() or is_reparse_point(marker):
            raise OutputBoundaryError("output_marker_invalid")
        try:
            marker_value = marker.read_text(encoding="utf-8")
        except (OSError, UnicodeError) as error:
            raise OutputBoundaryError("output_marker_unreadable") from error
        if marker_value != OUTPUT_MARKER_VALUE:
            raise OutputBoundaryError("output_marker_mismatch")
    else:
        try:
            with marker.open("x", encoding="utf-8", newline="") as stream:
                stream.write(OUTPUT_MARKER_VALUE)
                stream.flush()
                os.fsync(stream.fileno())
        except OSError as error:
            raise OutputBoundaryError("output_marker_creation_failed") from error
    return target


def write_report(root: Path, output: str, report: dict[str, Any]) -> None:
    target = prepare_output_path(root, output)
    temporary_name: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            "w",
            encoding="utf-8",
            newline="\n",
            dir=target.parent,
            prefix=".alpha-readiness.",
            suffix=".tmp",
            delete=False,
        ) as stream:
            temporary_name = stream.name
            json.dump(report, stream, ensure_ascii=False, indent=2, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_name, target)
        temporary_name = None
    except OSError as error:
        raise OutputBoundaryError("output_atomic_write_failed") from error
    finally:
        if temporary_name is not None:
            try:
                Path(temporary_name).unlink(missing_ok=True)
            except OSError:
                pass


def print_blockers(report: dict[str, Any]) -> None:
    for blocker in report.get("blockers", []):
        identifier = blocker.get("id", "unknown")
        kind = blocker.get("kind", "unknown")
        gate_ids = blocker.get("gate_ids", [])
        suffix = f" gates={','.join(gate_ids)}" if gate_ids else ""
        print(f"- {kind}:{identifier}{suffix}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--require-ready",
        action="store_true",
        help="Return 3 unless all required Alpha capabilities, inputs and release targets pass.",
    )
    parser.add_argument(
        "--output",
        help=(
            "Optional repository-relative JSON path below "
            ".runtime/sandbox-storage/V01-009/."
        ),
    )
    arguments = parser.parse_args()

    errors, report = validate_repository()
    if errors:
        print("ALPHA_INTEGRATION_AUDIT_ERROR", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return AUDIT_ERROR_EXIT

    if arguments.output:
        try:
            write_report(ROOT, arguments.output, report)
        except OutputBoundaryError as error:
            print(f"ALPHA_INTEGRATION_AUDIT_ERROR\n- output: {error}", file=sys.stderr)
            return AUDIT_ERROR_EXIT

    print(f"ALPHA_INTEGRATION_AUDIT_OK decision={report['decision']}")
    if report["decision"] != "ready":
        print_blockers(report)
        return RELEASE_BLOCKED_EXIT if arguments.require_ready else 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
