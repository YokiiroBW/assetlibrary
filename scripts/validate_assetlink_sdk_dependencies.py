#!/usr/bin/env python3
"""Validate frozen AssetLink SDK dependencies, integrity metadata, licenses, and size budgets."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
POLICY_PATH = "eng/assetlink-sdk-dependency-policy.json"
WRAPPER_SHA256 = "b3a875ddc1f044746e1b1a55f645584505f4a10438c1afea9f15e92a7c42ec13"
GRADLE_DISTRIBUTION_SHA256 = "b266d5ff6b90eada6dc3b20cb090e3731302e553a27c5d3e4df1f0d76beaff06"
EXPECTED_VERSIONS = {
    "Node.js": "24.20.0",
    "pnpm": "11.19.0",
    "typescript": "6.0.3",
    "Eclipse Temurin": "21.0.12+8",
    "Gradle": "9.3.1",
    "Kotlin JVM Gradle plugin": "2.3.20",
    "kotlinx-serialization-json": "1.11.0",
}
GRADLE_LICENSE_GROUPS = {
    "com.google.code.gson": "Apache-2.0",
    "com.google.errorprone": "Apache-2.0",
    "io.github.java-diff-utils": "Apache-2.0",
    "org.jetbrains": "Apache-2.0",
    "org.jetbrains.kotlin": "Apache-2.0",
    "org.jetbrains.kotlin.jvm": "Apache-2.0",
    "org.jetbrains.kotlinx": "Apache-2.0",
}


def load_json(path: Path, errors: list[str]) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        errors.append(f"cannot load JSON {path}: {exc}")
        return {}
    if not isinstance(value, dict):
        errors.append(f"JSON document must be an object: {path}")
        return {}
    return value


def validate_policy(root: Path, errors: list[str]) -> dict:
    policy = load_json(root / POLICY_PATH, errors)
    dependencies = policy.get("dependencies", [])
    actual = {
        entry.get("name"): entry
        for entry in dependencies
        if isinstance(entry, dict) and isinstance(entry.get("name"), str)
    }
    if set(actual) != set(EXPECTED_VERSIONS):
        errors.append("SDK dependency policy must list the complete approved direct tool/dependency set")
    allowed = set(policy.get("allowed_licenses", []))
    for name, version in EXPECTED_VERSIONS.items():
        entry = actual.get(name, {})
        if entry.get("version") != version:
            errors.append(f"SDK dependency policy version mismatch for {name}")
        if entry.get("license") not in allowed:
            errors.append(f"SDK dependency policy has an unapproved license for {name}")
        if not str(entry.get("security_updates", "")).startswith("https://"):
            errors.append(f"SDK dependency policy lacks an HTTPS security update source for {name}")
    if not isinstance(policy.get("size_budgets_bytes"), dict):
        errors.append("SDK dependency policy lacks size budgets")
    license_groups = policy.get("gradle_component_license_groups")
    if license_groups != GRADLE_LICENSE_GROUPS:
        errors.append("SDK dependency policy must record the complete reviewed Gradle license-group mapping")
    elif any(license not in allowed for license in license_groups.values()):
        errors.append("SDK dependency policy maps a Gradle component group to an unapproved license")
    return policy


def validate_typescript(root: Path, errors: list[str]) -> None:
    base = root / "packages" / "sdk" / "assetlink" / "typescript"
    package = load_json(base / "package.json", errors)
    if package.get("packageManager") != "pnpm@11.19.0":
        errors.append("TypeScript SDK must pin pnpm 11.19.0")
    if package.get("engines", {}).get("node") != ">=24.0.0 <25":
        errors.append("TypeScript SDK must constrain builds to Node.js 24 LTS")
    if package.get("devDependencies") != {"typescript": "6.0.3"}:
        errors.append("TypeScript SDK must have only exact TypeScript 6.0.3 as a development dependency")
    if package.get("dependencies"):
        errors.append("TypeScript SDK must not have runtime npm dependencies")
    try:
        lock = (base / "pnpm-lock.yaml").read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        errors.append(f"cannot read TypeScript SDK lockfile: {exc}")
        return
    if lock.count("typescript@6.0.3:") != 2:
        errors.append("pnpm lock must contain exactly the package and snapshot records for TypeScript 6.0.3")
    if "sha512-" not in lock or re.search(r"\b(?:http|git)[:+]", lock, flags=re.IGNORECASE):
        errors.append("pnpm lock must retain registry integrity and contain no non-registry source")


def validate_dotnet(root: Path, errors: list[str]) -> None:
    base = root / "packages" / "sdk" / "assetlink" / "dotnet"
    project = (base / "AssetLibrary.AssetLink.csproj").read_text(encoding="utf-8")
    if "PackageReference" in project:
        errors.append(".NET AssetLink runtime SDK must remain BCL-only")
    lock = load_json(base / "packages.lock.json", errors)
    frameworks = lock.get("dependencies", {})
    if frameworks != {"net10.0": {}}:
        errors.append(".NET AssetLink runtime lock must contain no NuGet dependencies")


def validate_kotlin(root: Path, policy: dict, errors: list[str]) -> None:
    base = root / "packages" / "sdk" / "assetlink" / "kotlin"
    build = (base / "build.gradle.kts").read_text(encoding="utf-8")
    for literal in ('version "2.3.20"', 'kotlinx-serialization-json:1.11.0', "jvmToolchain(21)"):
        if literal not in build:
            errors.append(f"Kotlin SDK build lacks frozen setting: {literal}")
    if re.search(r'"[^"\n]*(?:latest|SNAPSHOT|\+)[^"\n]*"', build, flags=re.IGNORECASE):
        errors.append("Kotlin SDK build contains a dynamic or snapshot dependency")

    properties = (base / "gradle" / "wrapper" / "gradle-wrapper.properties").read_text(encoding="utf-8")
    if "gradle-9.3.1-bin.zip" not in properties or f"distributionSha256Sum={GRADLE_DISTRIBUTION_SHA256}" not in properties:
        errors.append("Gradle wrapper must pin the verified 9.3.1 binary distribution")
    wrapper = base / "gradle" / "wrapper" / "gradle-wrapper.jar"
    if not wrapper.is_file() or hashlib.sha256(wrapper.read_bytes()).hexdigest() != WRAPPER_SHA256:
        errors.append("Gradle wrapper JAR does not match the official 9.3.1 SHA-256")
    for script in (base / "gradlew", base / "gradlew.bat"):
        if not script.is_file():
            errors.append(f"Gradle wrapper script is missing: {script.name}")

    lock_path = base / "gradle.lockfile"
    try:
        lock_lines = lock_path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exc:
        errors.append(f"cannot read Gradle lockfile: {exc}")
        lock_lines = []
    coordinates = [line.split("=", 1)[0] for line in lock_lines if line and not line.startswith(("#", "empty="))]
    if not coordinates:
        errors.append("Gradle lockfile must contain the resolved dependency closure")
    reviewed_groups = policy.get("gradle_component_license_groups", {})
    for coordinate in coordinates:
        parts = coordinate.split(":")
        if len(parts) != 3 or parts[0] not in reviewed_groups:
            errors.append(f"Gradle lock contains an unreviewed component: {coordinate}")
    for required in (
        "org.jetbrains.kotlin:kotlin-stdlib:2.3.20",
        "org.jetbrains.kotlinx:kotlinx-serialization-json:1.11.0",
    ):
        if required not in coordinates:
            errors.append(f"Gradle lock lacks required component: {required}")

    metadata_path = base / "gradle" / "verification-metadata.xml"
    try:
        tree = ET.parse(metadata_path)
    except (OSError, ET.ParseError) as exc:
        errors.append(f"cannot read Gradle verification metadata: {exc}")
        return
    namespace = {"v": "https://schema.gradle.org/dependency-verification"}
    components = tree.findall(".//v:component", namespace)
    if not components:
        errors.append("Gradle verification metadata must contain resolved components")
    for component in components:
        group = component.attrib.get("group", "")
        if group not in reviewed_groups:
            errors.append(f"Gradle verification metadata has an unreviewed license group: {group}")
        artifacts = component.findall("v:artifact", namespace)
        if not artifacts or any(artifact.find("v:sha256", namespace) is None for artifact in artifacts):
            coordinate = ":".join(component.attrib.get(key, "") for key in ("group", "name", "version"))
            errors.append(f"Gradle component lacks SHA-256 verification: {coordinate}")


def total_size(path: Path) -> int | None:
    if path.is_file():
        return path.stat().st_size
    if path.is_dir():
        return sum(item.stat().st_size for item in path.rglob("*") if item.is_file())
    return None


def validate_build_sizes(root: Path, policy: dict, errors: list[str], required: bool) -> None:
    budgets = policy.get("size_budgets_bytes", {})
    artifacts = {
        "typescript_dist": root / "packages" / "sdk" / "assetlink" / "typescript" / "dist",
        "dotnet_release_dll": root
        / "packages"
        / "sdk"
        / "assetlink"
        / "dotnet"
        / "bin"
        / "Release"
        / "net10.0"
        / "AssetLibrary.AssetLink.dll",
        "kotlin_release_jar": root
        / "packages"
        / "sdk"
        / "assetlink"
        / "kotlin"
        / "build"
        / "libs"
        / "assetlibrary-assetlink-0.1.0.jar",
    }
    for name, path in artifacts.items():
        size = total_size(path)
        if size is None:
            if required:
                errors.append(f"required SDK build artifact is missing: {path.relative_to(root).as_posix()}")
            continue
        budget = budgets.get(name)
        if not isinstance(budget, int) or budget <= 0 or size > budget:
            errors.append(f"SDK build artifact exceeds or lacks size budget: {name} ({size} bytes)")


def validate_dependencies(root: Path, require_build_artifacts: bool = False) -> list[str]:
    root = root.resolve()
    errors: list[str] = []
    policy = validate_policy(root, errors)
    validate_dotnet(root, errors)
    validate_typescript(root, errors)
    validate_kotlin(root, policy, errors)
    validate_build_sizes(root, policy, errors, require_build_artifacts)
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--require-build-artifacts", action="store_true")
    args = parser.parse_args()
    errors = validate_dependencies(args.root, args.require_build_artifacts)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    suffix = " with size budgets" if args.require_build_artifacts else ""
    print(f"AssetLink SDK dependency, integrity, and license policy passed{suffix}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
