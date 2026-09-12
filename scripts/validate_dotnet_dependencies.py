#!/usr/bin/env python3
"""Validate locked NuGet dependencies, audit output, sources, and licenses."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXACT_VERSION_RE = re.compile(r"^[0-9]+(?:\.[0-9]+){2,3}$")


def load_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"expected a JSON object: {path}")
    return value


def solution_projects(root: Path, solution: Path) -> list[Path]:
    tree = ET.parse(solution)
    projects: list[Path] = []
    root_resolved = root.resolve()
    for element in tree.iter():
        if element.tag.rsplit("}", 1)[-1] != "Project":
            continue
        relative = element.get("Path")
        if not relative:
            raise ValueError("solution contains a Project without Path")
        project = (solution.parent / relative).resolve()
        if not project.is_relative_to(root_resolved):
            raise ValueError(f"solution project escapes repository: {relative}")
        if not project.is_file():
            raise ValueError(f"solution project is missing: {relative}")
        projects.append(project)
    if not projects:
        raise ValueError("solution contains no projects")
    if len(projects) != len(set(projects)):
        raise ValueError("solution contains duplicate project paths")
    return projects


def central_package_versions(path: Path) -> dict[str, str]:
    tree = ET.parse(path)
    properties = {
        element.tag.rsplit("}", 1)[-1]: (element.text or "").strip().lower()
        for element in tree.iter()
    }
    if properties.get("ManagePackageVersionsCentrally") != "true":
        raise ValueError("central package management must be enabled")
    if properties.get("CentralPackageVersionOverrideEnabled") != "false":
        raise ValueError("per-project central package version overrides must be disabled")
    versions: dict[str, str] = {}
    for element in tree.iter():
        if element.tag.rsplit("}", 1)[-1] != "PackageVersion":
            continue
        package = element.get("Include")
        version = element.get("Version")
        if not package or not version:
            raise ValueError("PackageVersion requires Include and Version")
        key = package.lower()
        if key in versions:
            raise ValueError(f"duplicate central package version: {package}")
        if not EXACT_VERSION_RE.fullmatch(version):
            raise ValueError(f"central package version is not an exact stable version: {package} {version}")
        versions[key] = version
    if not versions:
        raise ValueError("no central package versions found")
    return versions


def configured_package_sources(path: Path) -> set[str]:
    tree = ET.parse(path)
    package_sources = next(
        (
            element for element in tree.iter()
            if element.tag.rsplit("}", 1)[-1] == "packageSources"
        ),
        None,
    )
    if package_sources is None:
        raise ValueError("NuGet.config must declare packageSources")
    sources: dict[str, str] = {}
    cleared = False
    for element in package_sources:
        tag = element.tag.rsplit("}", 1)[-1]
        if tag == "clear":
            sources.clear()
            cleared = True
        elif tag == "add":
            key = element.get("key")
            value = element.get("value")
            if not key or not value or key.lower() in sources:
                raise ValueError("NuGet.config contains an invalid or duplicate package source")
            sources[key.lower()] = value.strip()
    if not cleared:
        raise ValueError("NuGet.config must clear inherited package sources")
    if not sources:
        raise ValueError("NuGet.config must declare at least one package source")
    return set(sources.values())


def has_sha512_content_hash(value: object) -> bool:
    if not isinstance(value, str):
        return False
    try:
        return len(base64.b64decode(value, validate=True)) == 64
    except (ValueError, base64.binascii.Error):
        return False


def project_package_references(project: Path, central: dict[str, str]) -> set[str]:
    references: set[str] = set()
    for element in ET.parse(project).iter():
        if element.tag.rsplit("}", 1)[-1] != "PackageReference":
            continue
        package = element.get("Include")
        if not package:
            raise ValueError(f"PackageReference without Include: {project}")
        if element.get("Version") or any(
            child.tag.rsplit("}", 1)[-1] == "Version" for child in element
        ):
            raise ValueError(f"project bypasses central package management: {project} / {package}")
        key = package.lower()
        if key not in central:
            raise ValueError(f"package lacks a central version: {project} / {package}")
        if key in references:
            raise ValueError(f"duplicate PackageReference: {project} / {package}")
        references.add(key)
    return references


def locked_packages(
    projects: list[Path], central: dict[str, str],
    group_overrides: dict[Path, list[str]] | None = None,
) -> dict[str, tuple[str, str]]:
    packages: dict[str, tuple[str, str]] = {}
    for project in projects:
        references = project_package_references(project, central)
        lock_path = project.parent / "packages.lock.json"
        if not lock_path.is_file():
            raise ValueError(f"missing lock file: {lock_path}")
        lock = load_json(lock_path)
        if lock.get("version") != 2:
            raise ValueError(f"lock file must use central-package-management version 2: {lock_path}")
        target_groups = lock.get("dependencies")
        expected_groups = (group_overrides or {}).get(project.resolve(), ["net10.0"])
        if not isinstance(target_groups, dict) or set(target_groups) != set(expected_groups):
            raise ValueError(f"lock groups differ from approved project groups {expected_groups}: {lock_path}")
        for group_name, target in target_groups.items():
            if not isinstance(target, dict):
                raise ValueError(f"invalid dependency group: {lock_path}")
            direct: set[str] = set()
            for package, details in target.items():
                if not isinstance(details, dict):
                    raise ValueError(f"invalid locked dependency: {lock_path} / {package}")
                if details.get("type") == "Project":
                    continue
                resolved = details.get("resolved")
                content_hash = details.get("contentHash")
                if not isinstance(resolved, str) or not EXACT_VERSION_RE.fullmatch(resolved):
                    raise ValueError(f"dependency is not locked to an exact stable version: {package}")
                if not has_sha512_content_hash(content_hash):
                    raise ValueError(f"dependency lacks a valid SHA-512 content hash: {package} {resolved}")
                dependency_type = details.get("type")
                if dependency_type not in {"Direct", "Transitive", "CentralTransitive"}:
                    raise ValueError(f"dependency has an unsupported lock type: {package}")
                key = package.lower()
                if dependency_type == "Direct":
                    direct.add(key)
                if dependency_type in {"Direct", "CentralTransitive"}:
                    expected = central.get(key)
                    if expected != resolved:
                        raise ValueError(
                            f"centrally managed dependency drift: "
                            f"{package} resolves {resolved}, expected {expected}"
                        )
                prior = packages.get(key)
                current = (package, resolved)
                if prior is not None and prior[1] != resolved:
                    raise ValueError(f"inconsistent dependency versions: {package} {prior[1]} / {resolved}")
                packages[key] = current
            expected_direct = references if "/" not in group_name else set()
            if direct != expected_direct:
                missing = sorted(expected_direct - direct)
                unexpected = sorted(direct - expected_direct)
                raise ValueError(
                    f"project and lock direct dependencies differ for {project}: "
                    f"missing={missing}, unexpected={unexpected}"
                )
    return packages


def license_file_exceptions(policy: dict) -> dict[tuple[str, str, str], str]:
    exceptions: dict[tuple[str, str, str], str] = {}
    for item in policy.get("license_file_exceptions", []):
        if not isinstance(item, dict):
            raise ValueError("license_file_exceptions entries must be objects")
        values = [item.get(name) for name in ("id", "version", "path", "sha256")]
        if not all(isinstance(value, str) and value for value in values):
            raise ValueError("license file exception requires id, version, path, and sha256")
        package, version, path, digest = values
        key = (package.lower(), version.lower(), path.replace("\\", "/").lower())
        if key in exceptions or not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
            raise ValueError(f"invalid or duplicate license file exception: {package} {version}")
        exceptions[key] = digest.lower()
    return exceptions


def validate_licenses(
    packages: dict[str, tuple[str, str]],
    packages_dir: Path,
    policy: dict,
) -> list[str]:
    allowed_expressions = {
        str(value).strip() for value in policy.get("allowed_license_expressions", [])
        if str(value).strip()
    }
    exceptions = license_file_exceptions(policy)
    legacy = policy.get("legacy_license_url_exceptions", [])
    if not isinstance(legacy, list) or any(
        not isinstance(item, dict) or set(item) != {"id", "version", "url", "nuspec_sha256"}
        or not all(isinstance(value, str) and value for value in item.values())
        or not re.fullmatch(r"[0-9a-f]{64}", item.get("nuspec_sha256", ""))
        for item in legacy
    ):
        raise ValueError("invalid legacy license URL exception")
    errors: list[str] = []
    for key, (package, version) in sorted(packages.items()):
        package_root = packages_dir / key / version.lower()
        nuspec = package_root / f"{key}.nuspec"
        if not nuspec.is_file():
            errors.append(f"restored package metadata is missing: {package} {version}")
            continue
        license_element = next(
            (
                element for element in ET.parse(nuspec).iter()
                if element.tag.rsplit("}", 1)[-1] == "license"
            ),
            None,
        )
        if license_element is None or not (license_element.text or "").strip():
            url = next((element.text for element in ET.parse(nuspec).iter()
                        if element.tag.rsplit("}", 1)[-1] == "licenseUrl"), None)
            if any(item["id"].lower() == key and item["version"] == version
                   and item["url"] == url and item["nuspec_sha256"] == hashlib.sha256(nuspec.read_bytes()).hexdigest()
                   for item in legacy):
                continue
            errors.append(f"package has no machine-readable license: {package} {version}")
            continue
        license_value = (license_element.text or "").strip()
        license_type = (license_element.get("type") or "").lower()
        if license_type == "expression":
            if license_value not in allowed_expressions:
                errors.append(
                    f"unapproved license expression: {package} {version} / {license_value}"
                )
            continue
        if license_type == "file":
            relative = license_value.replace("\\", "/")
            license_path = (package_root / relative).resolve()
            if not license_path.is_relative_to(package_root.resolve()) or not license_path.is_file():
                errors.append(f"package license file is missing or escapes package: {package} {version}")
                continue
            digest = hashlib.sha256(license_path.read_bytes()).hexdigest()
            expected = exceptions.get((key, version.lower(), relative.lower()))
            if digest != expected:
                errors.append(
                    f"unapproved license file: {package} {version} / {relative} / {digest}"
                )
            continue
        errors.append(f"unsupported license metadata: {package} {version} / {license_type}")
    return errors


def validate_vulnerability_report(
    report: dict,
    approved_sources: set[str],
    expected_projects: list[Path],
) -> list[str]:
    errors: list[str] = []
    if report.get("version") != 1:
        errors.append("vulnerability report version must be 1")
    parameters = str(report.get("parameters", ""))
    for required in ("--vulnerable", "--include-transitive"):
        if required not in parameters:
            errors.append(f"vulnerability report is missing parameter {required}")
    sources = report.get("sources")
    if not isinstance(sources, list) or not sources:
        errors.append("vulnerability report has no package sources")
    else:
        unexpected = sorted(set(map(str, sources)) - approved_sources)
        if unexpected:
            errors.append(f"vulnerability report used unapproved sources: {unexpected}")
    projects = report.get("projects")
    if not isinstance(projects, list) or not projects:
        errors.append("vulnerability report contains no audited projects")
    else:
        try:
            reported_projects = {
                Path(str(item["path"])).resolve()
                for item in projects
                if isinstance(item, dict) and item.get("path")
            }
        except OSError as exc:
            errors.append(f"invalid project path in vulnerability report: {exc}")
        else:
            if reported_projects != set(expected_projects):
                missing = sorted(str(path) for path in set(expected_projects) - reported_projects)
                unexpected = sorted(str(path) for path in reported_projects - set(expected_projects))
                errors.append(
                    "vulnerability report project coverage differs from solution: "
                    f"missing={missing}, unexpected={unexpected}"
                )

    def walk(value: object, context: str = "report") -> None:
        if isinstance(value, dict):
            next_context = str(value.get("id") or value.get("name") or value.get("path") or context)
            vulnerabilities = value.get("vulnerabilities")
            if isinstance(vulnerabilities, list) and vulnerabilities:
                errors.append(f"vulnerable dependency reported for {next_context}")
            for child in value.values():
                walk(child, next_context)
        elif isinstance(value, list):
            for child in value:
                walk(child, context)

    walk(report)
    return errors


def validate(
    root: Path,
    solution: Path,
    policy_path: Path,
    packages_dir: Path,
    vulnerability_report: Path,
) -> tuple[list[str], int, int]:
    errors: list[str] = []
    projects: list[Path] = []
    packages: dict[str, tuple[str, str]] = {}
    try:
        policy = load_json(policy_path)
        if policy.get("version") != 1:
            raise ValueError("dependency policy version must be 1")
        approved_sources = {
            str(source).strip() for source in policy.get("approved_sources", [])
            if str(source).strip()
        }
        if not approved_sources:
            raise ValueError("dependency policy must approve at least one package source")
        configured_sources = configured_package_sources(root / "NuGet.config")
        if configured_sources != approved_sources:
            raise ValueError(
                "NuGet.config sources differ from dependency policy: "
                f"configured={sorted(configured_sources)}, approved={sorted(approved_sources)}"
            )
        projects = solution_projects(root, solution)
        central = central_package_versions(root / "Directory.Packages.props")
        group_overrides: dict[Path, list[str]] = {}
        configured_groups = policy.get("project_target_groups", {})
        if not isinstance(configured_groups, dict):
            raise ValueError("project_target_groups must be an object")
        for relative, groups in configured_groups.items():
            project_path = (root / relative).resolve()
            if (Path(relative).is_absolute() or not project_path.is_relative_to(root.resolve())
                    or not isinstance(groups, list) or not groups
                    or not all(isinstance(group, str) and re.fullmatch(
                        r"net10\.0(?:-windows[0-9]+(?:\.[0-9]+){1,3})?(?:/win-x64)?", group
                    ) for group in groups)
                    or len(set(groups)) != len(groups)
                    or sum("/" not in group for group in groups) != 1):
                raise ValueError(f"invalid exact project target groups: {relative}")
            base_group = next(group for group in groups if "/" not in group)
            if any(group != base_group and group != base_group + "/win-x64" for group in groups):
                raise ValueError(f"mismatched project target groups: {relative}")
            group_overrides[project_path] = groups
        packages = locked_packages(projects, central, group_overrides)
        errors.extend(validate_licenses(packages, packages_dir.resolve(), policy))
        report = load_json(vulnerability_report)
        errors.extend(validate_vulnerability_report(report, approved_sources, projects))
    except (ET.ParseError, OSError, ValueError, json.JSONDecodeError) as exc:
        errors.append(str(exc))
    return errors, len(projects), len(packages)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--solution", type=Path, default=Path("AssetLibrary.slnx"))
    parser.add_argument(
        "--policy",
        type=Path,
        default=Path("eng/dotnet-dependency-policy.json"),
    )
    parser.add_argument("--packages-dir", type=Path, required=True)
    parser.add_argument("--vulnerability-report", type=Path, required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    solution = args.solution if args.solution.is_absolute() else root / args.solution
    policy = args.policy if args.policy.is_absolute() else root / args.policy
    report = (
        args.vulnerability_report
        if args.vulnerability_report.is_absolute()
        else root / args.vulnerability_report
    )
    errors, project_count, package_count = validate(
        root,
        solution,
        policy,
        args.packages_dir if args.packages_dir.is_absolute() else root / args.packages_dir,
        report,
    )
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        return 1
    print(
        f".NET dependency policy passed ({project_count} projects, "
        f"{package_count} locked packages)."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
