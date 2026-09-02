from __future__ import annotations

import importlib.util
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def load_script(name: str):
    path = ROOT / "scripts" / f"{name}.py"
    specification = importlib.util.spec_from_file_location(name, path)
    if specification is None or specification.loader is None:
        raise RuntimeError(f"cannot load {name}")
    module = importlib.util.module_from_spec(specification)
    sys.modules[specification.name] = module
    specification.loader.exec_module(module)
    return module


dependency_policy = load_script("validate_assetlink_sdk_dependencies")
source_policy = load_script("validate_assetlink_sdk_source")


def copy_sdk_fixture(destination: Path) -> None:
    shutil.copytree(
        ROOT / "packages" / "sdk" / "assetlink",
        destination / "packages/sdk/assetlink",
        ignore=shutil.ignore_patterns(".gradle", "bin", "build", "dist", "node_modules", "obj"),
    )
    shutil.copytree(ROOT / "eng", destination / "eng")


class AssetLinkSdkPolicyTests(unittest.TestCase):
    def test_committed_sdk_dependencies_and_source_pass(self) -> None:
        self.assertEqual([], dependency_policy.validate_dependencies(ROOT))
        self.assertEqual([], source_policy.validate_source(ROOT))

    def test_source_policy_rejects_handwritten_extra_and_network_source(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary)
            copy_sdk_fixture(fixture)
            extra = fixture / "packages/sdk/assetlink/typescript/src/network.ts"
            extra.write_text('import "node:http";\n', encoding="utf-8")

            errors = source_policy.validate_source(fixture)

            self.assertTrue(any("unexpected SDK source" in error for error in errors))

    def test_source_policy_rejects_sensitive_logging_and_business_policy(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary)
            copy_sdk_fixture(fixture)
            generated = fixture / "packages/sdk/assetlink/typescript/src/generated.ts"
            generated.write_text(
                generated.read_text(encoding="utf-8")
                + '\nconsole.log("authorization token");\nclass TransferStateMachine {}\n',
                encoding="utf-8",
            )

            errors = source_policy.validate_source(fixture)

            self.assertTrue(any("sensitive logging" in error for error in errors))
            self.assertTrue(any("business policy" in error for error in errors))

    def test_dependency_policy_rejects_version_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary)
            copy_sdk_fixture(fixture)
            package = fixture / "packages/sdk/assetlink/typescript/package.json"
            package.write_text(package.read_text(encoding="utf-8").replace("6.0.3", "6.0.4"), encoding="utf-8")

            errors = dependency_policy.validate_dependencies(fixture)

            self.assertTrue(any("TypeScript 6.0.3" in error for error in errors))

    def test_dependency_policy_rejects_missing_gradle_integrity(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary)
            copy_sdk_fixture(fixture)
            metadata = fixture / "packages/sdk/assetlink/kotlin/gradle/verification-metadata.xml"
            text = metadata.read_text(encoding="utf-8")
            metadata.write_text(text.replace("<sha256 ", "<ignored-sha256 ", 1), encoding="utf-8")

            errors = dependency_policy.validate_dependencies(fixture)

            self.assertTrue(any("lacks SHA-256" in error for error in errors))

    def test_required_build_artifacts_fail_when_missing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary)
            copy_sdk_fixture(fixture)

            errors = dependency_policy.validate_dependencies(fixture, require_build_artifacts=True)

            self.assertEqual(3, sum("required SDK build artifact is missing" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
