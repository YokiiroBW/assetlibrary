#!/usr/bin/env python3
"""Audit the exact Windows win-x64 release lock set against normal NuGet locks."""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
LOCK_DIRECTORY = Path("infra/windows-client/locks")
# NuGet normalizes the Windows TFM by dropping its final .0 component.
PROJECTS = {
    "AssetLibrary.Windows.Setup": ("apps/windows-client/Setup", "net10.0-windows7.0"),
    "AssetLibrary.Windows.AssetHost": ("apps/windows-client/AssetHost", "net10.0"),
    "AssetLibrary.Windows.Settings": ("apps/windows-client/Settings", "net10.0-windows10.0.26100"),
    "AssetLibrary.Windows.Session": ("apps/windows-client/Session", "net10.0"),
    "AssetLibrary.Windows.Core": ("apps/windows-client/Core", "net10.0"),
    "AssetLibrary.AssetLink": ("packages/sdk/assetlink/dotnet", "net10.0"),
}
PROJECT_IDS = {name.lower() for name in PROJECTS}
RID = "win-x64"

# File-based loading also works with Python -I, without adding an untrusted root
# to sys.path. Reuse the repository's exact version and SHA-512 validation.
_SPEC = importlib.util.spec_from_file_location(
    "windows_lock_dependency_helpers", Path(__file__).with_name("validate_dotnet_dependencies.py")
)
assert _SPEC is not None and _SPEC.loader is not None
DEPENDENCIES = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(DEPENDENCIES)


def unique_members(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON member: {key}")
        result[key] = value
    return result


def read_lock(path: Path, expected_groups: set[str]) -> dict[str, dict[str, tuple[str, str]]]:
    if not path.is_file():
        raise ValueError(f"missing lock: {path}")
    lock = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_members)
    if not isinstance(lock, dict) or type(lock.get("version")) is not int or lock["version"] != 2:
        raise ValueError(f"lock must use version 2: {path}")
    groups = lock.get("dependencies")
    if not isinstance(groups, dict) or set(groups) != expected_groups:
        raise ValueError(f"invalid target groups in {path}; expected {sorted(expected_groups)}")
    result = {}
    for group, records in groups.items():
        if not isinstance(records, dict):
            raise ValueError(f"invalid dependency group in {path}: {group}")
        packages = {}
        seen = set()
        for package, details in records.items():
            if not package or package.strip() != package or not isinstance(details, dict):
                raise ValueError(f"invalid locked dependency in {path}: {package}")
            key = package.lower()
            if key in seen:
                raise ValueError(f"duplicate package ID in {path}: {package}")
            seen.add(key)
            kind = details.get("type")
            if kind == "Project":
                if key not in PROJECT_IDS or "resolved" in details or "contentHash" in details:
                    raise ValueError(f"invalid project dependency in {path}: {package}")
                # Build Version legitimately changes project reference ranges.
                continue
            if kind not in {"Direct", "Transitive", "CentralTransitive"}:
                raise ValueError(f"unsupported lock type in {path}: {package}")
            version, digest = details.get("resolved"), details.get("contentHash")
            if not isinstance(version, str) or not DEPENDENCIES.EXACT_VERSION_RE.fullmatch(version):
                raise ValueError(f"package lacks an exact stable version in {path}: {package}")
            if not DEPENDENCIES.has_sha512_content_hash(digest):
                raise ValueError(f"package lacks a valid SHA-512 hash in {path}: {package}")
            packages[key] = (version, digest)
        result[group] = packages
    return result


def merge_packages(destination: dict[str, tuple[str, str]], groups: dict, label: str) -> None:
    for packages in groups.values():
        for key, package in packages.items():
            if key in destination and destination[key] != package:
                raise ValueError(f"package version/hash mismatch across {label}: {key}")
            destination[key] = package


def validate(
    root: Path,
    packages_dir: Path | None = None,
    policy_path: Path = Path("eng/dotnet-dependency-policy.json"),
) -> int:
    lock_directory = root / LOCK_DIRECTORY
    if not lock_directory.is_dir():
        raise ValueError(f"missing release lock directory: {lock_directory}")
    expected = {f"{project}.{RID}.lock.json" for project in PROJECTS}
    actual = {path.name for path in lock_directory.iterdir()}
    if actual != expected:
        raise ValueError(
            f"release lock set mismatch: missing={sorted(expected - actual)}, extra={sorted(actual - expected)}"
        )
    defaults = {}
    default_packages: dict[str, tuple[str, str]] = {}
    for project, (directory, framework) in PROJECTS.items():
        groups = {framework}
        if project == "AssetLibrary.Windows.Settings":
            groups.add(f"{framework}/{RID}")
        defaults[project] = read_lock(root / directory / "packages.lock.json", groups)
        merge_packages(default_packages, defaults[project], "normal locks")

    release_packages: dict[str, tuple[str, str]] = {}
    for project, (_, framework) in PROJECTS.items():
        released = read_lock(
            lock_directory / f"{project}.{RID}.lock.json", {framework, f"{framework}/{RID}"}
        )
        for group, packages in defaults[project].items():
            for key, baseline in packages.items():
                if key not in released[group]:
                    raise ValueError(f"normal package missing from release group: {project}/{group}/{key}")
                if released[group][key] != baseline:
                    raise ValueError(f"normal/release package version/hash mismatch: {project}/{group}/{key}")
        for packages in released.values():
            for key, package in packages.items():
                if key in default_packages and default_packages[key] != package:
                    raise ValueError(f"normal/release package version/hash mismatch: {project}/{key}")
        # RID-only build packages may be new, but must have valid hashes and be
        # consistent across every release lock. Optional license auditing below
        # consumes the complete release union, including these extra packages.
        merge_packages(release_packages, released, "release locks")
    if packages_dir is not None:
        policy = DEPENDENCIES.load_json(root / policy_path)
        if policy.get("version") != 1:
            raise ValueError("dependency policy must use version 1")
        cache = (root / packages_dir).resolve()
        if not cache.is_dir():
            raise ValueError(f"restored NuGet package directory is missing: {cache}")
        license_packages = {key: (key, version) for key, (version, _) in release_packages.items()}
        errors = DEPENDENCIES.validate_licenses(license_packages, cache, policy)
        if errors:
            raise ValueError("release package license audit failed: " + "; ".join(errors))
    return len(release_packages)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT, help="Repository root containing default and release locks")
    parser.add_argument("--packages-dir", type=Path, help="Audit licenses in this restored NuGet cache (relative to --root unless absolute)")
    parser.add_argument("--policy", type=Path, default=Path("eng/dotnet-dependency-policy.json"),
                        help="License policy relative to --root unless absolute; used only with --packages-dir")
    args = parser.parse_args()
    try:
        packages = validate(args.root.resolve(), args.packages_dir, args.policy)
    except (OSError, UnicodeError, ValueError, ET.ParseError) as error:
        print(f"WINDOWS_RELEASE_LOCKS_INVALID: {error}")
        return 1
    licenses = "passed" if args.packages_dir is not None else "not_requested"
    print(f"WINDOWS_RELEASE_LOCKS_OK projects={len(PROJECTS)} rid={RID} packages={packages} licenses={licenses}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
