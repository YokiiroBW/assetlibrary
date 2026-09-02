from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


architecture = load_module("assetlibrary_architecture_gate", ROOT / "scripts" / "validate_architecture_baseline.py")
release_gates = load_module("assetlibrary_release_gates", ROOT / "tests" / "architecture" / "check_release_gates.py")


class ArchitectureRuleTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "AGENTS.md").write_text(
            "模块化单体 依赖方向 语言与框架预算 architecture_review\n",
            encoding="utf-8",
        )

    def rules(self) -> dict:
        return {
            "version": 2,
            "enforcement_status": "active",
            "architecture_style": "modular-monolith-ports-adapters-isolated-workers",
            "full_enforcement_task": "M0-009",
            "required_files": [],
            "layers": ["Domain", "Application", "Infrastructure", "Contracts", "Adapters"],
            "layer_rules": {
                "module_root": "services/core-server/Modules",
                "cross_module_public_layers": ["Contracts", "Public"],
                "forbidden_targets_by_source": {
                    "Domain": ["Application", "Infrastructure", "Adapters", "UI"],
                    "Application": ["Infrastructure", "Adapters", "UI"],
                },
                "forbidden_tokens_by_source": {
                    "Domain": ["system.io", "npgsql"],
                    "Application": ["npgsql"],
                },
            },
            "module_owners": [{"module": "Fixture", "owns": "fixture state"}],
            "source_roots": [
                {
                    "root": "services/core-server",
                    "primary_language": "C# fixture",
                    "allowed_source_extensions": [".cs"],
                    "activation_extensions": [".cs"],
                },
                {
                    "root": "apps/web",
                    "primary_language": "TypeScript fixture",
                    "allowed_source_extensions": [".ts", ".tsx"],
                    "activation_extensions": [".ts", ".tsx"],
                },
                {
                    "root": "apps/windows-shell",
                    "primary_language": "C++ fixture",
                    "allowed_source_extensions": [".cpp", ".h"],
                    "activation_extensions": [".cpp", ".h"],
                },
                {
                    "root": "providers",
                    "primary_language": "C# fixture",
                    "allowed_source_extensions": [".cs"],
                    "activation_extensions": [".cs"],
                },
                {
                    "root": "packages/sdk/assetlink/typescript",
                    "primary_language": "TypeScript fixture",
                    "allowed_source_extensions": [".ts"],
                    "activation_extensions": [".ts"],
                },
            ],
            "architecture_scoped_roots": ["apps", "services", "gateways", "integrations", "providers", "packages"],
            "forbidden_unbounded_directories": ["common", "shared", "utils", "helpers"],
            "boundary_rules": [],
            "contract_sync": [],
        }

    def validate(self, rules: dict | None = None):
        selected = rules or self.rules()
        rules_path = self.root / "architecture-rules.json"
        rules_path.write_text(json.dumps(selected), encoding="utf-8")
        return architecture.validate_repository(self.root, rules_path)

    def write(self, relative: str, content: str = "") -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def test_valid_layered_module_and_language_manifest_pass(self) -> None:
        rules = self.rules()
        rules["source_roots"] = [
            {
                "root": "services/core-server",
                "primary_language": "C#",
                "allowed_source_extensions": [".cs"],
                "activation_extensions": [".cs"],
                "required_manifest_globs": ["*.csproj"],
            }
        ]
        self.write("services/core-server/Core.csproj", "<Project />")
        self.write(
            "services/core-server/Modules/Alpha/Domain/Asset.cs",
            "namespace AssetLibrary.Modules.Alpha.Domain; public sealed class Asset {}",
        )
        self.assertEqual(self.validate(rules).errors, [])

    def test_domain_reverse_dependency_and_platform_access_fail(self) -> None:
        path = self.write(
            "services/core-server/Modules/Alpha/Domain/Asset.cs",
            "namespace AssetLibrary.Modules.Alpha.Domain;",
        )
        self.assertEqual(self.validate().errors, [])
        path.write_text(
            "using System.IO; using AssetLibrary.Modules.Alpha.Infrastructure;",
            encoding="utf-8",
        )
        errors = self.validate().errors
        self.assertTrue(any("Domain platform/infrastructure dependency" in error for error in errors), errors)
        self.assertTrue(any("forbidden layer dependency" in error for error in errors), errors)

    def test_cross_module_internal_access_and_cycle_fail(self) -> None:
        alpha = self.write(
            "services/core-server/Modules/Alpha/Application/A.cs",
            "using AssetLibrary.Modules.Beta.Contracts;",
        )
        beta = self.write(
            "services/core-server/Modules/Beta/Application/B.cs",
            "namespace AssetLibrary.Modules.Beta.Application;",
        )
        self.assertEqual(self.validate().errors, [])
        alpha.write_text(
            "using AssetLibrary.Modules.Beta.Infrastructure; using AssetLibrary.Modules.Beta.Contracts;",
            encoding="utf-8",
        )
        beta.write_text(
            "using AssetLibrary.Modules.Alpha.Contracts;",
            encoding="utf-8",
        )
        errors = self.validate().errors
        self.assertTrue(any("cross-module internal dependency" in error for error in errors), errors)
        self.assertTrue(any("module dependency cycle" in error for error in errors), errors)

    def test_language_budget_requires_allowed_language_manifest_and_lock(self) -> None:
        rules = self.rules()
        rules["source_roots"] = [
            {
                "root": "apps/web",
                "primary_language": "TypeScript",
                "allowed_source_extensions": [".ts", ".tsx"],
                "activation_extensions": [".ts", ".tsx"],
                "required_manifest_globs": ["package.json"],
                "required_lockfile_globs": ["pnpm-lock.yaml"],
            }
        ]
        self.write("apps/web/main.js", "export const value = 1;")
        errors = self.validate(rules).errors
        self.assertTrue(any("language budget violation" in error for error in errors), errors)

        (self.root / "apps/web/main.js").unlink()
        self.write("apps/web/main.ts", "export const value = 1;")
        errors = self.validate(rules).errors
        self.assertTrue(any("lacks a build manifest" in error for error in errors), errors)
        self.assertTrue(any("lacks a frozen dependency" in error for error in errors), errors)

        self.write("apps/web/package.json", "{}")
        self.write("apps/web/pnpm-lock.yaml", "lockfileVersion: '9.0'\n")
        self.assertEqual(self.validate(rules).errors, [])

    def test_source_outside_registered_language_root_fails(self) -> None:
        rules = self.rules()
        rules["source_roots"] = []
        self.write("services/unowned/main.go", "package main")
        errors = self.validate(rules).errors
        self.assertTrue(any("outside every registered language-budget root" in error for error in errors), errors)

        rules["source_roots"] = [
            {
                "root": "services/unowned",
                "primary_language": "Go fixture",
                "allowed_source_extensions": [".go"],
                "activation_extensions": [".go"],
            }
        ]
        self.assertEqual(self.validate(rules).errors, [])

    def test_adapter_database_and_business_logic_tokens_fail(self) -> None:
        rules = self.rules()
        rules["boundary_rules"] = [
            {
                "id": "no-postgres",
                "roots": ["apps"],
                "forbidden_tokens": ["from 'pg'"],
            },
            {
                "id": "no-core-copy",
                "roots": ["apps"],
                "forbidden_tokens": ["class transferstatemachine"],
            },
            {
                "id": "provider-no-postgres",
                "roots": ["providers"],
                "forbidden_tokens": ["npgsql"],
            },
        ]
        app = self.write("apps/web/view.ts", "export const view = true;")
        provider = self.write("providers/metadata/Worker.cs", "public sealed class Worker {}")
        self.assertEqual(self.validate(rules).errors, [])
        app.write_text("import postgres from 'pg'; class TransferStateMachine {}", encoding="utf-8")
        provider.write_text("using Npgsql;", encoding="utf-8")
        errors = self.validate(rules).errors
        self.assertTrue(any("no-postgres violation" in error for error in errors), errors)
        self.assertTrue(any("no-core-copy violation" in error for error in errors), errors)
        self.assertTrue(any("provider-no-postgres violation" in error for error in errors), errors)

    def test_shell_heavy_dependency_fails(self) -> None:
        rules = self.rules()
        rules["boundary_rules"] = [
            {"id": "minimal-shell", "roots": ["apps/windows-shell"], "forbidden_tokens": ["winhttp"]}
        ]
        bridge = self.write("apps/windows-shell/bridge.cpp", "#include <windows.h>")
        self.assertEqual(self.validate(rules).errors, [])
        bridge.write_text("#include <winhttp.h>", encoding="utf-8")
        errors = self.validate(rules).errors
        self.assertTrue(any("minimal-shell violation" in error for error in errors), errors)

    def test_unbounded_shared_directory_fails(self) -> None:
        self.write("services/core-server/Modules/Alpha/Domain/Asset.cs", "class Asset {}")
        self.assertEqual(self.validate().errors, [])
        self.write("services/core-server/Helpers/Everything.cs", "class Everything {}")
        errors = self.validate().errors
        self.assertTrue(any("forbidden unbounded directory" in error for error in errors), errors)

    def test_contract_consumer_digest_passes_and_stale_digest_fails(self) -> None:
        rules = self.rules()
        rules["contract_sync"] = [
            {
                "name": "assetlink",
                "source_root": "contracts/assetlink",
                "consumer_roots": ["packages/sdk/assetlink/typescript"],
                "stamp_file": ".contract-source.sha256",
            }
        ]
        self.write("contracts/assetlink/control.schema.json", "{}")
        self.write("packages/sdk/assetlink/typescript/control.ts", "export interface Control {}")
        source = self.root / "contracts/assetlink"
        digest = architecture.contract_digest(source, self.root)
        stamp = self.write("packages/sdk/assetlink/typescript/.contract-source.sha256", digest + "\n")
        self.assertEqual(self.validate(rules).errors, [])

        stamp.write_text("0" * 64 + "\n", encoding="utf-8")
        errors = self.validate(rules).errors
        self.assertTrue(any("consumer is stale" in error for error in errors), errors)

    def gate(self, gate_id: str, index: int, classification: str = "blocking_release_or_later_milestone") -> dict:
        gate = {
            "id": gate_id,
            "source_task": "M0-X",
            "source_blocker_index": index,
            "classification": classification,
            "status": "open",
            "decision": "fixture decision",
            "blocked_targets": ["fixture-release"],
            "owner": "fixture-owner",
            "follow_up_task": "FIXTURE-001",
            "target_milestone": "Fixture",
            "enforced_by": ["python check.py --target fixture-release"],
            "exit_criteria": ["fixture exits"],
        }
        if classification == "deferred_fail_closed":
            gate["default_enabled"] = False
            gate["allowed_scope"] = "fixture only"
        return gate

    def write_gate_fixture(self, rules: dict, gates: list[dict]) -> None:
        rules["m0_gate_ledger"] = "tests/architecture/m0-gates.json"
        self.write(
            ".codex/task-registry.json",
            json.dumps(
                {
                    "tasks": [
                        {"id": "M0-X", "status": "partial"},
                        {"id": "M0-009", "accepted_partial_dependencies": ["M0-X"]},
                    ]
                }
            ),
        )
        self.write(".codex/handoffs/M0-X/result.json", json.dumps({"blocked_by": ["one", "two"]}))
        self.write(
            "tests/architecture/m0-gates.json",
            json.dumps(
                {
                    "version": 1,
                    "decision_status": "frozen",
                    "v0_1_start_decision": "authorized_for_scoped_implementation",
                    "v0_1_start_conditions": ["fixture condition"],
                    "partial_dependencies": ["M0-X"],
                    "audited_commits": {"M0-X": "fixture-commit"},
                    "gates": gates,
                }
            ),
        )
        result_path = self.root / ".codex/handoffs/M0-X/result.json"
        result_path.write_text(
            json.dumps({"commit": "fixture-commit", "blocked_by": ["one", "two"]}),
            encoding="utf-8",
        )

    def test_m0_gate_ledger_requires_complete_coverage_and_fail_closed_deferrals(self) -> None:
        rules = self.rules()
        gates = [self.gate("G1", 0), self.gate("G2", 1, "deferred_fail_closed")]
        self.write_gate_fixture(rules, gates)
        self.assertEqual(self.validate(rules).errors, [])

        ledger_path = self.root / "tests/architecture/m0-gates.json"
        ledger = json.loads(ledger_path.read_text(encoding="utf-8"))
        ledger["gates"] = [self.gate("G1", 0)]
        ledger_path.write_text(json.dumps(ledger), encoding="utf-8")
        errors = architecture.validate_repository(self.root, self.root / "architecture-rules.json").errors
        self.assertTrue(any("cover every blocker exactly once" in error for error in errors), errors)

    def test_v0_1_start_cannot_be_authorized_with_an_open_start_blocker(self) -> None:
        rules = self.rules()
        gates = [self.gate("G1", 0), self.gate("G2", 1)]
        gates[0]["classification"] = "blocking_v0_1_start"
        gates[0]["blocked_targets"] = ["v0.1-start"]
        gates[0]["enforced_by"] = ["python check.py --target v0.1-start"]
        self.write_gate_fixture(rules, gates)
        errors = self.validate(rules).errors
        self.assertTrue(any("V0.1 start cannot be authorized" in error for error in errors), errors)

    def test_ci_tiers_require_real_workflows_commands_and_skip_policies(self) -> None:
        rules = self.rules()
        rules["ci_tier_contract"] = "tests/architecture/ci-tiers.json"
        tiers = []
        for tier_id in ("fast-merge", "platform", "scheduled-release"):
            workflow = f".github/workflows/{tier_id}.yml"
            self.write(workflow, f"jobs:\n  {tier_id}:\n    run: python check.py\n")
            tiers.append(
                {
                    "id": tier_id,
                    "workflow": workflow,
                    "timeout_minutes": 10,
                    "required_for": ["fixture"],
                    "runners": ["fixture-runner"],
                    "cache": "none",
                    "artifacts": ["fixture-log"],
                    "failure_policy": "fail",
                    "commands": ["python check.py"],
                    "skip_policy": "skips do not pass",
                }
            )
        contract = self.write(
            "tests/architecture/ci-tiers.json",
            json.dumps(
                {
                    "tiers": tiers,
                    "future_native_gate_activation": {
                        key: "fixture prerequisite"
                        for key in ("dotnet", "typescript", "android", "cpp", "database", "release_labs")
                    },
                }
            ),
        )
        self.assertEqual(self.validate(rules).errors, [])

        payload = json.loads(contract.read_text(encoding="utf-8"))
        payload["tiers"][0]["commands"] = ["TODO <command>"]
        contract.write_text(json.dumps(payload), encoding="utf-8")
        errors = self.validate(rules).errors
        self.assertTrue(any("placeholder command" in error for error in errors), errors)

    def test_release_gate_allows_scoped_start_and_blocks_unresolved_target(self) -> None:
        ledger = {
            "version": 1,
            "decision_status": "frozen",
            "v0_1_start_decision": "authorized_for_scoped_implementation",
            "gates": [
                {"id": "G1", "status": "open", "decision": "not ready", "blocked_targets": ["release"]}
            ],
        }
        self.assertEqual(release_gates.evaluate_target(ledger, "v0.1-start"), (True, []))
        allowed, blockers = release_gates.evaluate_target(ledger, "release")
        self.assertFalse(allowed)
        self.assertEqual([gate["id"] for gate in blockers], ["G1"])

        allowed, blockers = release_gates.evaluate_target({}, "release")
        self.assertFalse(allowed)
        self.assertEqual([gate["id"] for gate in blockers], ["LEDGER_INVALID"])


if __name__ == "__main__":
    unittest.main()
