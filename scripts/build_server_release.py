#!/usr/bin/env python3
"""Build deterministic V01-008 native server artifacts inside the task sandbox."""
from __future__ import annotations

import sys

if not sys.flags.isolated:
    raise SystemExit("release tooling must run with Python isolated mode (-I)")

import argparse
import gzip
import hashlib
import json
import os
import platform
import shutil
import stat
import subprocess
import tarfile
import zipfile
from pathlib import Path, PurePosixPath
from typing import Any, Iterable


ROOT = Path(__file__).resolve().parents[1]
POLICY_PATH = ROOT / "eng" / "server-release-policy.json"
TASK_MARKER = ".assetlibrary-v01-008-task-root"
OUTPUT_MARKER = ".assetlibrary-v01-008-release-output"
MARKER_VALUE = "AssetLibrary/V01-008/release-output/v1\n"
SOURCE_SNAPSHOT_MARKER = ".assetlibrary-v01-008-source-snapshot"
SOURCE_SNAPSHOT_MARKER_VALUE = "AssetLibrary/V01-008/source-snapshot/v1\n"
RUNTIME_EVIDENCE_BINDING_CONTEXT = "AssetLibrary/V01-008/runtime-evidence-binding/v1"
REQUIRED_MSBUILD_AUTO_IMPORTS = {
    "Directory.Build.props",
    "Directory.Build.targets",
}
REQUIRED_ISSUANCE_INPUTS = (
    ".codex/tasks/V01-008.md",
    "AssetLibrary.slnx",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "NuGet.config",
    "global.json",
    "eng/server-release-policy.json",
    "eng/CodeMetricsConfig.txt",
    "infra/docker",
    "infra/linux-server",
    "infra/windows-server",
    "scripts",
    "tests/release/run_docker_evidence.py",
    "packages/sdk/assetlink/dotnet",
    "services/core-server",
)
MSBUILD_IMPORT_OVERRIDE_ENVIRONMENT = {
    "customaftermicrosoftcommonprops",
    "customaftermicrosoftcommontargets",
    "custombeforemicrosoftcommonprops",
    "custombeforemicrosoftcommontargets",
    "directorybuildpropspath",
    "directorybuildtargetspath",
    "importdirectorybuildprops",
    "importdirectorybuildtargets",
    "msbuildextensionspath",
    "msbuildsdkspath",
    "msbuilduserextensionspath",
    "msbuild_exe_path",
}
BASE_LOCKS = {
    "AssetLibrary.AssetLink": "packages/sdk/assetlink/dotnet/packages.lock.json",
    "AssetLibrary.CoreServer": "services/core-server/packages.lock.json",
    "AssetLibrary.CoreServer.Host": "services/core-server/Host/packages.lock.json",
}


class ReleaseBuildError(RuntimeError):
    """A fail-closed release build contract violation."""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(",", ":")) + "\n").encode(
        "utf-8"
    )


