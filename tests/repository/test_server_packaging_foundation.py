from __future__ import annotations

import importlib.util
import json
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def load_script(name: str):
    path = ROOT / "scripts" / f"{name}.py"
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(module)
    return module


VALIDATOR = load_script("validate_server_release")


class ServerPackagingFoundationTests(unittest.TestCase):
    def test_release_definition_policy_passes_without_claiming_platform_execution(self) -> None:
        errors, report = VALIDATOR.validate_repository(ROOT)

        self.assertEqual(errors, [])
        self.assertEqual(report["status"], "passed")
        self.assertEqual(set(report["platform_evidence"].values()), {"blocked_missing_environment"})

    def test_one_host_wrapper_references_the_inert_core(self) -> None:
        host = ET.parse(
            ROOT / "services/core-server/Host/AssetLibrary.CoreServer.Host.csproj"
        ).getroot()
        core = ET.parse(ROOT / "services/core-server/AssetLibrary.CoreServer.csproj").getroot()
        host_references = [
            element.attrib.get("Include", "").replace("\\", "/")
            for element in host.iter()
            if element.tag.endswith("ProjectReference")
        ]
        core_output = [
            (element.text or "").strip()
            for element in core.iter()
            if element.tag.endswith("OutputType")
        ]

        self.assertEqual(host_references, ["../AssetLibrary.CoreServer.csproj"])
        self.assertEqual(core_output, [])
        self.assertIn(
            '<Compile Remove="Host\\**\\*.cs" />',
            (ROOT / "services/core-server/AssetLibrary.CoreServer.csproj").read_text(encoding="utf-8"),
        )

    def test_release_workflow_executes_real_native_and_docker_commands(self) -> None:
        workflow = (ROOT / ".github/workflows/release-evidence.yml").read_text(encoding="utf-8")

        for command in (
            "scripts/validate_server_release.py",
            "scripts/build_server_release.py",
            "tests/release/run_docker_evidence.py --execute",
            "scripts/validate_server_release.py --require-artifacts",
            "actions/upload-artifact@v4",
            "windows-latest",
            "ubuntu-latest",
        ):
            self.assertIn(command, workflow)
        self.assertNotRegex(workflow.lower(), r"continue-on-error\s*:\s*true")

    def test_release_rid_locks_are_sandboxed_and_neutral_locks_stay_portable(self) -> None:
        build_props = ET.parse(ROOT / "Directory.Build.props").getroot()
        release_lock_routes = [
            element
            for element in build_props.iter()
            if element.tag.endswith("NuGetLockFilePath")
        ]

        self.assertEqual(len(release_lock_routes), 1)
        self.assertIn("AssetLibraryReleaseLockRoot", release_lock_routes[0].attrib["Condition"])
        self.assertIn("RuntimeIdentifier", release_lock_routes[0].attrib["Condition"])
        self.assertIn(
            "$(MSBuildProjectName).$(RuntimeIdentifier).lock.json",
            release_lock_routes[0].text or "",
        )
        for relative in (
            "packages/sdk/assetlink/dotnet/packages.lock.json",
            "services/core-server/packages.lock.json",
            "services/core-server/Host/packages.lock.json",
        ):
            lock = json.loads((ROOT / relative).read_text(encoding="utf-8"))
            self.assertEqual(set(lock["dependencies"]), {"net10.0"})

    def test_privileged_scripts_are_read_only_by_default_and_owner_guarded(self) -> None:
        windows = (ROOT / "infra/windows-server/service-evidence.ps1").read_text(encoding="utf-8")
        linux = (ROOT / "infra/linux-server/systemd-evidence.sh").read_text(encoding="utf-8")
        docker = (ROOT / "tests/release/run_docker_evidence.py").read_text(encoding="utf-8")

        self.assertIn("[string]$Action = 'preflight'", windows)
        self.assertIn("[switch]$ApproveSystemChanges", windows)
        self.assertIn("AssetLibraryEvidenceOwner", windows)
        self.assertIn('action="${1:-preflight}"', linux)
        self.assertIn("ASSETLIBRARY_APPROVE_SYSTEM_CHANGES", linux)
        self.assertIn("--execute", docker)
        self.assertIn("explicit_execution_not_requested", docker)

    def test_release_and_production_write_gates_remain_open(self) -> None:
        ledger = json.loads((ROOT / "tests/architecture/m0-gates.json").read_text(encoding="utf-8"))
        statuses = {gate["id"]: gate["status"] for gate in ledger["gates"]}

        for gate_id in ("M0-004-G1", "M0-004-G2", "M0-006-G1", "M0-006-G2", "M0-006-G3"):
            self.assertEqual(statuses[gate_id], "open")


if __name__ == "__main__":
    unittest.main()
