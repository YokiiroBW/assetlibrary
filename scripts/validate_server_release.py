#!/usr/bin/env python3
"""Validate V01-008 server release definitions and optional native artifacts."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import tarfile
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath
from typing import Any, BinaryIO, Iterable


ROOT = Path(__file__).resolve().parents[1]
CONTRACT = "v01-008/1"
OUTPUT_MARKER = ".assetlibrary-v01-008-release-output"
MARKER_VALUE = "AssetLibrary/V01-008/release-output/v1\n"
REQUIRED_OPEN_GATES = {
    "M0-004-G1",
    "M0-004-G2",
    "M0-006-G1",
    "M0-006-G2",
    "M0-006-G3",
}
BASE_LOCKS = {
    "AssetLibrary.AssetLink": "packages/sdk/assetlink/dotnet/packages.lock.json",
    "AssetLibrary.CoreServer": "services/core-server/packages.lock.json",
    "AssetLibrary.CoreServer.Host": "services/core-server/Host/packages.lock.json",
}


def canonical_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(",", ":")) + "\n").encode(
        "utf-8"
    )


def sha256_stream(stream: BinaryIO) -> str:
    digest = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(chunk)
    return digest.hexdigest()


def sha256_file(path: Path) -> str:
    with path.open("rb") as stream:
        return sha256_stream(stream)


def is_link_or_reparse(path: Path) -> bool:
    try:
        metadata = path.lstat()
    except FileNotFoundError:
        return False
    attributes = getattr(metadata, "st_file_attributes", 0)
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return path.is_symlink() or bool(attributes & reparse_flag)


def add_missing(errors: list[str], text: str, fragments: Iterable[str], label: str) -> None:
    for fragment in fragments:
        if fragment not in text:
            errors.append(f"{label} is missing required contract: {fragment}")


def read_json(path: Path, errors: list[str]) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        errors.append(f"cannot read valid JSON from {path.name}: {exception}")
        return {}
    if not isinstance(value, dict):
        errors.append(f"{path.name} must contain a JSON object")
        return {}
    return value


def xml_values(path: Path, errors: list[str]) -> dict[str, list[str]]:
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as exception:
        errors.append(f"cannot parse {path.name}: {exception}")
        return {}
    values: dict[str, list[str]] = {}
    for element in root.iter():
        tag = element.tag.rsplit("}", 1)[-1]
        values.setdefault(tag, []).append((element.text or "").strip())
    return values


def package_versions(path: Path, errors: list[str]) -> dict[str, str]:
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as exception:
        errors.append(f"cannot parse {path.name}: {exception}")
        return {}
    return {
        element.attrib["Include"]: element.attrib["Version"]
        for element in root.iter()
        if element.tag.rsplit("}", 1)[-1] == "PackageVersion"
        and "Include" in element.attrib
        and "Version" in element.attrib
    }


def validate_repository(root: Path) -> tuple[list[str], dict[str, Any]]:
    errors: list[str] = []
    policy = read_json(root / "eng/server-release-policy.json", errors)
    sdk = read_json(root / "global.json", errors).get("sdk", {})
    expected_rids = ["linux-x64", "win-x64"]

    if policy.get("contract") != CONTRACT:
        errors.append("release policy contract must be v01-008/1")
    if policy.get("dotnet_sdk") != "10.0.111" or sdk.get("version") != "10.0.111":
        errors.append("release policy and global.json must pin .NET SDK 10.0.111")
    if sdk.get("rollForward") != "disable" or sdk.get("allowPrerelease") is not False:
        errors.append("global.json must fail closed on SDK drift")
    if policy.get("aspnet_runtime") != "10.0.11":
        errors.append("ASP.NET runtime must be pinned to 10.0.11")
    if policy.get("runtime_identifiers") != expected_rids:
        errors.append("native runtime identifiers must be linux-x64 then win-x64")
    if policy.get("cold_publish_runs") != 2:
        errors.append("release policy must require exactly two independent cold publishes")
    native = policy.get("native_artifacts", {})
    if native != {
        "self_contained": True,
        "manifest_hash": "SHA-256",
        "windows_archive": "zip",
        "linux_archive": "tar.gz",
    }:
        errors.append("native artifact format and hashing policy drifted")
    if policy.get("artifact_root") != ".runtime/sandbox-storage/V01-008":
        errors.append("artifact_root must remain inside the fixed V01-008 task sandbox")
    if policy.get("evidence_statuses") != ["passed", "blocked_missing_environment", "failed"]:
        errors.append("platform evidence status vocabulary drifted")
    if policy.get("host", {}).get("required_environment") != "Production":
        errors.append("server host must require the explicit Production environment")

    package = policy.get("windows_service_package", {})
    versions = package_versions(root / "Directory.Packages.props", errors)
    if package.get("name") != "Microsoft.Extensions.Hosting.WindowsServices":
        errors.append("Windows Service package identity drifted")
    if package.get("version") != "10.0.11" or package.get("license") != "MIT":
        errors.append("Windows Service package version/license policy drifted")
    if versions.get("Microsoft.Extensions.Hosting.WindowsServices") != "10.0.11":
        errors.append("central Windows Service package version must be 10.0.11")
    for package_name in (
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
    ):
        if versions.get(package_name) != "10.0.11":
            errors.append(f"central framework patch pin must be 10.0.11: {package_name}")

    build_values = xml_values(root / "Directory.Build.props", errors)
    release_lock_routes = build_values.get("NuGetLockFilePath", [])
    if release_lock_routes != [
        "$(AssetLibraryReleaseLockRoot)/$(MSBuildProjectName).$(RuntimeIdentifier).lock.json"
    ]:
        errors.append("RID release locks must use the single sandbox-selectable lock route")
    for relative in BASE_LOCKS.values():
        lock = read_json(root / relative, errors)
        if set(lock.get("dependencies", {})) != {"net10.0"}:
            errors.append(f"committed project lock must remain RID-neutral: {relative}")

    host_project = root / str(policy.get("host_project", "missing"))
    host_values = xml_values(host_project, errors)
    try:
        host_xml = ET.parse(host_project).getroot()
    except (OSError, ET.ParseError):
        host_xml = ET.Element("invalid")
    if host_xml.attrib.get("Sdk") != "Microsoft.NET.Sdk.Web":
        errors.append("host project must use the single ASP.NET Core Web SDK wrapper")
    if host_values.get("RuntimeIdentifier") or host_values.get("RuntimeIdentifiers"):
        errors.append("runtime identifiers belong in the release policy, not the shared project lock graph")
    references = {
        element.attrib.get("Include")
        for element in host_xml.iter()
        if element.tag.rsplit("}", 1)[-1] == "PackageReference"
    }
    project_references = {
        element.attrib.get("Include", "").replace("\\", "/")
        for element in host_xml.iter()
        if element.tag.rsplit("}", 1)[-1] == "ProjectReference"
    }
    if references != {"Microsoft.Extensions.Hosting.WindowsServices"}:
        errors.append("host wrapper may have only the approved Windows Service package")
    if project_references != {"../AssetLibrary.CoreServer.csproj"}:
        errors.append("host wrapper must reference the one existing CoreServer project")

    core_project = (root / "services/core-server/AssetLibrary.CoreServer.csproj").read_text(encoding="utf-8")
    if '<Compile Remove="Host\\**\\*.cs" />' not in core_project:
        errors.append("CoreServer library must exclude the host entry-point sources")

    build_info = (root / "services/core-server/Host/Hosting/CoreServerBuildInfo.cs").read_text(
        encoding="utf-8"
    )
    endpoints = (root / "services/core-server/Host/Hosting/CoreServerEndpointPayloads.cs").read_text(
        encoding="utf-8"
    )
    add_missing(
        errors,
        build_info,
        ('CurrentContract = "v01-008/1"', "AssetLibrary.SourceRevision"),
        "host build-info",
    )
    add_missing(
        errors,
        endpoints,
        ('"host_only"', "BusinessApiReady: false", "ProductionFileWritesEnabled: false"),
        "host readiness",
    )

    dockerfile = (root / "infra/docker/Dockerfile").read_text(encoding="utf-8")
    compose = (root / "infra/docker/compose.yaml").read_text(encoding="utf-8")
    dockerignore = (root / "infra/docker/Dockerfile.dockerignore").read_text(encoding="utf-8")
    add_missing(
        errors,
        dockerfile,
        (
            "FROM mcr.microsoft.com/dotnet/sdk:10.0.111 AS build",
            "FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime",
            "test \"${#SOURCE_REVISION}\" -eq 40",
            "--locked-mode",
            "--self-contained false",
            "-p:SourceRevisionId=\"$SOURCE_REVISION\"",
            "org.opencontainers.image.revision=\"$SOURCE_REVISION\"",
            "USER 1654:1654",
            "--health-probe",
            '"--environment", "Production"',
            'ENTRYPOINT ["/app/AssetLibrary.CoreServer.Host"',
        ),
        "Dockerfile",
    )
    if "-r linux-x64" in dockerfile or "--runtime linux-x64" in dockerfile:
        errors.append("Docker framework-dependent restore must use the committed RID-neutral locks")
    add_missing(
        errors,
        compose,
        (
            'SOURCE_REVISION: "${ASSETLIBRARY_SOURCE_REVISION:?',
            'user: "1654:1654"',
            "read_only: true",
            "cap_drop:",
            "- ALL",
            "no-new-privileges:true",
            "/tmp:size=16m,noexec,nosuid,nodev",
            '"127.0.0.1:${ASSETLIBRARY_EVIDENCE_PORT:-5080}:8080"',
            "assetlibrary-state:/var/lib/assetlibrary",
        ),
        "Compose",
    )
    add_missing(errors, dockerignore, (".git", ".runtime", ".env.*", "**/obj"), "Docker context")

    release_builder = (root / "scripts/build_server_release.py").read_text(encoding="utf-8")
    add_missing(
        errors,
        release_builder,
        (
            "prepare_release_locks",
            '"-p:SelfContained=true"',
            "release_lock_aggregate_sha256",
            "independent cold-publish manifests differ",
        ),
        "native release builder",
    )

    unit = (root / "infra/linux-server/assetlibrary-core-server.service").read_text(encoding="utf-8")
    add_missing(
        errors,
        unit,
        (
            "User=assetlibrary",
            "Group=assetlibrary",
            "StateDirectory=assetlibrary",
            "NoNewPrivileges=true",
            "ProtectSystem=strict",
            "ProtectHome=true",
            "PrivateDevices=true",
            "CapabilityBoundingSet=",
            "AmbientCapabilities=",
            "ReadWritePaths=/var/lib/assetlibrary",
            "TimeoutStopSec=15s",
            "--environment Production",
        ),
        "systemd unit",
    )

    windows_script = (root / "infra/windows-server/service-evidence.ps1").read_text(encoding="utf-8")
    add_missing(
        errors,
        windows_script,
        (
            "AssetLibrary-V01-008-Evidence",
            "AssetLibraryEvidenceOwner",
            "NT AUTHORITY\\LocalService",
            "ApproveSystemChanges",
            "ExpectedSourceRevision",
            "Assert-StrictDescendant",
            "Assert-NoReparsePoint",
            "verify-absent",
            "--environment Production",
        ),
        "Windows Service evidence script",
    )
    linux_script = (root / "infra/linux-server/systemd-evidence.sh").read_text(encoding="utf-8")
    add_missing(
        errors,
        linux_script,
        (
            "ASSETLIBRARY_APPROVE_SYSTEM_CHANGES",
            "ASSETLIBRARY_EXPECTED_HOSTNAME",
            "ASSETLIBRARY_EXPECTED_SOURCE_REVISION",
            "AssetLibrary/V01-008/linux-systemd-evidence/v1",
            "has_reparse_or_link",
            "verify-absent",
            "systemctl daemon-reload",
            "systemd-analyze verify",
        ),
        "Linux systemd evidence script",
    )

    gate_contract = read_json(root / "tests/architecture/m0-gates.json", errors)
    gates = {gate.get("id"): gate for gate in gate_contract.get("gates", [])}
    for gate_id in sorted(REQUIRED_OPEN_GATES):
        if gates.get(gate_id, {}).get("status") != "open":
            errors.append(f"{gate_id} must remain open until separate real-environment evidence is reviewed")

    report = {
        "contract": CONTRACT,
        "target": "release_definitions",
        "status": "passed" if not errors else "failed",
        "runtime_identifiers": policy.get("runtime_identifiers", []),
        "platform_evidence": {
            "windows_service": "blocked_missing_environment",
            "docker": "blocked_missing_environment",
            "linux_systemd": "blocked_missing_environment",
        },
        "open_release_gates_verified": sorted(REQUIRED_OPEN_GATES),
    }
    return errors, report


def safe_manifest_relative(value: str) -> PurePosixPath:
    raw_parts = value.split("/")
    path = PurePosixPath(value)
    if (
        path.is_absolute()
        or not path.parts
        or any(part in ("", ".", "..") for part in raw_parts)
        or ":" in path.parts[0]
    ):
        raise ValueError("unsafe relative path")
    return path


def verify_archive(
    archive_path: Path,
    runtime_identifier: str,
    artifacts: Path,
    errors: list[str],
) -> None:
    prefix = f"assetlibrary-core-server-{runtime_identifier}"
    expected: dict[str, Path] = {}
    for path in (artifacts / runtime_identifier).rglob("*"):
        if path.is_file():
            relative = path.relative_to(artifacts / runtime_identifier).as_posix()
            expected[f"{prefix}/{relative}"] = path
    observed: set[str] = set()
    try:
        if archive_path.suffix == ".zip":
            with zipfile.ZipFile(archive_path) as archive:
                for member in archive.infolist():
                    if member.is_dir():
                        errors.append(f"non-file zip member is forbidden: {member.filename}")
                        continue
                    safe_manifest_relative(member.filename)
                    if stat.S_ISLNK(member.external_attr >> 16):
                        errors.append(f"zip symbolic link is forbidden: {member.filename}")
                        continue
                    if member.filename not in expected:
                        errors.append(f"unexpected archive member: {member.filename}")
                        continue
                    if member.filename in observed:
                        errors.append(f"duplicate archive member: {member.filename}")
                        continue
                    with archive.open(member) as stream:
                        if sha256_stream(stream) != sha256_file(expected[member.filename]):
                            errors.append(f"archive content hash mismatch: {member.filename}")
                    observed.add(member.filename)
        else:
            with tarfile.open(archive_path, mode="r:gz") as archive:
                for member in archive.getmembers():
                    if not member.isfile():
                        errors.append(f"non-file tar member is forbidden: {member.name}")
                        continue
                    safe_manifest_relative(member.name)
                    if member.name in observed:
                        errors.append(f"duplicate archive member: {member.name}")
                        continue
                    if member.name not in expected:
                        errors.append(f"unexpected archive member: {member.name}")
                        continue
                    stream = archive.extractfile(member)
                    if stream is None or sha256_stream(stream) != sha256_file(expected[member.name]):
                        errors.append(f"archive content hash mismatch: {member.name}")
                    observed.add(member.name)
    except (OSError, tarfile.TarError, ValueError, zipfile.BadZipFile) as exception:
        errors.append(f"cannot safely validate archive {archive_path.name}: {exception}")
        return
    missing = sorted(set(expected) - observed)
    if missing:
        errors.append(f"archive {archive_path.name} is missing {len(missing)} files")


def validate_release_locks(
    root: Path,
    output_root: Path,
    source_revision: str,
    errors: list[str],
) -> str:
    manifest_path = output_root / "release-locks.json"
    lock_manifest = read_json(manifest_path, errors)
    if lock_manifest and manifest_path.read_bytes() != canonical_json_bytes(lock_manifest):
        errors.append("release-lock manifest is not canonical JSON")
    if lock_manifest.get("contract") != CONTRACT or lock_manifest.get("source_revision") != source_revision:
        errors.append("release-lock manifest provenance drifted")
    declared: set[str] = set()
    expected = {
        f"release-locks/{project}.{runtime_identifier}.lock.json"
        for project in BASE_LOCKS
        for runtime_identifier in ("linux-x64", "win-x64")
    }
    entries = lock_manifest.get("locks", [])
    if not isinstance(entries, list):
        errors.append("release-lock manifest locks must be an array")
        entries = []
    for entry in entries:
        if not isinstance(entry, dict):
            errors.append("release-lock manifest entries must be objects")
            continue
        try:
            relative = safe_manifest_relative(str(entry.get("path", "")))
        except ValueError:
            errors.append("release-lock manifest contains an unsafe path")
            continue
        name = relative.as_posix()
        if name in declared:
            errors.append(f"duplicate release-lock manifest path: {name}")
            continue
        declared.add(name)
        project = entry.get("project")
        runtime_identifier = entry.get("runtime_identifier")
        lock_path = output_root.joinpath(*relative.parts)
        expected_name = (
            f"release-locks/{project}.{runtime_identifier}.lock.json"
            if project in BASE_LOCKS and runtime_identifier in ("linux-x64", "win-x64")
            else ""
        )
        if name not in expected or name != expected_name:
            errors.append(f"unexpected release lock: {name}")
            continue
        if not lock_path.is_file() or is_link_or_reparse(lock_path):
            errors.append(f"release lock is missing or unsafe: {name}")
            continue
        if lock_path.stat().st_size != entry.get("length") or sha256_file(lock_path) != entry.get("sha256"):
            errors.append(f"release lock digest mismatch: {name}")
        generated = read_json(lock_path, errors)
        base = read_json(root / BASE_LOCKS[project], errors)
        groups = generated.get("dependencies", {})
        base_groups = base.get("dependencies", {})
        if set(groups) != {"net10.0", f"net10.0/{runtime_identifier}"}:
            errors.append(f"release lock target groups drifted: {name}")
        if set(base_groups) != {"net10.0"} or groups.get("net10.0") != base_groups.get("net10.0"):
            errors.append(f"release lock changed the committed neutral graph: {name}")
        runtime_group = groups.get(f"net10.0/{runtime_identifier}", {})
        if not isinstance(runtime_group, dict):
            errors.append(f"release lock RID dependency group is invalid: {name}")
            continue
        for package_name, details in runtime_group.items():
            if not isinstance(details, dict):
                errors.append(f"release lock RID dependency is invalid: {name} / {package_name}")
                continue
            if details.get("type") == "Project":
                continue
            if not isinstance(details.get("resolved"), str) or not details["resolved"]:
                errors.append(f"release lock RID dependency is unversioned: {name} / {package_name}")
            digest = details.get("contentHash")
            if not isinstance(digest, str) or len(digest) < 80:
                errors.append(f"release lock RID dependency hash is invalid: {name} / {package_name}")
    if declared != expected:
        errors.append("release-lock manifest does not cover the exact project/RID matrix")
    actual = {
        path.relative_to(output_root).as_posix()
        for path in (output_root / "release-locks").rglob("*.lock.json")
        if path.is_file()
    } if (output_root / "release-locks").is_dir() else set()
    if actual != expected:
        errors.append("release-lock directory does not contain the exact project/RID matrix")

    aggregate = hashlib.sha256(canonical_json_bytes(lock_manifest)).hexdigest() if lock_manifest else ""
    try:
        stored = (output_root / "release-locks-aggregate-sha256.txt").read_text(
            encoding="utf-8"
        ).strip()
    except OSError:
        stored = ""
    if stored != aggregate:
        errors.append("release-lock aggregate digest mismatch")
    return aggregate


def validate_artifacts(root: Path, output_root: Path) -> tuple[list[str], dict[str, Any]]:
    errors: list[str] = []
    task_root = (root / ".runtime/sandbox-storage/V01-008").resolve(strict=False)
    resolved_output = output_root.resolve(strict=False)
    try:
        resolved_output.relative_to(task_root)
    except ValueError:
        errors.append("artifact output escaped the V01-008 task sandbox")
        return errors, {"status": "failed", "target": "native_artifacts"}
    if resolved_output == task_root:
        errors.append("artifact output must be a strict task-root descendant")
        return errors, {"status": "failed", "target": "native_artifacts"}
    if is_link_or_reparse(output_root):
        errors.append("artifact output may not be a link or reparse point")
        return errors, {"status": "failed", "target": "native_artifacts"}
    elif output_root.is_dir():
        unsafe_entries = [
            path.relative_to(output_root).as_posix()
            for path in output_root.rglob("*")
            if is_link_or_reparse(path)
        ]
        if unsafe_entries:
            errors.append(f"artifact output contains links or reparse points: {unsafe_entries[0]}")
            return errors, {"status": "failed", "target": "native_artifacts"}
    marker = output_root / OUTPUT_MARKER
    if not marker.is_file() or marker.read_text(encoding="utf-8") != MARKER_VALUE:
        errors.append("artifact output owner marker is missing or invalid")

    manifest_path = output_root / "manifest.json"
    manifest = read_json(manifest_path, errors)
    if manifest and manifest_path.read_bytes() != canonical_json_bytes(manifest):
        errors.append("artifact manifest is not canonical JSON")
    source_revision_value = manifest.get("source_revision", "")
    source_revision = source_revision_value if isinstance(source_revision_value, str) else ""
    if re.fullmatch(r"[0-9a-f]{40}", source_revision) is None:
        errors.append("artifact source_revision must be a full lowercase hexadecimal Git commit")
    if manifest.get("contract") != CONTRACT:
        errors.append("artifact manifest contract drifted")

    release_lock_aggregate = validate_release_locks(root, output_root, source_revision, errors)
    if manifest.get("release_lock_aggregate_sha256") != release_lock_aggregate:
        errors.append("artifact manifest is not bound to the release-lock aggregate")

    artifacts = output_root / "artifacts"
    declared: set[str] = set()
    file_entries = manifest.get("files", [])
    if not isinstance(file_entries, list):
        errors.append("artifact manifest files must be an array")
        file_entries = []
    for entry in file_entries:
        if not isinstance(entry, dict):
            errors.append("artifact manifest entries must be objects")
            continue
        try:
            relative = safe_manifest_relative(str(entry.get("path", "")))
        except ValueError:
            errors.append("artifact manifest contains an unsafe path")
            continue
        name = relative.as_posix()
        if name in declared:
            errors.append(f"duplicate artifact manifest path: {name}")
            continue
        if relative.parts[0] not in ("linux-x64", "win-x64"):
            errors.append(f"artifact path has an unexpected runtime root: {name}")
            continue
        declared.add(name)
        path = artifacts.joinpath(*relative.parts)
        if not path.is_file() or is_link_or_reparse(path):
            errors.append(f"artifact file is missing or unsafe: {name}")
            continue
        if path.stat().st_size != entry.get("length"):
            errors.append(f"artifact length mismatch: {name}")
        if sha256_file(path) != entry.get("sha256"):
            errors.append(f"artifact hash mismatch: {name}")
    actual = {
        path.relative_to(artifacts).as_posix()
        for path in artifacts.rglob("*")
        if path.is_file()
    } if artifacts.is_dir() else set()
    if actual != declared:
        errors.append("artifact manifest does not exactly cover the published files")
    required_executables = {
        "linux-x64/AssetLibrary.CoreServer.Host",
        "win-x64/AssetLibrary.CoreServer.Host.exe",
    }
    if not required_executables.issubset(declared):
        errors.append("artifact manifest is missing a native host executable")

    aggregate_path = output_root / "aggregate-sha256.txt"
    expected_aggregate = hashlib.sha256(canonical_json_bytes(manifest)).hexdigest() if manifest else ""
    try:
        aggregate = aggregate_path.read_text(encoding="utf-8").strip()
    except OSError:
        aggregate = ""
    if aggregate != expected_aggregate:
        errors.append("artifact aggregate digest mismatch")

    metadata = read_json(output_root / "release-metadata.json", errors)
    if metadata.get("source_revision") != source_revision or metadata.get("contract") != CONTRACT:
        errors.append("release metadata does not match the artifact manifest")
    if metadata.get("runtime_identifiers") != ["linux-x64", "win-x64"]:
        errors.append("release metadata runtime identifiers drifted")
    if metadata.get("self_contained") is not True:
        errors.append("native artifacts must be self-contained")
    if metadata.get("configuration") != "Release" or metadata.get("dotnet_sdk") != "10.0.111":
        errors.append("release metadata configuration or SDK drifted")
    if metadata.get("host_project") != "services/core-server/Host/AssetLibrary.CoreServer.Host.csproj":
        errors.append("release metadata host project drifted")
    if metadata.get("release_lock_aggregate_sha256") != release_lock_aggregate:
        errors.append("release metadata is not bound to the release-lock aggregate")

    cold_manifests: list[dict[str, Any]] = []
    for index in (1, 2):
        run_root = output_root / f"cold-runs/run-{index}"
        cold_manifests.append(read_json(run_root / "manifest.json", errors))
        expected_cold_aggregate = (
            hashlib.sha256(canonical_json_bytes(cold_manifests[-1])).hexdigest()
            if cold_manifests[-1]
            else ""
        )
        try:
            cold_aggregate = (run_root / "aggregate-sha256.txt").read_text(
                encoding="utf-8"
            ).strip()
        except OSError:
            cold_aggregate = ""
        if cold_aggregate != expected_cold_aggregate:
            errors.append(f"cold run {index} aggregate digest mismatch")
        run_locks = {
            path.name: sha256_file(path)
            for path in (run_root / "release-locks").glob("*.lock.json")
        }
        master_locks = {
            path.name: sha256_file(path)
            for path in (output_root / "release-locks").glob("*.lock.json")
        }
        if run_locks != master_locks:
            errors.append(f"cold run {index} did not use the immutable release-lock set")
    if not cold_manifests[0] or cold_manifests[0] != cold_manifests[1] or cold_manifests[0] != manifest:
        errors.append("two independent cold-publish manifests are not identical")

    archive_manifest = read_json(output_root / "archives.json", errors)
    archive_entries = archive_manifest.get("archives", [])
    if not isinstance(archive_entries, list):
        errors.append("archive manifest archives must be an array")
        archive_entries = []
    if (
        archive_manifest.get("contract") != CONTRACT
        or archive_manifest.get("source_revision") != source_revision
        or archive_manifest.get("artifact_aggregate_sha256") != aggregate
        or archive_manifest.get("release_lock_aggregate_sha256") != release_lock_aggregate
        or len(archive_entries) != 2
    ):
        errors.append("archive manifest must cover both native runtime identifiers")
    archive_runtimes: set[str] = set()
    archive_paths: set[str] = set()
    for entry in archive_entries:
        if not isinstance(entry, dict):
            errors.append("archive manifest entries must be objects")
            continue
        try:
            relative = safe_manifest_relative(str(entry.get("path", "")))
        except (AttributeError, ValueError):
            errors.append("archive manifest contains an unsafe path")
            continue
        archive_path = output_root.joinpath(*relative.parts)
        if relative.as_posix() in archive_paths:
            errors.append(f"duplicate archive manifest path: {relative.as_posix()}")
            continue
        archive_paths.add(relative.as_posix())
        if not archive_path.is_file():
            errors.append(f"archive is missing: {relative.as_posix()}")
            continue
        if archive_path.stat().st_size != entry.get("length") or sha256_file(archive_path) != entry.get("sha256"):
            errors.append(f"archive digest mismatch: {relative.as_posix()}")
        runtime_identifier = entry.get("runtime_identifier")
        if runtime_identifier not in ("linux-x64", "win-x64"):
            errors.append("archive runtime identifier is invalid")
            continue
        archive_runtimes.add(runtime_identifier)
        expected_suffix = ".zip" if runtime_identifier == "win-x64" else ".tar.gz"
        expected_name = (
            f"archives/assetlibrary-core-server-{runtime_identifier}-"
            f"{source_revision[:12]}{expected_suffix}"
        )
        if relative.as_posix() != expected_name:
            errors.append(f"archive path does not match its runtime/provenance: {relative.as_posix()}")
        verify_archive(archive_path, runtime_identifier, artifacts, errors)
    if archive_runtimes != {"linux-x64", "win-x64"}:
        errors.append("archive manifest does not cover each runtime exactly once")

    evidence = read_json(output_root / "build-evidence.json", errors)
    if evidence.get("status") != "passed" or evidence.get("target") != "native_artifacts":
        errors.append("native artifact build evidence is not passed")
    if evidence.get("source_revision") != source_revision or evidence.get("aggregate_sha256") != aggregate:
        errors.append("native artifact build evidence does not match the manifest")
    if evidence.get("release_lock_aggregate_sha256") != release_lock_aggregate:
        errors.append("native artifact build evidence is not bound to the release-lock aggregate")
    if evidence.get("cold_publish_runs") != 2:
        errors.append("native artifact evidence must report two cold publishes")
    if evidence.get("file_count") != len(declared):
        errors.append("native artifact evidence file count drifted")
    build_info = evidence.get("build_info", {})
    if not isinstance(build_info, dict) or (
        build_info.get("contract") != CONTRACT
        or build_info.get("source_revision") != source_revision
    ):
        errors.append("native artifact build-info evidence does not match the issuance commit")
    statuses = evidence.get("platform_evidence", {})
    if any(statuses.get(name) != "blocked_missing_environment" for name in ("windows_service", "docker", "linux_systemd")):
        errors.append("native build evidence must not claim unexecuted platform lifecycles passed")

    report = {
        "contract": CONTRACT,
        "target": "native_artifacts",
        "status": "passed" if not errors else "failed",
        "source_revision": source_revision,
        "aggregate_sha256": aggregate,
        "file_count": len(declared),
        "cold_publish_runs": 2,
    }
    return errors, report


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-artifacts", action="store_true")
    parser.add_argument("--output-root", type=Path)
    return parser.parse_args()


def main() -> int:
    arguments = parse_arguments()
    errors, report = validate_repository(ROOT)
    if arguments.require_artifacts:
        output_root = arguments.output_root or ROOT / ".runtime/sandbox-storage/V01-008/release-build"
        artifact_errors, artifact_report = validate_artifacts(ROOT, output_root.absolute())
        errors.extend(artifact_errors)
        report["native_artifacts"] = artifact_report
    report["status"] = "passed" if not errors else "failed"
    report["errors"] = errors
    print(json.dumps(report, ensure_ascii=True, sort_keys=True))
    return 0 if not errors else 1


if __name__ == "__main__":
    raise SystemExit(main())