def runtime_evidence_binding_sha256(
    source_revision: str,
    runtime_identifier: str,
    runtime_tree_sha256: str,
) -> str:
    payload = (
        f"{RUNTIME_EVIDENCE_BINDING_CONTEXT}\n"
        f"{source_revision}\n"
        f"{runtime_identifier}\n"
        f"{runtime_tree_sha256}\n"
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def write_json(path: Path, value: Any) -> None:
    path.write_bytes(canonical_json_bytes(value))


def is_link_or_reparse(path: Path) -> bool:
    try:
        metadata = path.lstat()
    except FileNotFoundError:
        return False
    attributes = getattr(metadata, "st_file_attributes", 0)
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return path.is_symlink() or bool(attributes & reparse_flag)


def assert_no_link_components(path: Path, boundary: Path) -> None:
    path = path.absolute()
    boundary = boundary.absolute()
    try:
        path.relative_to(boundary)
    except ValueError as exception:
        raise ReleaseBuildError("path escaped its declared boundary") from exception

    current = path
    while True:
        if is_link_or_reparse(current):
            raise ReleaseBuildError("links and reparse points are forbidden in the release boundary")
        if current == boundary:
            break
        current = current.parent


def assert_no_link_descendants(root: Path) -> None:
    if not root.is_dir():
        return
    for current, directories, files in os.walk(root, topdown=True, followlinks=False):
        for name in (*directories, *files):
            if is_link_or_reparse(Path(current) / name):
                raise ReleaseBuildError("links and reparse points are forbidden in owned output")


def assert_strict_descendant(parent: Path, child: Path) -> None:
    parent_resolved = parent.resolve(strict=False)
    child_resolved = child.resolve(strict=False)
    if child_resolved == parent_resolved:
        raise ReleaseBuildError("release output must be a strict task-root descendant")
    try:
        child_resolved.relative_to(parent_resolved)
    except ValueError as exception:
        raise ReleaseBuildError("release output escaped the V01-008 task root") from exception
    assert_no_link_components(child, parent)


def initialize_task_root(repository: Path, artifact_root: str) -> Path:
    expected = repository / ".runtime" / "sandbox-storage" / "V01-008"
    task_root = repository / PurePosixPath(artifact_root)
    if task_root.resolve(strict=False) != expected.resolve(strict=False):
        raise ReleaseBuildError("policy artifact_root is not the fixed V01-008 sandbox")
    task_root.mkdir(parents=True, exist_ok=True)
    assert_no_link_components(task_root, repository)
    marker = task_root / TASK_MARKER
    if marker.exists() and marker.read_text(encoding="utf-8") != MARKER_VALUE:
        raise ReleaseBuildError("task-root owner marker does not match")
    marker.write_text(MARKER_VALUE, encoding="utf-8", newline="\n")
    return task_root


def reset_owned_output(task_root: Path, output_root: Path) -> None:
    assert_strict_descendant(task_root, output_root)
    if output_root.exists():
        marker = output_root / OUTPUT_MARKER
        if not marker.is_file() or marker.read_text(encoding="utf-8") != MARKER_VALUE:
            raise ReleaseBuildError("refusing to replace release output without its owner marker")
        assert_no_link_components(output_root, task_root)
        assert_no_link_descendants(output_root)
        shutil.rmtree(output_root)
    output_root.mkdir(parents=True)
    (output_root / OUTPUT_MARKER).write_text(MARKER_VALUE, encoding="utf-8", newline="\n")


def validate_relative_archive_name(name: str) -> PurePosixPath:
    raw_parts = name.split("/")
    relative = PurePosixPath(name)
    if (
        relative.is_absolute()
        or not relative.parts
        or any(part in ("", ".", "..") for part in raw_parts)
        or ":" in relative.parts[0]
    ):
        raise ReleaseBuildError(f"unsafe archive member: {name}")
    return relative


def iter_artifact_files(root: Path) -> Iterable[tuple[Path, PurePosixPath]]:
    for path in sorted(root.rglob("*"), key=lambda item: item.as_posix()):
        if is_link_or_reparse(path):
            raise ReleaseBuildError("artifact contains a link or reparse point")
        if path.is_file():
            relative = validate_relative_archive_name(path.relative_to(root).as_posix())
            yield path, relative


def create_file_manifest(publish_root: Path, runtime_identifiers: Iterable[str]) -> dict[str, Any]:
    entries: list[dict[str, Any]] = []
    runtime_records: dict[str, list[tuple[bytes, str]]] = {}
    for runtime_identifier in runtime_identifiers:
        artifact = publish_root / runtime_identifier
        if not artifact.is_dir():
            raise ReleaseBuildError(f"missing publish directory for {runtime_identifier}")
        records: list[tuple[bytes, str]] = []
        for path, relative in iter_artifact_files(artifact):
            length = path.stat().st_size
            digest = sha256_file(path)
            entries.append(
                {
                    "path": f"{runtime_identifier}/{relative.as_posix()}",
                    "length": length,
                    "sha256": digest,
                }
            )
            relative_name = relative.as_posix()
            records.append(
                (
                    relative_name.encode("utf-8"),
                    f"{digest}\t{length}\t{relative_name}\n",
                )
            )
        runtime_records[runtime_identifier] = records
    entries.sort(key=lambda item: item["path"])
    runtime_aggregates = {
        runtime_identifier: hashlib.sha256(
            "".join(record for _, record in sorted(records)).encode("utf-8")
        ).hexdigest()
        for runtime_identifier, records in runtime_records.items()
    }
    return {
        "files": entries,
        "runtime_aggregate_sha256": runtime_aggregates,
    }


def deterministic_zip(source: Path, destination: Path, prefix: str) -> None:
    prefix_path = validate_relative_archive_name(prefix)
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path, relative in iter_artifact_files(source):
            member = (prefix_path / relative).as_posix()
            info = zipfile.ZipInfo(member, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = (0o755 if path.suffix.lower() == ".exe" else 0o644) << 16
            with path.open("rb") as source_stream, archive.open(info, mode="w", force_zip64=True) as target_stream:
                shutil.copyfileobj(source_stream, target_stream, length=1024 * 1024)


def deterministic_tar_gz(source: Path, destination: Path, prefix: str) -> None:
    prefix_path = validate_relative_archive_name(prefix)
    with destination.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, compresslevel=9, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
                for path, relative in iter_artifact_files(source):
                    member = (prefix_path / relative).as_posix()
                    info = tarfile.TarInfo(member)
                    info.size = path.stat().st_size
                    info.mode = 0o755 if path.name == "AssetLibrary.CoreServer.Host" else 0o644
                    info.mtime = 0
                    info.uid = 0
                    info.gid = 0
                    info.uname = ""
                    info.gname = ""
                    with path.open("rb") as stream:
                        archive.addfile(info, stream)


def run_checked(
    arguments: list[str],
    *,
    cwd: Path,
    environment: dict[str, str],
    capture: bool = False,
) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(
            arguments,
            cwd=cwd,
            env=environment,
            check=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            capture_output=capture,
            timeout=900,
        )
    except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as exception:
        raise ReleaseBuildError(f"command failed: {Path(arguments[0]).name} {arguments[1]}") from exception


def create_source_snapshot(repository: Path, source_revision: str, snapshot_root: Path) -> Path:
    if snapshot_root.exists():
        raise ReleaseBuildError("source snapshot path must not already exist")
    snapshot_root.mkdir(parents=True)
    (snapshot_root / SOURCE_SNAPSHOT_MARKER).write_text(
        SOURCE_SNAPSHOT_MARKER_VALUE,
        encoding="utf-8",
        newline="\n",
    )
    archive_path = snapshot_root / "source.tar"
    source_root = snapshot_root / "tree"
    source_root.mkdir()
    try:
        run_checked(
            [
                "git",
                "-C",
                str(repository),
                "archive",
                "--format=tar",
                f"--output={archive_path}",
                source_revision,
            ],
            cwd=repository,
            environment=os.environ.copy(),
        )
        with tarfile.open(archive_path, mode="r:") as archive:
            for member in archive.getmembers():
                name = member.name
                if (
                    not name
                    or "\\" in name
                    or any(ord(character) < 32 or ord(character) == 127 for character in name)
                ):
                    raise ReleaseBuildError("Git snapshot contains an unsafe path")
                relative = PurePosixPath(name)
                if (
                    relative.is_absolute()
                    or relative.as_posix() != name
                    or any(part in ("", ".", "..") or ":" in part for part in name.split("/"))
                ):
                    raise ReleaseBuildError("Git snapshot contains an unsafe path")
                destination = source_root.joinpath(*relative.parts)
                assert_strict_descendant(source_root, destination)
                if member.isdir():
                    destination.mkdir(parents=True, exist_ok=True)
                    continue
                if not member.isfile():
                    raise ReleaseBuildError("Git snapshot may contain only regular files and directories")
                destination.parent.mkdir(parents=True, exist_ok=True)
                source = archive.extractfile(member)
                if source is None:
                    raise ReleaseBuildError("Git snapshot file could not be read")
                with source, destination.open("xb") as target:
                    shutil.copyfileobj(source, target, length=1024 * 1024)
                destination.chmod(0o755 if member.mode & 0o111 else 0o644)
        assert_msbuild_auto_import_boundary(source_root, reject_ancestor_targets=False)
    except ReleaseBuildError:
        remove_source_snapshot(snapshot_root)
        raise
    except (OSError, tarfile.TarError) as exception:
        remove_source_snapshot(snapshot_root)
        raise ReleaseBuildError("Git source snapshot could not be materialized") from exception
    finally:
        if archive_path.exists():
            archive_path.unlink()
    return source_root


def remove_source_snapshot(snapshot_root: Path) -> None:
    marker = snapshot_root / SOURCE_SNAPSHOT_MARKER
    if not marker.is_file() or marker.read_text(encoding="utf-8") != SOURCE_SNAPSHOT_MARKER_VALUE:
        raise ReleaseBuildError("refusing to remove an unowned source snapshot")
    assert_no_link_descendants(snapshot_root)
    shutil.rmtree(snapshot_root)


def git_output(repository: Path, *arguments: str) -> str:
    result = run_checked(
        ["git", "-C", str(repository), *arguments],
        cwd=repository,
        environment=os.environ.copy(),
        capture=True,
    )
    return result.stdout.strip()


def load_issuance_inputs(repository: Path) -> tuple[str, ...]:
    try:
        policy = json.loads((repository / "eng/server-release-policy.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise ReleaseBuildError("release policy could not provide issuance inputs") from exception
    values = policy.get("issuance_inputs")
    if not isinstance(values, list) or not values:
        raise ReleaseBuildError("release policy must define issuance_inputs")
    inputs: list[str] = []
    for value in values:
        if not isinstance(value, str) or "\\" in value:
            raise ReleaseBuildError("issuance inputs must use canonical repository-relative paths")
        relative = PurePosixPath(value)
        if (
            relative.is_absolute()
            or not relative.parts
            or relative.as_posix() != value
            or any(part in ("", ".", "..") for part in value.split("/"))
        ):
            raise ReleaseBuildError("issuance inputs must use canonical repository-relative paths")
        inputs.append(value)
    if len(inputs) != len(set(inputs)):
        raise ReleaseBuildError("issuance inputs must not contain duplicates")
    if not REQUIRED_MSBUILD_AUTO_IMPORTS.issubset(inputs):
        raise ReleaseBuildError("issuance inputs must cover both Directory.Build auto-imports")
    if tuple(inputs) != REQUIRED_ISSUANCE_INPUTS:
        raise ReleaseBuildError("issuance inputs must match the protected release input set")
    return tuple(inputs)


def assert_msbuild_auto_import_boundary(
    repository: Path,
    *,
    reject_ancestor_targets: bool = True,
) -> None:
    repository = repository.resolve(strict=True)
    props = repository / "Directory.Build.props"
    targets = repository / "Directory.Build.targets"
    if not props.is_file() or is_link_or_reparse(props):
        raise ReleaseBuildError("repository-owned Directory.Build.props is required and may not be a link")
    if targets.exists() and (not targets.is_file() or is_link_or_reparse(targets)):
        raise ReleaseBuildError("repository-owned Directory.Build.targets must be a regular file")

    if not reject_ancestor_targets:
        return
    current = repository.parent
    while True:
        candidate = current / "Directory.Build.targets"
        if candidate.exists() or is_link_or_reparse(candidate):
            raise ReleaseBuildError("Directory.Build.targets outside the repository is forbidden")
        if current.parent == current:
            break
        current = current.parent


def resolve_provenance(repository: Path) -> tuple[str, str]:
    issuance_inputs = load_issuance_inputs(repository)
    assert_msbuild_auto_import_boundary(repository)
    dirty = git_output(
        repository,
        "status",
        "--porcelain=v1",
        "--untracked-files=all",
        "--",
        *issuance_inputs,
    )
    if dirty:
        raise ReleaseBuildError("all issuance inputs must be committed before a release build")
    source_revision = git_output(
        repository,
        "log",
        "-1",
        "--format=%H",
        "--",
        *issuance_inputs,
    )
    repository_head = git_output(repository, "rev-parse", "HEAD")
    if len(source_revision) != 40 or len(repository_head) != 40:
        raise ReleaseBuildError("could not resolve full Git provenance")
    drift = git_output(
        repository,
        "diff",
        "--name-only",
        source_revision,
        "--",
        *issuance_inputs,
    )
    if drift:
        raise ReleaseBuildError("issuance inputs differ from the selected source revision")
    return source_revision, repository_head


def issuance_input_tree_sha256(repository: Path, source_revision: str) -> str:
    issuance_inputs = load_issuance_inputs(repository)
    tree = git_output(
        repository,
        "ls-tree",
        "-r",
        "--full-tree",
        source_revision,
        "--",
        *issuance_inputs,
    )
    if not tree:
        raise ReleaseBuildError("issuance input tree is empty")
    return hashlib.sha256((tree + "\n").encode("utf-8")).hexdigest()


def dotnet_environment(task_root: Path, dotnet: Path) -> dict[str, str]:
    environment = os.environ.copy()
    overrides = sorted(
        name
        for name in environment
        if name.casefold() in MSBUILD_IMPORT_OVERRIDE_ENVIRONMENT and environment[name]
    )
    if overrides:
        raise ReleaseBuildError(
            "external MSBuild import overrides are forbidden: " + ", ".join(overrides)
        )
    environment.update(
        {
            "CI": "true",
            "DOTNET_CLI_HOME": str(task_root / "dotnet-home"),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_CLI_UI_LANGUAGE": "en-US",
            "DOTNET_NOLOGO": "1",
            "DOTNET_ROOT": str(dotnet.parent),
            "MSBUILDDISABLENODEREUSE": "1",
            "NUGET_PACKAGES": str(task_root / "nuget"),
        }
    )
    return environment


def resolve_dotnet(candidate: str | None, expected_sdk: str, task_root: Path) -> tuple[Path, dict[str, str]]:
    selected = candidate or os.environ.get("ASSETLIBRARY_RELEASE_DOTNET") or shutil.which("dotnet")
    if not selected:
        raise ReleaseBuildError("dotnet was not found; pass --dotnet or ASSETLIBRARY_RELEASE_DOTNET")
    dotnet = Path(selected).resolve(strict=True)
    environment = dotnet_environment(task_root, dotnet)
    result = run_checked([str(dotnet), "--version"], cwd=ROOT, environment=environment, capture=True)
    if result.stdout.strip() != expected_sdk:
        raise ReleaseBuildError(f"required .NET SDK is {expected_sdk}")
    return dotnet, environment


def shutdown_build_servers(dotnet: Path, environment: dict[str, str]) -> None:
    run_checked(
        [str(dotnet), "build-server", "shutdown"],
        cwd=ROOT,
        environment=environment,
        capture=True,
    )


def build_properties(repository: Path, source_revision: str) -> list[str]:
    return [
        "-p:ContinuousIntegrationBuild=true",
        "-p:Deterministic=true",
        "-p:DeterministicSourcePaths=true",
        "-p:IncludeSourceRevisionInInformationalVersion=true",
        "-p:UseSharedCompilation=false",
        f"-p:SourceRevisionId={source_revision}",
        f"-p:AssetLibrarySourceRevision={source_revision}",
        f"-p:DirectoryBuildPropsPath={repository / 'Directory.Build.props'}",
        f"-p:DirectoryBuildTargetsPath={repository / 'Directory.Build.targets'}",
        f"-p:PathMap={repository}=/_/src",
    ]


def release_lock_name(project_name: str, runtime_identifier: str) -> str:
    return f"{project_name}.{runtime_identifier}.lock.json"


def validate_release_lock(
    generated_path: Path,
    base_path: Path,
    runtime_identifier: str,
) -> None:
    try:
        generated = json.loads(generated_path.read_text(encoding="utf-8"))
        base = json.loads(base_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise ReleaseBuildError("release lock is missing or invalid") from exception
    generated_groups = generated.get("dependencies", {})
    base_groups = base.get("dependencies", {})
    expected_groups = {"net10.0", f"net10.0/{runtime_identifier}"}
    if set(generated_groups) != expected_groups:
        raise ReleaseBuildError("release lock target groups differ from the requested RID")
    if set(base_groups) != {"net10.0"} or generated_groups.get("net10.0") != base_groups.get("net10.0"):
        raise ReleaseBuildError("release lock changed the committed neutral dependency graph")
    for details in generated_groups[f"net10.0/{runtime_identifier}"].values():
        if not isinstance(details, dict):
            raise ReleaseBuildError("release lock contains an invalid RID dependency")
        if details.get("type") == "Project":
            continue
        digest = details.get("contentHash")
        version = details.get("resolved")
        if not isinstance(version, str) or not version:
            raise ReleaseBuildError("release lock contains an unversioned RID dependency")
        if not isinstance(digest, str) or len(digest) < 80:
            raise ReleaseBuildError("release lock contains an invalid package content hash")


def prepare_release_locks(
    *,
    dotnet: Path,
    environment: dict[str, str],
    project: Path,
    repository: Path,
    output_root: Path,
    runtime_identifiers: list[str],
    source_revision: str,
) -> tuple[Path, dict[str, Any]]:
    lock_root = output_root / "release-locks"
    lock_root.mkdir()
    base_hashes = {
        name: sha256_file(repository / relative)
        for name, relative in BASE_LOCKS.items()
    }
    neutral_artifacts = output_root / "lock-bootstrap" / "neutral"
    run_checked(
        [
            str(dotnet),
            "restore",
            str(project),
            "--locked-mode",
            "--packages",
            environment["NUGET_PACKAGES"],
            "--artifacts-path",
            str(neutral_artifacts),
            "--disable-build-servers",
            *build_properties(repository, source_revision),
        ],
        cwd=repository,
        environment=environment,
    )

    entries: list[dict[str, Any]] = []
    for runtime_identifier in runtime_identifiers:
        for project_name, relative in BASE_LOCKS.items():
            shutil.copy2(
                repository / relative,
                lock_root / release_lock_name(project_name, runtime_identifier),
            )
        run_checked(
            [
                str(dotnet),
                "restore",
                str(project),
                "--force-evaluate",
                "--packages",
                environment["NUGET_PACKAGES"],
                "--artifacts-path",
                str(output_root / "lock-bootstrap" / runtime_identifier),
                "--disable-build-servers",
                f"-p:RuntimeIdentifier={runtime_identifier}",
                "-p:SelfContained=true",
                f"-p:AssetLibraryReleaseLockRoot={lock_root}",
                *build_properties(repository, source_revision),
            ],
            cwd=repository,
            environment=environment,
        )
        for project_name, relative in BASE_LOCKS.items():
            lock_path = lock_root / release_lock_name(project_name, runtime_identifier)
            validate_release_lock(lock_path, repository / relative, runtime_identifier)
            entries.append(
                {
                    "path": lock_path.relative_to(output_root).as_posix(),
                    "length": lock_path.stat().st_size,
                    "sha256": sha256_file(lock_path),
                    "runtime_identifier": runtime_identifier,
                    "project": project_name,
                }
            )
    for project_name, relative in BASE_LOCKS.items():
        if sha256_file(repository / relative) != base_hashes[project_name]:
            raise ReleaseBuildError("release lock bootstrap modified a committed project lock")
    expected_lock_names = {
        release_lock_name(project_name, runtime_identifier)
        for project_name in BASE_LOCKS
        for runtime_identifier in runtime_identifiers
    }
    actual_lock_names = {
        path.name
        for path in lock_root.iterdir()
        if path.is_file()
    }
    if actual_lock_names != expected_lock_names:
        raise ReleaseBuildError("release lock bootstrap produced an unexpected project/RID matrix")
    lock_manifest = {
        "contract": "v01-008/1",
        "source_revision": source_revision,
        "locks": sorted(entries, key=lambda item: item["path"]),
    }
    write_json(output_root / "release-locks.json", lock_manifest)
    lock_manifest["aggregate_sha256"] = hashlib.sha256(canonical_json_bytes(lock_manifest)).hexdigest()
    (output_root / "release-locks-aggregate-sha256.txt").write_text(
        lock_manifest["aggregate_sha256"] + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return lock_root, lock_manifest


def build_cold_run(
    *,
    dotnet: Path,
    environment: dict[str, str],
    project: Path,
    repository: Path,
    run_root: Path,
    runtime_identifiers: list[str],
    source_revision: str,
    issuance_input_tree_digest: str,
    release_lock_root: Path,
    release_lock_aggregate: str,
) -> dict[str, Any]:
    publish_root = run_root / "publish"
    run_lock_root = run_root / "release-locks"
    shutil.copytree(release_lock_root, run_lock_root)
    original_lock_hashes = {
        path.name: sha256_file(path)
        for path in run_lock_root.iterdir()
        if path.is_file()
    }
    properties = build_properties(repository, source_revision)
    shutdown_build_servers(dotnet, environment)
    try:
        for runtime_identifier in runtime_identifiers:
            artifact_path = run_root / "build" / runtime_identifier
            output_path = publish_root / runtime_identifier
            restore = [
                str(dotnet),
                "restore",
                str(project),
                "--locked-mode",
                "--packages",
                environment["NUGET_PACKAGES"],
                "--artifacts-path",
                str(artifact_path),
                "--disable-build-servers",
                f"-p:RuntimeIdentifier={runtime_identifier}",
                "-p:SelfContained=true",
                f"-p:AssetLibraryReleaseLockRoot={run_lock_root}",
                *properties,
            ]
            run_checked(restore, cwd=repository, environment=environment)
            publish = [
                str(dotnet),
                "publish",
                str(project),
                "--configuration",
                "Release",
                "--self-contained",
                "true",
                "--no-restore",
                "--artifacts-path",
                str(artifact_path),
                "--output",
                str(output_path),
                "--disable-build-servers",
                "-p:DebugType=None",
                "-p:DebugSymbols=false",
                f"-p:RuntimeIdentifier={runtime_identifier}",
                f"-p:AssetLibraryReleaseLockRoot={run_lock_root}",
                *properties,
            ]
            run_checked(publish, cwd=repository, environment=environment)
    finally:
        shutdown_build_servers(dotnet, environment)

    final_lock_hashes = {
        path.name: sha256_file(path)
        for path in run_lock_root.iterdir()
        if path.is_file()
    }
    if final_lock_hashes != original_lock_hashes:
        raise ReleaseBuildError("locked restore modified the release-lock set")

    manifest = create_file_manifest(publish_root, runtime_identifiers)
    manifest.update(
        {
            "contract": "v01-008/1",
            "source_revision": source_revision,
            "issuance_input_tree_sha256": issuance_input_tree_digest,
            "release_lock_aggregate_sha256": release_lock_aggregate,
        }
    )
    manifest["runtime_evidence_binding_sha256"] = {
        runtime_identifier: runtime_evidence_binding_sha256(
            source_revision,
            runtime_identifier,
            manifest["runtime_aggregate_sha256"][runtime_identifier],
        )
        for runtime_identifier in runtime_identifiers
    }
    write_json(run_root / "manifest.json", manifest)
    aggregate = hashlib.sha256(canonical_json_bytes(manifest)).hexdigest()
    (run_root / "aggregate-sha256.txt").write_text(aggregate + "\n", encoding="utf-8", newline="\n")
    return {"manifest": manifest, "aggregate_sha256": aggregate, "publish_root": publish_root}


def current_runtime_identifier() -> str | None:
    machine = platform.machine().lower()
    if machine not in ("amd64", "x86_64"):
        return None
    if os.name == "nt":
        return "win-x64"
    if sys.platform.startswith("linux"):
        return "linux-x64"
    return None


def host_executable_path(publish_root: Path, runtime_identifier: str) -> Path:
    name = "AssetLibrary.CoreServer.Host.exe" if runtime_identifier == "win-x64" else "AssetLibrary.CoreServer.Host"
    return publish_root / runtime_identifier / name


def read_build_info(
    publish_root: Path,
    runtime_identifier: str,
    environment: dict[str, str],
) -> dict[str, Any]:
    executable = host_executable_path(publish_root, runtime_identifier)
    result = run_checked(
        [str(executable), "--build-info"],
        cwd=publish_root,
        environment=environment,
        capture=True,
    )
    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError as exception:
        raise ReleaseBuildError("host --build-info did not return JSON") from exception
    return payload


def promote_artifacts(
    output_root: Path,
    run: dict[str, Any],
    runtime_identifiers: list[str],
    source_revision: str,
    repository_head: str,
    issuance_input_tree_digest: str,
    policy: dict[str, Any],
    environment: dict[str, str],
    release_lock_aggregate: str,
) -> dict[str, Any]:
    artifact_root = output_root / "artifacts"
    for runtime_identifier in runtime_identifiers:
        shutil.copytree(
            run["publish_root"] / runtime_identifier,
            artifact_root / runtime_identifier,
        )

    manifest = run["manifest"]
    write_json(output_root / "manifest.json", manifest)
    (output_root / "aggregate-sha256.txt").write_text(
        run["aggregate_sha256"] + "\n", encoding="utf-8", newline="\n"
    )
    metadata = {
        "contract": policy["contract"],
        "source_revision": source_revision,
        "issuance_input_tree_sha256": issuance_input_tree_digest,
        "host_project": policy["host_project"],
        "runtime_identifiers": runtime_identifiers,
        "runtime_aggregate_sha256": manifest["runtime_aggregate_sha256"],
        "runtime_evidence_binding_sha256": manifest["runtime_evidence_binding_sha256"],
        "self_contained": True,
        "configuration": "Release",
        "dotnet_sdk": policy["dotnet_sdk"],
        "release_lock_aggregate_sha256": release_lock_aggregate,
    }
    write_json(output_root / "release-metadata.json", metadata)

    archives = output_root / "archives"
    archives.mkdir()
    archive_entries: list[dict[str, Any]] = []
    for runtime_identifier in runtime_identifiers:
        prefix = f"assetlibrary-core-server-{runtime_identifier}"
        if runtime_identifier == "win-x64":
            archive_path = archives / f"{prefix}-{source_revision[:12]}.zip"
            deterministic_zip(artifact_root / runtime_identifier, archive_path, prefix)
        else:
            archive_path = archives / f"{prefix}-{source_revision[:12]}.tar.gz"
            deterministic_tar_gz(artifact_root / runtime_identifier, archive_path, prefix)
        archive_entries.append(
            {
                "path": archive_path.relative_to(output_root).as_posix(),
                "length": archive_path.stat().st_size,
                "sha256": sha256_file(archive_path),
                "runtime_identifier": runtime_identifier,
            }
        )
    archive_manifest = {
        "contract": policy["contract"],
        "source_revision": source_revision,
        "issuance_input_tree_sha256": issuance_input_tree_digest,
        "artifact_aggregate_sha256": run["aggregate_sha256"],
        "release_lock_aggregate_sha256": release_lock_aggregate,
        "archives": archive_entries,
    }
    write_json(output_root / "archives.json", archive_manifest)

    executable_runtime = current_runtime_identifier()
    build_info: dict[str, Any] | None = None
    if executable_runtime in runtime_identifiers:
        build_info = read_build_info(artifact_root, executable_runtime, environment)
        if build_info.get("contract") != policy["contract"]:
            raise ReleaseBuildError("host build-info contract differs from release policy")
        if build_info.get("source_revision") != source_revision:
            raise ReleaseBuildError("host binary is not bound to the issuance source revision")

    evidence = {
        "contract": policy["contract"],
        "status": "passed",
        "target": "native_artifacts",
        "source_revision": source_revision,
        "repository_head_at_build": repository_head,
        "issuance_input_tree_sha256": issuance_input_tree_digest,
        "cold_publish_runs": policy["cold_publish_runs"],
        "aggregate_sha256": run["aggregate_sha256"],
        "release_lock_aggregate_sha256": release_lock_aggregate,
        "file_count": len(manifest["files"]),
        "runtime_aggregate_sha256": manifest["runtime_aggregate_sha256"],
        "runtime_evidence_binding_sha256": manifest["runtime_evidence_binding_sha256"],
        "build_info": build_info,
        "platform_evidence": {
            "windows_service": "blocked_missing_environment",
            "docker": "blocked_missing_environment",
            "linux_systemd": "blocked_missing_environment",
        },
    }
    write_json(output_root / "build-evidence.json", evidence)
    return evidence


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", help="Path to the exact dotnet executable")
    parser.add_argument("--output-root", type=Path, help="Task-sandbox output directory")
    return parser.parse_args()


def main() -> int:
    arguments = parse_arguments()
    try:
        policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
        task_root = initialize_task_root(ROOT, policy["artifact_root"])
        output_root = arguments.output_root or task_root / "release-build"
        output_root = output_root.absolute()
        assert_strict_descendant(task_root, output_root)
        source_revision, repository_head = resolve_provenance(ROOT)
        issuance_input_tree_digest = issuance_input_tree_sha256(ROOT, source_revision)
        reset_owned_output(task_root, output_root)
        snapshot_root = output_root / "source-snapshot"
        source_root = create_source_snapshot(ROOT, source_revision, snapshot_root)
        try:
            snapshot_policy = json.loads(
                (source_root / "eng/server-release-policy.json").read_text(encoding="utf-8")
            )
            if snapshot_policy != policy:
                raise ReleaseBuildError("live release policy differs from the Git source snapshot")
            policy = snapshot_policy
            dotnet, environment = resolve_dotnet(arguments.dotnet, policy["dotnet_sdk"], task_root)
            runtime_identifiers = list(policy["runtime_identifiers"])
            shutdown_build_servers(dotnet, environment)
            try:
                release_lock_root, release_lock_manifest = prepare_release_locks(
                    dotnet=dotnet,
                    environment=environment,
                    project=source_root / policy["host_project"],
                    repository=source_root,
                    output_root=output_root,
                    runtime_identifiers=runtime_identifiers,
                    source_revision=source_revision,
                )
            finally:
                shutdown_build_servers(dotnet, environment)
            runs: list[dict[str, Any]] = []
            for index in range(policy["cold_publish_runs"]):
                run_root = output_root / "cold-runs" / f"run-{index + 1}"
                run_root.mkdir(parents=True)
                runs.append(
                    build_cold_run(
                        dotnet=dotnet,
                        environment=environment,
                        project=source_root / policy["host_project"],
                        repository=source_root,
                        run_root=run_root,
                        runtime_identifiers=runtime_identifiers,
                        source_revision=source_revision,
                        issuance_input_tree_digest=issuance_input_tree_digest,
                        release_lock_root=release_lock_root,
                        release_lock_aggregate=release_lock_manifest["aggregate_sha256"],
                    )
                )
            if any(run["manifest"] != runs[0]["manifest"] for run in runs[1:]):
                raise ReleaseBuildError("independent cold-publish manifests differ")
            if any(run["aggregate_sha256"] != runs[0]["aggregate_sha256"] for run in runs[1:]):
                raise ReleaseBuildError("independent cold-publish aggregate digests differ")

            evidence = promote_artifacts(
                output_root,
                runs[0],
                runtime_identifiers,
                source_revision,
                repository_head,
                issuance_input_tree_digest,
                policy,
                environment,
                release_lock_manifest["aggregate_sha256"],
            )
        finally:
            remove_source_snapshot(snapshot_root)
        print(json.dumps(evidence, ensure_ascii=True, sort_keys=True))
        return 0
    except (KeyError, OSError, ReleaseBuildError, json.JSONDecodeError) as exception:
        print(json.dumps({"contract": "v01-008/1", "status": "failed", "error": str(exception)}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
