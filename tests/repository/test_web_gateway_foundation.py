from __future__ import annotations

import base64
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


DEPENDENCIES = load_script("validate_web_dependencies")
SOURCE_POLICY = load_script("validate_web_source")


class WebGatewayFoundationRepositoryTests(unittest.TestCase):
    def test_exact_manifest_matches_reviewed_dependency_policy(self) -> None:
        package = json.loads((ROOT / "apps/web/package.json").read_text(encoding="utf-8"))
        policy = json.loads(
            (ROOT / "eng/web-dependency-policy.json").read_text(encoding="utf-8")
        )

        self.assertEqual("pnpm@11.19.0", package["packageManager"])
        self.assertEqual(">=24.20.0 <25", package["engines"]["node"])
        self.assertEqual(policy["manifest"]["dependencies"], package["dependencies"])
        self.assertEqual(
            policy["manifest"]["devDependencies"], package["devDependencies"]
        )
        self.assertEqual(
            "link:../../packages/sdk/assetlink/typescript",
            package["dependencies"]["@assetlibrary/assetlink"],
        )
        self.assertEqual([], DEPENDENCIES.validate(ROOT))

    def test_lock_parser_enforces_integrity_and_allows_pnpm_version_deduping(self) -> None:
        digest = base64.b64encode(b"x" * 64).decode("ascii")
        lock = f"""lockfileVersion: '9.0'

packages:

  fsevents@2.3.2:
    resolution:
      {{ integrity: sha512-{digest} }}

  fsevents@2.3.3:
    resolution: {{ integrity: sha512-{digest} }}

snapshots:
"""

        self.assertEqual(
            {"2.3.2", "2.3.3"}, DEPENDENCIES.lock_packages(lock)["fsevents"]
        )

        invalid = lock.replace(digest, "eA==", 1)
        with self.assertRaisesRegex(ValueError, "invalid SHA-512 integrity"):
            DEPENDENCIES.lock_packages(invalid)

    def test_web_gateway_tests_and_locks_are_in_the_solution(self) -> None:
        projects = {
            element.get("Path")
            for element in ET.parse(ROOT / "AssetLibrary.slnx").iter()
            if element.tag.rsplit("}", 1)[-1] == "Project"
        }

        self.assertIn(
            "tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj",
            projects,
        )
        self.assertTrue(
            (ROOT / "tests/dotnet/AssetLibrary.WebGateway.Tests/packages.lock.json").is_file()
        )
        self.assertTrue((ROOT / "apps/web/pnpm-lock.yaml").is_file())

    def test_fast_merge_activates_every_web_gateway_gate(self) -> None:
        contract = json.loads(
            (ROOT / "tests/architecture/ci-tiers.json").read_text(encoding="utf-8")
        )
        tier = next(item for item in contract["tiers"] if item["id"] == "fast-merge")
        commands = "\n".join(tier["commands"])
        for marker in (
            "test_web_gateway_migrations.py",
            "apps/web install --frozen-lockfile",
            "apps/web run format:check",
            "apps/web run lint",
            "apps/web run typecheck",
            "apps/web run build",
            "playwright install --with-deps chromium",
            "apps/web run test:browser",
            "apps/web audit --audit-level low",
            "validate_web_dependencies.py --require-build-artifacts",
            "validate_web_source.py",
            "tests/integration/read-only-trial/run_e2e.py --execute --postgres-external",
            "test_worker_lifetime.py",
        ):
            self.assertIn(marker, commands)
        self.assertIn(
            "Chromium browser regressions",
            contract["future_native_gate_activation"]["typescript"],
        )

        workflow = (ROOT / ".github/workflows/handoff-quality.yml").read_text(
            encoding="utf-8"
        )
        for marker in (
            "node-version: '24.20.0'",
            "apps/web/pnpm-lock.yaml",
            "AssetLibrary.WebGateway.Tests/packages.lock.json",
            "PLAYWRIGHT_BROWSERS_PATH:",
            "ASSETLIBRARY_TEST_RUNTIME: ${{ github.workspace }}/.runtime/sandbox-storage/V01-006",
            "if: matrix.label == 'ubuntu'",
            "if: matrix.label == 'windows'",
        ):
            self.assertIn(marker, workflow)

    def test_repository_verification_runs_connection_free_web_gates(self) -> None:
        self.assertEqual([], SOURCE_POLICY.validate(ROOT))
        script = (ROOT / "scripts/verify_repository.py").read_text(encoding="utf-8")

        self.assertIn("validate_web_dependencies.py", script)
        self.assertIn("validate_web_source.py", script)
        self.assertNotIn("test_web_gateway_100k", script)

    def test_postgres_gateway_reads_use_explicit_read_only_transactions(self) -> None:
        infrastructure = ROOT / "services/core-server/Modules/GatewayAuth/Infrastructure"
        executor = (infrastructure / "PostgresReadExecutor.cs").read_text(encoding="utf-8")
        self.assertIn('"SET TRANSACTION READ ONLY;"', executor)

        for source_name in ("PostgresLibraryQuery.cs", "PostgresSearchQuery.cs"):
            source = (infrastructure / source_name).read_text(encoding="utf-8")
            self.assertIn("PostgresReadExecutor.ReadAsync(", source, source_name)

        browse = (infrastructure / "PostgresBrowseReader.cs").read_text(encoding="utf-8")
        self.assertIn("PostgresReadExecutor.BeginReadOnlyAsync(", browse)


if __name__ == "__main__":
    unittest.main()
