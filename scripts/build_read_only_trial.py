#!/usr/bin/env python3
"""Build a Windows x64 read-only trial from one clean Git commit."""
from __future__ import annotations

import sys

if not sys.flags.isolated:
    raise SystemExit("trial packaging requires Python isolated mode (-I)")

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("server_release_builder", ROOT / "scripts/build_server_release.py")
assert SPEC and SPEC.loader
RELEASE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RELEASE)
PACKAGE_NAME = "assetlibrary-read-only-trial-win-x64"


def clean_revision(repository: Path) -> str:
    RELEASE.assert_msbuild_auto_import_boundary(repository)
    if RELEASE.git_output(repository, "status", "--porcelain=v1", "--untracked-files=all"):
        raise RELEASE.ReleaseBuildError("trial source must be a clean committed worktree")
    return RELEASE.git_output(repository, "rev-parse", "HEAD").strip()


def create_output(repository: Path, output: Path) -> None:
    boundary = repository / ".runtime/sandbox-storage/V01-019"
    RELEASE.assert_strict_descendant(boundary, output)
    RELEASE.assert_no_link_components(output, repository)
    if output.exists():
        raise RELEASE.ReleaseBuildError("trial build output already exists; choose a new directory")
    output.mkdir(parents=True)


def package_manifest(package: Path, revision: str, version: str, build_info: dict) -> dict:
    return {
        "format_version": 1,
        "product": "AssetLibrary/read-only-trial",
        "runtime_identifier": "win-x64",
        "version": version,
        "source_revision": revision,
        "production_file_writes_enabled": False,
        "formal_release": False,
        "build_info": build_info,
        "files": [
            {"path": relative.as_posix(), "length": path.stat().st_size, "sha256": RELEASE.sha256_file(path)}
            for path, relative in RELEASE.iter_artifact_files(package)
            if relative.as_posix() != "package-manifest.json"
        ],
    }


def build_web(source: Path, node: Path, pnpm: Path, output: Path) -> None:
    environment = os.environ.copy()
    environment["PATH"] = str(node.parent) + os.pathsep + environment.get("PATH", "")
    environment["CI"] = "true"
    environment["COREPACK_ENABLE_PROJECT_SPEC"] = "0"
    version = RELEASE.run_checked([str(node), "--version"], cwd=source, environment=environment, capture=True)
    if version.stdout.strip() != "v24.20.0":
        raise RELEASE.ReleaseBuildError("trial Web build requires Node.js 24.20.0")
    command = [str(node), str(pnpm)]
    version = RELEASE.run_checked([*command, "--version"], cwd=source, environment=environment, capture=True)
    if version.stdout.strip() != "11.19.0":
        raise RELEASE.ReleaseBuildError("trial Web build requires pnpm 11.19.0 (pass its pnpm.cjs)")
    for directory in ("packages/sdk/assetlink/typescript", "apps/web"):
        RELEASE.run_checked(
            [*command, "--dir", directory, "install", "--frozen-lockfile", "--ignore-scripts"],
            cwd=source, environment=environment,
        )
    # Invoke the already-declared build tools directly so a global pnpm shim cannot select another Node.
    for arguments, directory in (
        ([str(node), "node_modules/typescript/bin/tsc", "--project", "tsconfig.json", "--pretty", "false"], "packages/sdk/assetlink/typescript"),
        ([str(node), "node_modules/typescript/bin/tsc", "--project", "tsconfig.json", "--noEmit", "--pretty", "false"], "apps/web"),
        ([str(node), "node_modules/vite/bin/vite.js", "build"], "apps/web"),
    ):
        RELEASE.run_checked(arguments, cwd=source / directory, environment=environment)
    shutil.copytree(source / "apps/web/dist", output)


def build(args: argparse.Namespace) -> dict:
    if os.name != "nt":
        raise RELEASE.ReleaseBuildError("native trial packaging requires Windows x64")
    revision = clean_revision(ROOT)
    output = args.output_root.absolute()
    create_output(ROOT, output)
    source = RELEASE.create_source_snapshot(ROOT, revision, output / "source")
    policy = json.loads((source / "eng/server-release-policy.json").read_text(encoding="utf-8"))
    dotnet, environment = RELEASE.resolve_dotnet(str(args.dotnet), policy["dotnet_sdk"], output)
    environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false"
    environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    if args.nuget_packages:
        environment["NUGET_PACKAGES"] = str(args.nuget_packages.resolve(strict=True))
    project = source / policy["host_project"]
    locks, lock_manifest = RELEASE.prepare_release_locks(
        dotnet=dotnet, environment=environment, project=project, repository=source,
        output_root=output, runtime_identifiers=["win-x64"], source_revision=revision,
    )
    run_root = output / "native"
    run_root.mkdir()
    source_tree = RELEASE.git_output(ROOT, "rev-parse", "HEAD^{tree}").strip()
    run = RELEASE.build_cold_run(
        dotnet=dotnet, environment=environment, project=project, repository=source,
        run_root=run_root, runtime_identifiers=["win-x64"], source_revision=revision,
        issuance_input_tree_digest=hashlib.sha256(source_tree.encode("ascii")).hexdigest(),
        release_lock_root=locks, release_lock_aggregate=lock_manifest["aggregate_sha256"],
    )
    package = output / PACKAGE_NAME
    package.mkdir()
    shutil.copytree(run["publish_root"] / "win-x64", package / "host")
    build_web(source, args.node.resolve(strict=True), args.pnpm.resolve(strict=True), package / "web")
    shutil.copytree(source / "database/migrations/production", package / "migrations")
    for path in (source / "infra/windows/trial").iterdir():
        if not path.is_file() or RELEASE.is_link_or_reparse(path):
            raise RELEASE.ReleaseBuildError("trial deployment scripts must be regular files")
        shutil.copy2(path, package / path.name)
    build_info = RELEASE.read_build_info(run["publish_root"], "win-x64", environment)
    if build_info.get("source_revision") != revision:
        raise RELEASE.ReleaseBuildError("Host build provenance differs from the Web source commit")
    version = json.loads((source / "apps/web/package.json").read_text(encoding="utf-8"))["version"]
    manifest = package_manifest(package, revision, version, build_info)
    manifest["source_tree"] = source_tree
    manifest["tools"] = {"dotnet_sdk": policy["dotnet_sdk"], "node": "24.20.0", "pnpm": "11.19.0"}
    RELEASE.write_json(package / "package-manifest.json", manifest)
    archive = output / f"{PACKAGE_NAME}.zip"
    RELEASE.deterministic_zip(package, archive, PACKAGE_NAME)
    evidence = {
        "format_version": 1, "status": "built", "source_revision": revision,
        "source_tree": source_tree, "package": str(package), "archive": str(archive),
        "archive_sha256": RELEASE.sha256_file(archive),
        "manifest_sha256": RELEASE.sha256_file(package / "package-manifest.json"),
        "native_runtime_validation": "not_run_by_builder", "formal_release": False,
    }
    RELEASE.write_json(output / "build-evidence.json", evidence)
    return evidence


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--node", type=Path, required=True)
    parser.add_argument("--pnpm", type=Path, required=True, help="Path to the pinned pnpm.cjs entry point")
    parser.add_argument("--nuget-packages", type=Path)
    parser.add_argument("--output-root", type=Path, required=True)
    try:
        print(json.dumps(build(parser.parse_args()), sort_keys=True))
        return 0
    except (OSError, ValueError, KeyError, RELEASE.ReleaseBuildError, subprocess.SubprocessError) as error:
        print(json.dumps({"status": "failed", "error": str(error)}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
