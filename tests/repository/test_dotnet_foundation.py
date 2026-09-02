from __future__ import annotations

import base64
import importlib.util
import json
import tempfile
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


DEPENDENCIES = load_script("validate_dotnet_dependencies")
SOURCE_POLICY = load_script("validate_dotnet_source")


class DotnetSourcePolicyTests(unittest.TestCase):
    def write(self, root: Path, relative: str, content: str) -> Path:
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def test_repository_source_passes(self) -> None:
        self.assertEqual(SOURCE_POLICY.validate_source(ROOT), [])

    def test_duplicate_block_fails(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            repeated = """
namespace Fixture;
public static class Repeated
{
    public static int Calculate(int value)
    {
        var total = value;
        total += value;
        total += value;
        total += value;
        total += value;
        total += value;
        total += value;
        total += value;
        total += value;
        total += value;
        total += value;
        return total;
    }
}
"""
            self.write(root, "services/core-server/First.cs", repeated)
            self.write(root, "services/worker-supervisor/Second.cs", repeated)

            errors = SOURCE_POLICY.validate_source(root, window_size=40)

            self.assertTrue(any("duplicated C# token block" in error for error in errors), errors)

    def test_sensitive_log_fails(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.write(
                root,
                "services/core-server/UnsafeLog.cs",
                """
namespace Fixture;
public static class UnsafeLog
{
    public static void Write(dynamic logger, string accessToken)
    {
        logger.LogInformation("Access token: {AccessToken}", accessToken);
    }
}
""",
            )

            errors = SOURCE_POLICY.validate_source(root)

            self.assertTrue(any("possible sensitive logging" in error for error in errors), errors)


class DotnetFoundationContractTests(unittest.TestCase):
    def test_sdk_and_build_properties_are_fail_closed(self) -> None:
        sdk = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))["sdk"]
        self.assertEqual(sdk["version"], "10.0.111")
        self.assertEqual(sdk["rollForward"], "disable")
        self.assertFalse(sdk["allowPrerelease"])

        properties = ET.parse(ROOT / "Directory.Build.props")
        values = {
            element.tag.rsplit("}", 1)[-1]: (element.text or "").strip()
            for element in properties.iter()
        }
        self.assertEqual(values["TargetFramework"], "net10.0")
        self.assertEqual(values["LangVersion"], "14.0")
        self.assertEqual(values["TreatWarningsAsErrors"], "true")
        self.assertEqual(values["RestorePackagesWithLockFile"], "true")
        self.assertEqual(values["DisableImplicitNuGetFallbackFolder"], "true")
        self.assertEqual(values["NuGetAuditMode"], "all")

        attributes = (ROOT / ".gitattributes").read_text(encoding="utf-8")
        for pattern in ("*.cs", "*.csproj", "*.props", "*.slnx"):
            self.assertIn(f"{pattern} text eol=lf", attributes)

    def test_fast_merge_executes_every_dotnet_foundation_gate(self) -> None:
        contract = json.loads(
            (ROOT / "tests/architecture/ci-tiers.json").read_text(encoding="utf-8")
        )
        fast_merge = next(tier for tier in contract["tiers"] if tier["id"] == "fast-merge")
        commands = "\n".join(fast_merge["commands"])
        for marker in (
            "dotnet restore AssetLibrary.slnx --locked-mode",
            "dotnet format AssetLibrary.slnx --verify-no-changes --no-restore",
            "dotnet build AssetLibrary.slnx --configuration Release --no-restore",
            "dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore",
            "dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable",
            "validate_dotnet_dependencies.py",
            "validate_dotnet_source.py",
        ):
            self.assertIn(marker, commands)
        self.assertTrue(contract["future_native_gate_activation"]["dotnet"].startswith("Active"))

        workflow = (ROOT / ".github/workflows/handoff-quality.yml").read_text(encoding="utf-8")
        self.assertIn("actions/setup-dotnet@v4", workflow)
        self.assertIn("cache: true", workflow)
        self.assertIn("ubuntu-latest", workflow)
        self.assertIn("windows-latest", workflow)
        self.assertEqual(fast_merge["runners"], ["ubuntu-latest", "windows-latest"])
        self.assertNotRegex(workflow.lower(), r"continue-on-error\s*:\s*true")


class DotnetDependencyPolicyTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        project = self.root / "services/core-server/Fixture.csproj"
        project.parent.mkdir(parents=True)
        project.write_text(
            """<Project Sdk=\"Microsoft.NET.Sdk\">
  <ItemGroup><PackageReference Include=\"Example.Package\" /></ItemGroup>
</Project>
""",
            encoding="utf-8",
        )
        (self.root / "AssetLibrary.slnx").write_text(
            """<Solution>
  <Project Path=\"services/core-server/Fixture.csproj\" />
</Solution>
""",
            encoding="utf-8",
        )
        (self.root / "Directory.Packages.props").write_text(
            """<Project>
<PropertyGroup>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  <CentralPackageVersionOverrideEnabled>false</CentralPackageVersionOverrideEnabled>
</PropertyGroup>
<ItemGroup>
  <PackageVersion Include=\"Example.Package\" Version=\"1.2.3\" />
</ItemGroup></Project>
""",
            encoding="utf-8",
        )
        (self.root / "NuGet.config").write_text(
            """<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
""",
            encoding="utf-8",
        )
        self.lock_path = project.parent / "packages.lock.json"
        self.lock_path.write_text(
            json.dumps(
                {
                    "version": 2,
                    "dependencies": {
                        "net10.0": {
                            "Example.Package": {
                                "type": "Direct",
                                "requested": "[1.2.3, )",
                                "resolved": "1.2.3",
                                "contentHash": base64.b64encode(b"x" * 64).decode("ascii"),
                            }
                        }
                    },
                }
            ),
            encoding="utf-8",
        )
        self.policy_path = self.root / "eng/dotnet-dependency-policy.json"
        self.policy_path.parent.mkdir(parents=True)
        self.policy_path.write_text(
            json.dumps(
                {
                    "version": 1,
                    "approved_sources": ["https://api.nuget.org/v3/index.json"],
                    "allowed_license_expressions": ["MIT"],
                    "license_file_exceptions": [],
                }
            ),
            encoding="utf-8",
        )
        self.packages_dir = self.root / "packages"
        package_root = self.packages_dir / "example.package/1.2.3"
        package_root.mkdir(parents=True)
        self.nuspec = package_root / "example.package.nuspec"
        self.nuspec.write_text(
            """<package><metadata>
  <id>Example.Package</id><version>1.2.3</version>
  <license type=\"expression\">MIT</license>
</metadata></package>
""",
            encoding="utf-8",
        )
        self.report_path = self.root / "vulnerabilities.json"
        self.report = {
            "version": 1,
            "parameters": "--vulnerable --include-transitive",
            "sources": ["https://api.nuget.org/v3/index.json"],
            "projects": [{"path": str(project)}],
        }
        self.write_report()

    def write_report(self) -> None:
        self.report_path.write_text(json.dumps(self.report), encoding="utf-8")

    def validate(self):
        return DEPENDENCIES.validate(
            self.root,
            self.root / "AssetLibrary.slnx",
            self.policy_path,
            self.packages_dir,
            self.report_path,
        )

    def test_locked_approved_dependency_passes(self) -> None:
        errors, projects, packages = self.validate()

        self.assertEqual(errors, [])
        self.assertEqual(projects, 1)
        self.assertEqual(packages, 1)

    def test_direct_version_drift_fails(self) -> None:
        lock = json.loads(self.lock_path.read_text(encoding="utf-8"))
        lock["dependencies"]["net10.0"]["Example.Package"]["resolved"] = "1.2.4"
        self.lock_path.write_text(json.dumps(lock), encoding="utf-8")

        errors, _, _ = self.validate()

        self.assertTrue(any("direct dependency drift" in error for error in errors), errors)

    def test_unapproved_configured_source_fails(self) -> None:
        (self.root / "NuGet.config").write_text(
            """<configuration><packageSources><clear />
  <add key="other" value="https://packages.example.invalid/v3/index.json" />
</packageSources></configuration>
""",
            encoding="utf-8",
        )

        errors, _, _ = self.validate()

        self.assertTrue(any("sources differ" in error for error in errors), errors)

    def test_invalid_content_hash_fails(self) -> None:
        lock = json.loads(self.lock_path.read_text(encoding="utf-8"))
        lock["dependencies"]["net10.0"]["Example.Package"]["contentHash"] = "not-a-hash"
        self.lock_path.write_text(json.dumps(lock), encoding="utf-8")

        errors, _, _ = self.validate()

        self.assertTrue(any("valid SHA-512 content hash" in error for error in errors), errors)

    def test_reported_transitive_vulnerability_fails(self) -> None:
        self.report["projects"][0]["frameworks"] = [
            {
                "framework": "net10.0",
                "transitivePackages": [
                    {
                        "id": "Example.Package",
                        "resolvedVersion": "1.2.3",
                        "vulnerabilities": [{"severity": "High", "advisoryurl": "https://example.invalid"}],
                    }
                ],
            }
        ]
        self.write_report()

        errors, _, _ = self.validate()

        self.assertTrue(any("vulnerable dependency" in error for error in errors), errors)

    def test_unapproved_license_fails(self) -> None:
        self.nuspec.write_text(
            """<package><metadata>
  <id>Example.Package</id><version>1.2.3</version>
  <license type=\"expression\">GPL-3.0-only</license>
</metadata></package>
""",
            encoding="utf-8",
        )

        errors, _, _ = self.validate()

        self.assertTrue(any("unapproved license expression" in error for error in errors), errors)


if __name__ == "__main__":
    unittest.main()
