#!/usr/bin/env python3
"""Executable architecture, language-budget, contract and M0 gate validation."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

KNOWN_SOURCE_EXTENSIONS = {
    ".c",
    ".cc",
    ".cpp",
    ".cs",
    ".cxx",
    ".fs",
    ".go",
    ".h",
    ".hpp",
    ".idl",
    ".java",
    ".js",
    ".jsx",
    ".kt",
    ".kts",
    ".py",
    ".rs",
    ".ts",
    ".tsx",
    ".vb",
}
BOUNDARY_FILE_NAMES = {
    "cmakelists.txt",
    "directory.build.props",
    "directory.build.targets",
    "package.json",
}
BOUNDARY_FILE_EXTENSIONS = KNOWN_SOURCE_EXTENSIONS | {
    ".cmake",
    ".csproj",
    ".gradle",
    ".json",
    ".props",
    ".targets",
}
IGNORED_DIRECTORY_NAMES = {
    ".git",
    ".runtime",
    ".venv",
    "__pycache__",
    "bin",
    "build",
    "dist",
    "node_modules",
    "obj",
    "out",
}
GATE_CLASSIFICATIONS = {
    "blocking_v0_1_start",
    "deferred_fail_closed",
    "blocking_release_or_later_milestone",
    "rejected",
}

MODULE_REFERENCE_PATTERNS = (
    re.compile(r"(?:AssetLibrary\.)?Modules\.([A-Za-z][A-Za-z0-9_]*)\.([A-Za-z][A-Za-z0-9_]*)"),
    re.compile(r"@assetlibrary/modules/([A-Za-z][A-Za-z0-9_-]*)/([A-Za-z][A-Za-z0-9_-]*)", re.I),
    re.compile(r"assetlibrary\.modules\.([A-Za-z][A-Za-z0-9_]*)\.([A-Za-z][A-Za-z0-9_]*)", re.I),
    re.compile(r"AssetLibrary/Modules/([A-Za-z][A-Za-z0-9_]*)/([A-Za-z][A-Za-z0-9_]*)", re.I),
)


@dataclass
class ValidationReport:
    errors: list[str] = field(default_factory=list)
    scanned_files: int = 0
    active_roots: list[str] = field(default_factory=list)
    inactive_roots: list[str] = field(default_factory=list)
    module_edges: set[tuple[str, str]] = field(default_factory=set)


def relative(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def is_ignored(path: Path, root: Path) -> bool:
    try:
        parts = path.relative_to(root).parts
    except ValueError:
        return True
    return any(part.lower() in IGNORED_DIRECTORY_NAMES for part in parts)


def iter_files(path: Path, root: Path):
    if not path.exists():
        return
    for candidate in path.rglob("*"):
        if candidate.is_file() and not candidate.is_symlink() and not is_ignored(candidate, root):
            yield candidate


def is_boundary_file(path: Path) -> bool:
    return path.name.lower() in BOUNDARY_FILE_NAMES or path.suffix.lower() in BOUNDARY_FILE_EXTENSIONS


def read_text(path: Path, report: ValidationReport, root: Path) -> str | None:
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        report.errors.append(f"cannot read UTF-8 architecture input {relative(path, root)}: {exc}")
        return None
    report.scanned_files += 1
    return text


def load_json(path: Path, report: ValidationReport, root: Path) -> dict | None:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        label = relative(path, root) if path.is_relative_to(root) else str(path)
        report.errors.append(f"invalid JSON {label}: {exc}")
        return None
    if not isinstance(value, dict):
        report.errors.append(f"JSON root must be an object: {relative(path, root)}")
        return None
    return value


def resolve_config_path(root: Path, value: str) -> Path:
    path = Path(value)
    return path if path.is_absolute() else root / path


def validate_baseline(root: Path, rules: dict, report: ValidationReport) -> None:
    if rules.get("version") != 2:
        report.errors.append("architecture rules version must be 2")
    if rules.get("enforcement_status") != "active":
        report.errors.append("architecture enforcement_status must be active")
    if rules.get("architecture_style") != "modular-monolith-ports-adapters-isolated-workers":
        report.errors.append("unexpected architecture_style")
    if rules.get("full_enforcement_task") != "M0-009":
        report.errors.append("full_enforcement_task must remain M0-009")

    for item in rules.get("required_files", []):
        path = resolve_config_path(root, item)
        if not path.is_file() or path.stat().st_size == 0:
            report.errors.append(f"missing or empty: {item}")

    owners = rules.get("module_owners", [])
    owner_names = [owner.get("module") for owner in owners if isinstance(owner, dict)]
    if not owners or len(owner_names) != len(set(owner_names)) or any(not name for name in owner_names):
        report.errors.append("module_owners must contain unique, non-empty module names")
    for owner in owners:
        if not isinstance(owner, dict) or not str(owner.get("owns", "")).strip():
            report.errors.append("every module owner must describe the state or policy it owns")

    registry_path = root / ".codex" / "task-registry.json"
    graph_path = root / ".codex" / "task-graph.json"
    registry = load_json(registry_path, report, root) if registry_path.exists() else None
    graph = load_json(graph_path, report, root) if graph_path.exists() else None
    if registry is not None:
        ids = [task.get("id") for task in registry.get("tasks", []) if isinstance(task, dict)]
        if ids.count("M0-009") != 1:
            report.errors.append("M0-009 must appear exactly once in task registry")
    if graph is not None:
        node = next((item for item in graph.get("nodes", []) if item.get("id") == "M0-009"), None)
        expected = {f"M0-{number:03d}" for number in range(1, 9)}
        if node is None or not expected.issubset(set(node.get("depends_on", []))):
            report.errors.append("M0-009 must converge all M0-001..M0-008 results")

    agents_path = root / "AGENTS.md"
    agents = agents_path.read_text(encoding="utf-8") if agents_path.exists() else ""
    for marker in ("模块化单体", "依赖方向", "语言与框架预算", "architecture_review"):
        if marker not in agents:
            report.errors.append(f"AGENTS.md marker missing: {marker}")

    schema_path = root / "contracts" / "handoffs" / "task-result.schema.json"
    template_path = root / ".codex" / "handoffs" / "_template" / "result.json"
    schema = load_json(schema_path, report, root) if schema_path.exists() else None
    template = load_json(template_path, report, root) if template_path.exists() else None
    if schema is not None and "architecture_review" not in schema.get("required", []):
        report.errors.append("task result schema must require architecture_review")
    if template is not None and "architecture_review" not in template:
        report.errors.append("task result template missing architecture_review")


def validate_language_budget(root: Path, rules: dict, report: ValidationReport) -> None:
    configured_roots: list[Path] = []
    for entry in rules.get("source_roots", []):
        if not isinstance(entry, dict) or not entry.get("root"):
            report.errors.append("source_roots entries must be objects with a root")
            continue
        label = entry["root"]
        source_root = resolve_config_path(root, label)
        configured_roots.append(source_root.resolve())
        allowed = {suffix.lower() for suffix in entry.get("allowed_source_extensions", [])}
        activation = {suffix.lower() for suffix in entry.get("activation_extensions", [])}
        source_files = [
            path
            for path in iter_files(source_root, root) or ()
            if path.suffix.lower() in KNOWN_SOURCE_EXTENSIONS
        ]
        active_files = [path for path in source_files if path.suffix.lower() in activation]

        for path in source_files:
            if path.suffix.lower() not in allowed:
                report.errors.append(
                    f"language budget violation in {relative(path, root)}; {entry.get('primary_language')} owns {label}"
                )

        if not active_files:
            report.inactive_roots.append(label)
            continue
        report.active_roots.append(label)

        manifest_globs = entry.get("required_manifest_globs", [])
        if manifest_globs and not any(any(source_root.glob(pattern)) for pattern in manifest_globs):
            report.errors.append(f"active source root lacks a build manifest: {label}")
        lock_globs = entry.get("required_lockfile_globs", [])
        if lock_globs and not any(any(source_root.glob(pattern)) for pattern in lock_globs):
            report.errors.append(f"active source root lacks a frozen dependency/verification file: {label}")

    for scoped_name in rules.get("architecture_scoped_roots", []):
        scoped_root = resolve_config_path(root, scoped_name)
        for path in iter_files(scoped_root, root) or ():
            if path.suffix.lower() not in KNOWN_SOURCE_EXTENSIONS:
                continue
            resolved = path.resolve()
            if not any(resolved.is_relative_to(source_root) for source_root in configured_roots):
                report.errors.append(f"source file is outside every registered language-budget root: {relative(path, root)}")


def validate_unbounded_directories(root: Path, rules: dict, report: ValidationReport) -> None:
    forbidden = {name.lower() for name in rules.get("forbidden_unbounded_directories", [])}
    found: set[str] = set()
    for root_name in rules.get("architecture_scoped_roots", []):
        scoped_root = resolve_config_path(root, root_name)
        if not scoped_root.exists():
            continue
        for path in scoped_root.rglob("*"):
            if (
                path.is_dir()
                and not path.is_symlink()
                and path.name.lower() in forbidden
                and not is_ignored(path, root)
                and any(iter_files(path, root) or ())
            ):
                found.add(relative(path, root))
    for item in sorted(found):
        report.errors.append(f"forbidden unbounded directory: {item}")


def validate_boundary_tokens(root: Path, rules: dict, report: ValidationReport) -> None:
    emitted: set[tuple[str, str]] = set()
    for rule in rules.get("boundary_rules", []):
        rule_id = rule.get("id", "unnamed-boundary-rule")
        tokens = [str(token).lower() for token in rule.get("forbidden_tokens", []) if str(token)]
        for root_name in rule.get("roots", []):
            scoped_root = resolve_config_path(root, root_name)
            for path in iter_files(scoped_root, root) or ():
                if not is_boundary_file(path):
                    continue
                text = read_text(path, report, root)
                if text is None:
                    continue
                lowered = text.lower()
                for token in tokens:
                    key = (rule_id, relative(path, root))
                    if token in lowered and key not in emitted:
                        report.errors.append(f"{rule_id} violation in {key[1]} (token: {token})")
                        emitted.add(key)
                        break


def module_references(text: str) -> set[tuple[str, str]]:
    references: set[tuple[str, str]] = set()
    for pattern in MODULE_REFERENCE_PATTERNS:
        references.update((match.group(1), match.group(2)) for match in pattern.finditer(text))
    return references


def find_cycles(edges: set[tuple[str, str]]) -> list[list[str]]:
    graph: dict[str, set[str]] = {}
    for source, target in edges:
        graph.setdefault(source, set()).add(target)
        graph.setdefault(target, set())
    state: dict[str, int] = {}
    stack: list[str] = []
    cycles: list[list[str]] = []

    def visit(node: str) -> None:
        state[node] = 1
        stack.append(node)
        for target in sorted(graph.get(node, set())):
            if state.get(target, 0) == 0:
                visit(target)
            elif state.get(target) == 1:
                start = stack.index(target)
                cycle = stack[start:] + [target]
                if cycle not in cycles:
                    cycles.append(cycle)
        stack.pop()
        state[node] = 2

    for node in sorted(graph):
        if state.get(node, 0) == 0:
            visit(node)
    return cycles


def validate_module_dependencies(root: Path, rules: dict, report: ValidationReport) -> None:
    layer_rules = rules.get("layer_rules", {})
    module_root = resolve_config_path(root, layer_rules.get("module_root", "services/core-server/Modules"))
    known_layers = {str(layer).lower(): str(layer) for layer in rules.get("layers", [])}
    public_layers = {str(layer).lower() for layer in layer_rules.get("cross_module_public_layers", [])}
    forbidden_targets = {
        str(source).lower(): {str(target).lower() for target in targets}
        for source, targets in layer_rules.get("forbidden_targets_by_source", {}).items()
    }
    forbidden_tokens = {
        str(source).lower(): [str(token).lower() for token in tokens]
        for source, tokens in layer_rules.get("forbidden_tokens_by_source", {}).items()
    }

    for path in iter_files(module_root, root) or ():
        if path.suffix.lower() not in KNOWN_SOURCE_EXTENSIONS:
            continue
        parts = path.relative_to(module_root).parts
        if len(parts) < 3 or parts[1].lower() not in known_layers:
            report.errors.append(f"module source must live under <Module>/<Layer>: {relative(path, root)}")
            continue
        source_module, source_layer = parts[0], parts[1]
        text = read_text(path, report, root)
        if text is None:
            continue
        lowered = text.lower()
        for token in forbidden_tokens.get(source_layer.lower(), []):
            if token in lowered:
                report.errors.append(
                    f"{source_layer} platform/infrastructure dependency in {relative(path, root)} (token: {token})"
                )
                break

        for target_module, target_layer in module_references(text):
            source_key = source_module.lower()
            target_key = target_module.lower()
            target_layer_key = target_layer.lower()
            if target_layer_key in forbidden_targets.get(source_layer.lower(), set()):
                report.errors.append(
                    f"forbidden layer dependency {source_module}.{source_layer}->{target_module}.{target_layer} "
                    f"in {relative(path, root)}"
                )
            if source_key != target_key:
                report.module_edges.add((source_key, target_key))
                if target_layer_key not in public_layers:
                    report.errors.append(
                        f"cross-module internal dependency {source_module}->{target_module}.{target_layer} "
                        f"in {relative(path, root)}"
                    )

    for cycle in find_cycles(report.module_edges):
        report.errors.append("module dependency cycle: " + " -> ".join(cycle))


def contract_digest(source_root: Path, root: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(iter_files(source_root, root) or (), key=lambda item: item.as_posix()):
        rel = path.relative_to(source_root).as_posix().encode("utf-8")
        digest.update(len(rel).to_bytes(4, "big"))
        digest.update(rel)
        content = path.read_bytes()
        digest.update(len(content).to_bytes(8, "big"))
        digest.update(content)
    return digest.hexdigest()


def validate_contract_sync(root: Path, rules: dict, report: ValidationReport) -> None:
    for entry in rules.get("contract_sync", []):
        name = entry.get("name", "unnamed-contract")
        source_root = resolve_config_path(root, entry.get("source_root", ""))
        if not source_root.is_dir():
            report.errors.append(f"contract source root is missing: {entry.get('source_root')}")
            continue
        expected = contract_digest(source_root, root)
        stamp_name = entry.get("stamp_file", ".contract-source.sha256")
        for consumer_name in entry.get("consumer_roots", []):
            consumer_root = resolve_config_path(root, consumer_name)
            if not consumer_root.exists():
                continue
            active = any(
                path.suffix.lower() in KNOWN_SOURCE_EXTENSIONS
                for path in iter_files(consumer_root, root) or ()
            )
            stamp = consumer_root / stamp_name
            if not active and not stamp.exists():
                continue
            if not stamp.is_file():
                report.errors.append(f"generated {name} consumer lacks source digest: {consumer_name}/{stamp_name}")
                continue
            actual = stamp.read_text(encoding="utf-8").strip().lower()
            if actual != expected:
                report.errors.append(f"generated {name} consumer is stale: {consumer_name}")


def validate_m0_gate_ledger(root: Path, rules: dict, report: ValidationReport) -> None:
    ledger_name = rules.get("m0_gate_ledger")
    if not ledger_name:
        return
    ledger_path = resolve_config_path(root, ledger_name)
    ledger = load_json(ledger_path, report, root) if ledger_path.exists() else None
    if ledger is None:
        if not ledger_path.exists():
            report.errors.append(f"M0 gate ledger is missing: {ledger_name}")
        return
    if ledger.get("decision_status") != "frozen":
        report.errors.append("M0 gate ledger decision_status must be frozen")
    if ledger.get("version") != 1:
        report.errors.append("M0 gate ledger version must be 1")
    if not isinstance(ledger.get("v0_1_start_conditions"), list) or not ledger["v0_1_start_conditions"]:
        report.errors.append("M0 gate ledger must record V0.1 start conditions")

    registry_path = root / ".codex" / "task-registry.json"
    registry = load_json(registry_path, report, root) if registry_path.exists() else None
    if registry is None:
        report.errors.append("task registry is required for M0 gate coverage")
        return
    tasks = {item.get("id"): item for item in registry.get("tasks", []) if isinstance(item, dict)}
    gate_task = tasks.get("M0-009", {})
    partial_dependencies = ledger.get("partial_dependencies", [])
    accepted = gate_task.get("accepted_partial_dependencies", [])
    if partial_dependencies != accepted:
        report.errors.append("M0 ledger partial_dependencies must exactly match the audited registry order")
    for task_id in partial_dependencies:
        if tasks.get(task_id, {}).get("status") != "partial":
            report.errors.append(f"audited M0 dependency must remain partial: {task_id}")

    audited_commits = ledger.get("audited_commits", {})
    if set(audited_commits) != set(partial_dependencies):
        report.errors.append("M0 gate ledger must pin every audited partial dependency commit")

    gates = ledger.get("gates", [])
    ids: set[str] = set()
    coverage: dict[str, list[int]] = {task_id: [] for task_id in partial_dependencies}
    for gate in gates:
        gate_id = str(gate.get("id", ""))
        if not gate_id or gate_id in ids:
            report.errors.append(f"M0 gate IDs must be unique and non-empty: {gate_id or '<empty>'}")
        ids.add(gate_id)
        task_id = gate.get("source_task")
        index = gate.get("source_blocker_index")
        if task_id not in coverage or not isinstance(index, int) or index < 0:
            report.errors.append(f"{gate_id}: invalid source task or blocker index")
        else:
            coverage[task_id].append(index)
        classification = gate.get("classification")
        if classification not in GATE_CLASSIFICATIONS:
            report.errors.append(f"{gate_id}: invalid classification {classification}")
        if gate.get("status") not in {"open", "closed"}:
            report.errors.append(f"{gate_id}: status must be open or closed")
        for field_name in ("decision", "owner", "follow_up_task", "target_milestone"):
            if not str(gate.get(field_name, "")).strip():
                report.errors.append(f"{gate_id}: missing {field_name}")
        for field_name in ("blocked_targets", "enforced_by", "exit_criteria"):
            if not isinstance(gate.get(field_name), list) or not gate[field_name]:
                report.errors.append(f"{gate_id}: {field_name} must be a non-empty list")
        if classification == "deferred_fail_closed":
            if gate.get("default_enabled") is not False:
                report.errors.append(f"{gate_id}: deferred gate must set default_enabled=false")
            if not str(gate.get("allowed_scope", "")).strip():
                report.errors.append(f"{gate_id}: deferred gate must define allowed_scope")
        enforcement = "\n".join(str(item) for item in gate.get("enforced_by", []))
        for target in gate.get("blocked_targets", []):
            if f"--target {target}" not in enforcement:
                report.errors.append(f"{gate_id}: blocked target lacks executable release check: {target}")

    for task_id in partial_dependencies:
        result_path = root / ".codex" / "handoffs" / task_id / "result.json"
        result = load_json(result_path, report, root) if result_path.exists() else None
        if result is None:
            report.errors.append(f"missing handoff result for partial dependency: {task_id}")
            continue
        blockers = result.get("blocked_by", [])
        if audited_commits.get(task_id) != result.get("commit"):
            report.errors.append(f"M0 gate ledger audited commit is stale for {task_id}")
        expected = set(range(len(blockers)))
        actual = coverage.get(task_id, [])
        if len(actual) != len(expected) or set(actual) != expected:
            report.errors.append(
                f"M0 gate ledger must cover every blocker exactly once for {task_id}: "
                f"expected {sorted(expected)}, got {sorted(actual)}"
            )

    if ledger.get("v0_1_start_decision") == "authorized_for_scoped_implementation":
        for gate in gates:
            if gate.get("status") != "closed" and (
                gate.get("classification") == "blocking_v0_1_start"
                or "v0.1-start" in gate.get("blocked_targets", [])
            ):
                report.errors.append(f"V0.1 start cannot be authorized while {gate.get('id')} blocks it")


def validate_ci_tiers(root: Path, rules: dict, report: ValidationReport) -> None:
    contract_name = rules.get("ci_tier_contract")
    if not contract_name:
        return
    contract_path = resolve_config_path(root, contract_name)
    contract = load_json(contract_path, report, root) if contract_path.exists() else None
    if contract is None:
        if not contract_path.exists():
            report.errors.append(f"CI tier contract is missing: {contract_name}")
        return
    tiers = contract.get("tiers", [])
    by_id = {tier.get("id"): tier for tier in tiers if isinstance(tier, dict)}
    for tier_id in ("fast-merge", "platform", "scheduled-release"):
        tier = by_id.get(tier_id)
        if tier is None:
            report.errors.append(f"CI tier is missing: {tier_id}")
            continue
        workflow_name = tier.get("workflow", "")
        workflow = resolve_config_path(root, workflow_name)
        if not workflow.is_file():
            report.errors.append(f"CI workflow is missing for {tier_id}: {workflow_name}")
        else:
            content = workflow.read_text(encoding="utf-8").lower()
            if tier_id not in content:
                report.errors.append(f"CI workflow does not declare its tier {tier_id}: {workflow_name}")
        if not isinstance(tier.get("timeout_minutes"), int) or tier["timeout_minutes"] <= 0:
            report.errors.append(f"CI tier needs a positive timeout: {tier_id}")
        commands = tier.get("commands")
        if not isinstance(commands, list) or not commands:
            report.errors.append(f"CI tier needs executable commands: {tier_id}")
        elif any("todo" in str(command).lower() or "<" in str(command) for command in commands):
            report.errors.append(f"CI tier contains a placeholder command: {tier_id}")
        elif workflow.is_file():
            for command in commands:
                if str(command).lower() not in content:
                    report.errors.append(
                        f"CI workflow does not execute contracted command for {tier_id}: {command}"
                    )
        for field_name in ("required_for", "runners", "artifacts"):
            if not isinstance(tier.get(field_name), list) or not tier[field_name]:
                report.errors.append(f"CI tier needs a non-empty {field_name}: {tier_id}")
        for field_name in ("cache", "failure_policy"):
            if not str(tier.get(field_name, "")).strip():
                report.errors.append(f"CI tier needs an explicit {field_name}: {tier_id}")
        if not str(tier.get("skip_policy", "")).strip():
            report.errors.append(f"CI tier needs an explicit skip policy: {tier_id}")

    activation = contract.get("future_native_gate_activation", {})
    expected_activation = {"dotnet", "typescript", "android", "cpp", "database", "release_labs"}
    if set(activation) != expected_activation or any(not str(value).strip() for value in activation.values()):
        report.errors.append("CI contract must freeze every native gate activation prerequisite")


def validate_repository(root: Path, rules_path: Path | None = None) -> ValidationReport:
    root = root.resolve()
    report = ValidationReport()
    selected_rules = (rules_path or root / "tests" / "architecture" / "architecture-rules.json").resolve()
    rules = load_json(selected_rules, report, root) if selected_rules.exists() else None
    if rules is None:
        if not selected_rules.exists():
            report.errors.append(f"architecture rules are missing: {selected_rules}")
        return report

    validate_baseline(root, rules, report)
    validate_language_budget(root, rules, report)
    validate_unbounded_directories(root, rules, report)
    validate_boundary_tokens(root, rules, report)
    validate_module_dependencies(root, rules, report)
    validate_contract_sync(root, rules, report)
    validate_m0_gate_ledger(root, rules, report)
    validate_ci_tiers(root, rules, report)
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--rules", type=Path)
    args = parser.parse_args()

    report = validate_repository(args.root, args.rules)
    if report.errors:
        for error in report.errors:
            print("ERROR:", error)
        print(f"Architecture quality gate failed: {len(report.errors)} issue(s).")
        return 1

    print("Architecture quality gate passed.")
    print("Policy: modular monolith + ports/adapters + isolated workers")
    print("Enforcement task: M0-009")
    print(f"Architecture inputs scanned: {report.scanned_files}")
    print("Active governed roots:", ", ".join(report.active_roots) or "none (M0 skeleton)")
    print("Inactive governed roots:", len(report.inactive_roots))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
