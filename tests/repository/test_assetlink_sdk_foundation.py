from __future__ import annotations

import hashlib
import json
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SDK_ROOT = ROOT / "packages" / "sdk" / "assetlink"


class AssetLinkSdkFoundationTests(unittest.TestCase):
    def test_generation_manifest_hashes_every_declared_output(self) -> None:
        manifest = json.loads((SDK_ROOT / "generation-manifest.json").read_text(encoding="utf-8"))

        self.assertEqual(1, manifest["generator_version"])
        self.assertEqual(21, manifest["schema_count"])
        self.assertEqual(20, manifest["message_count"])
        self.assertEqual(6, len(manifest["generated_files"]))
        self.assertEqual(20, len(manifest["messages"]))
        for relative, expected in manifest["generated_files"].items():
            actual = hashlib.sha256((SDK_ROOT / relative).read_bytes()).hexdigest()
            self.assertEqual(expected, actual, relative)

    def test_three_targets_share_contract_digest_and_lf_policy(self) -> None:
        stamps = {
            (SDK_ROOT / target / ".contract-source.sha256").read_text(encoding="utf-8").strip()
            for target in ("dotnet", "typescript", "kotlin")
        }
        self.assertEqual(1, len(stamps))
        self.assertRegex(next(iter(stamps)), r"^[0-9a-f]{64}$")

        attributes = (ROOT / ".gitattributes").read_text(encoding="utf-8")
        self.assertIn("contracts/assetlink/** text eol=lf", attributes)
        self.assertIn("packages/sdk/assetlink/** text eol=lf", attributes)
        self.assertIn("packages/sdk/assetlink/kotlin/gradle/wrapper/gradle-wrapper.jar binary", attributes)

    def test_dotnet_sdk_and_tests_are_in_solution_with_locks(self) -> None:
        projects = {
            element.get("Path")
            for element in ET.parse(ROOT / "AssetLibrary.slnx").iter()
            if element.tag.rsplit("}", 1)[-1] == "Project"
        }
        self.assertIn("packages/sdk/assetlink/dotnet/AssetLibrary.AssetLink.csproj", projects)
        self.assertIn("tests/dotnet/AssetLibrary.AssetLink.Tests/AssetLibrary.AssetLink.Tests.csproj", projects)
        self.assertTrue((SDK_ROOT / "dotnet/packages.lock.json").is_file())
        self.assertTrue((ROOT / "tests/dotnet/AssetLibrary.AssetLink.Tests/packages.lock.json").is_file())

    def test_native_manifests_are_exact_and_fail_closed(self) -> None:
        package = json.loads((SDK_ROOT / "typescript/package.json").read_text(encoding="utf-8"))
        self.assertEqual("pnpm@11.19.0", package["packageManager"])
        self.assertEqual({"typescript": "6.0.3"}, package["devDependencies"])
        self.assertNotIn("dependencies", package)

        kotlin_build = (SDK_ROOT / "kotlin/build.gradle.kts").read_text(encoding="utf-8")
        self.assertIn('kotlin("jvm") version "2.3.20"', kotlin_build)
        self.assertIn("kotlinx-serialization-json:1.11.0", kotlin_build)
        wrapper = (SDK_ROOT / "kotlin/gradle/wrapper/gradle-wrapper.properties").read_text(encoding="utf-8")
        self.assertIn("gradle-9.3.1-bin.zip", wrapper)
        self.assertRegex(wrapper, r"distributionSha256Sum=[0-9a-f]{64}")

    def test_fast_merge_runs_all_sdk_gates_on_both_operating_systems(self) -> None:
        contract = json.loads((ROOT / "tests/architecture/ci-tiers.json").read_text(encoding="utf-8"))
        tier = next(item for item in contract["tiers"] if item["id"] == "fast-merge")
        commands = "\n".join(tier["commands"])
        for marker in (
            "generate_assetlink_sdks.py --check",
            "tests/sdk",
            "tests/spikes/assetlink",
            "install --frozen-lockfile",
            "run lint",
            "run typecheck",
            "run test",
            "audit --audit-level low",
            "--dependency-verification strict build",
            "validate_assetlink_sdk_dependencies.py --require-build-artifacts",
            "validate_assetlink_sdk_source.py",
        ):
            self.assertIn(marker, commands)
        self.assertEqual(["ubuntu-latest", "windows-latest"], tier["runners"])

        workflow = (ROOT / ".github/workflows/handoff-quality.yml").read_text(encoding="utf-8")
        for marker in (
            "node-version: '24.20.0'",
            "version: '11.19.0'",
            "java-version: '21.0.12+8.0.LTS'",
            "gradle/actions/setup-gradle@v4",
            "matrix.label == 'ubuntu'",
            "matrix.label == 'windows'",
        ):
            self.assertIn(marker, workflow)
        self.assertIsNone(re.search(r"continue-on-error\s*:\s*true", workflow, flags=re.IGNORECASE))
        platform_workflow = (ROOT / ".github/workflows/platform-quality.yml").read_text(encoding="utf-8")
        self.assertIn("java-version: '21.0.12+8.0.LTS'", platform_workflow)


if __name__ == "__main__":
    unittest.main()
