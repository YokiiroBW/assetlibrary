from __future__ import annotations

import hashlib
import json
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CORE_ROOTS = (
    ROOT / "services/core-server/Modules/TransferSync",
    ROOT / "services/core-server/Modules/OperationTrash",
)
TEST_ROOT = ROOT / "tests/dotnet/AssetLibrary.TransferOperation.Tests"


def read_csharp(roots: tuple[Path, ...]) -> str:
    return "\n".join(
        path.read_text(encoding="utf-8")
        for root in roots
        for path in sorted(root.rglob("*.cs"))
    )


class TransferOperationFoundationRepositoryTests(unittest.TestCase):
    def test_canonical_operation_contract_remains_frozen_lf_or_crlf(self) -> None:
        contract = ROOT / "contracts/operations/operation-plan.schema.json"

        # Preserve the original CRLF hash and the published d7b43ec Git LF hash.
        self.assertIn(
            hashlib.sha256(contract.read_bytes()).hexdigest(),
            ("f2e29c8ce5e8b43a79c01b4b8b7b0db8c63b0122aa250ab5d758db27181436b6",
             "4c5d7eddb9f5796325d6b44be203445c44a947a7307f4f3a7fe94bdf9fb0cc4b"),
        )

    def test_production_modules_define_ports_and_only_read_only_inspection_adapters(self) -> None:
        allowed_layers = {"Application", "Contracts", "Domain"}
        for root in CORE_ROOTS:
            expected_layers = allowed_layers | ({"Infrastructure"} if root.name == "OperationTrash" else set())
            self.assertEqual(
                expected_layers,
                {path.name for path in root.iterdir() if path.is_dir()},
            )

        # TS-099 added a read-only inspector; physical publication remains gated.
        infrastructure = CORE_ROOTS[1] / "Infrastructure"
        self.assertEqual(
            {"IsolatedMediaPackageInspector.cs", "MediaPackageFileHasher.cs",
             "MediaPackageManifestReader.cs", "MediaPackagePathBoundary.cs"},
            {path.relative_to(infrastructure).as_posix() for path in infrastructure.rglob("*") if path.is_file()},
        )
        hasher = (infrastructure / "MediaPackageFileHasher.cs").read_text(encoding="utf-8")
        self.assertIn("FileMode.Open,", hasher)
        self.assertIn("FileAccess.Read,", hasher)
        physical_source = read_csharp((infrastructure,))
        self.assertNotRegex(physical_source, r"\bFileAccess\.(?:Write|ReadWrite)\b")
        self.assertNotRegex(physical_source, r"\bFileMode\.(?:Create|CreateNew|OpenOrCreate|Truncate|Append)\b")
        self.assertNotRegex(physical_source, r"\b(?:File|Directory)\.(?:Write\w*|Append\w*|Create\w*|Copy|Move|Delete|Replace|Set\w*)\s*\(")

        source = read_csharp(tuple(root / layer for root in CORE_ROOTS for layer in sorted(allowed_layers)))
        for forbidden in (
            "System.IO",
            "FileStream",
            "FileInfo",
            "DirectoryInfo",
            "SandboxFixture",
            "SandboxPathBoundary",
            "SandboxTransferPayloadPort",
            "SandboxOperationExecutorPort",
            "sandbox-storage",
            ".runtime",
        ):
            self.assertNotIn(forbidden, source)
        self.assertIsNone(re.search(r"\b(?:File|Directory)\.", source))
        self.assertIsNone(re.search(r"(?i)[\"'](?:[a-z]:\\\\|\\\\\\\\)", source))

    def test_physical_adapters_are_test_only_and_solution_locked(self) -> None:
        project = TEST_ROOT / "AssetLibrary.TransferOperation.Tests.csproj"
        project_text = project.read_text(encoding="utf-8")
        self.assertIn("<IsTestProject>true</IsTestProject>", project_text)
        self.assertTrue((TEST_ROOT / "packages.lock.json").is_file())

        projects = {
            element.get("Path")
            for element in ET.parse(ROOT / "AssetLibrary.slnx").iter()
            if element.tag.rsplit("}", 1)[-1] == "Project"
        }
        self.assertIn(
            "tests/dotnet/AssetLibrary.TransferOperation.Tests/AssetLibrary.TransferOperation.Tests.csproj",
            projects,
        )

        for adapter in (
            "SandboxTransferPayloadPort.cs",
            "SandboxOperationExecutorPort.cs",
            "SandboxOperationPreflightPort.cs",
            "SandboxRecovery.cs",
        ):
            self.assertTrue((TEST_ROOT / adapter).is_file(), adapter)

        production = read_csharp((ROOT / "services",))
        for adapter_type in (
            "SandboxTransferPayloadPort",
            "SandboxOperationExecutorPort",
            "SandboxOperationPreflightPort",
            "SandboxRecovery",
        ):
            self.assertNotIn(adapter_type, production)

        for service_project in (ROOT / "services").rglob("*.csproj"):
            self.assertNotIn(
                "AssetLibrary.TransferOperation.Tests",
                service_project.read_text(encoding="utf-8"),
                service_project.as_posix(),
            )

    def test_test_adapter_has_one_task_owned_write_boundary(self) -> None:
        boundary = (TEST_ROOT / "SandboxPathBoundary.cs").read_text(encoding="utf-8")
        fixture = (TEST_ROOT / "SandboxFixture.cs").read_text(encoding="utf-8")
        adapter_source = "\n".join(
            path.read_text(encoding="utf-8")
            for path in sorted(TEST_ROOT.glob("Sandbox*.cs"))
            if not path.name.endswith("Tests.cs")
        )

        for marker in ('".runtime"', '"sandbox-storage"', '"V01-007"'):
            self.assertIn(marker, boundary + fixture)
        self.assertIn("MarkerValue", fixture)
        self.assertIn("EnsureNoReparse", boundary)
        self.assertIn("IsStrictChild", boundary)
        self.assertNotIn("Path.GetTempPath", adapter_source)
        self.assertNotIn("Environment.GetEnvironmentVariable", adapter_source)
        self.assertIsNone(
            re.search(r"(?i)[\"'](?:[a-z]:\\\\|\\\\\\\\)", adapter_source)
        )

        task = (ROOT / ".codex/tasks/V01-007.md").read_text(encoding="utf-8")
        self.assertIn(".runtime/sandbox-storage/V01-007/**", task)
        self.assertIn("任何生产文件系统", task)

    def test_m0_006_write_gates_remain_fail_closed(self) -> None:
        contract = json.loads(
            (ROOT / "tests/architecture/m0-gates.json").read_text(encoding="utf-8")
        )
        gates = {gate["id"]: gate for gate in contract["gates"]}

        for gate_id in ("M0-006-G1", "M0-006-G2", "M0-006-G3"):
            self.assertEqual("open", gates[gate_id]["status"])
        self.assertFalse(gates["M0-006-G2"]["default_enabled"])
        self.assertFalse(gates["M0-006-G3"]["default_enabled"])
        self.assertIn(
            "production-file-writes",
            gates["M0-006-G2"]["blocked_targets"],
        )


if __name__ == "__main__":
    unittest.main()
