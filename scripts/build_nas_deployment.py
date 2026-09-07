#!/usr/bin/env python3
"""Build NAS images and an offline Compose delivery from one committed source snapshot."""
from __future__ import annotations

import sys

if not sys.flags.isolated:
    raise SystemExit("NAS packaging requires Python isolated mode (-I)")

import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("nas_release_primitives", ROOT / "scripts/build_server_release.py")
assert SPEC and SPEC.loader
RELEASE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RELEASE)
BASE_IMAGES = {
    "DEBIAN_IMAGE": "debian:bookworm-slim",
    "NODE_IMAGE": "node:24.20.0-bookworm-slim",
    "PYTHON_IMAGE": "python:3.13-slim-bookworm",
    "POSTGRES_IMAGE": "postgres:16.15-bookworm",
}


def command(arguments: list[str], *, cwd: Path, capture: bool = False) -> str:
    result = RELEASE.run_checked(arguments, cwd=cwd, environment=os.environ.copy(), capture=capture)
    return result.stdout.strip() if capture else ""


def save_images_archive(docker: str, tags: list[str], destination: Path, source: Path) -> None:
    # The caller owns the file descriptor even when its Docker CLI uses sudo.
    # docker save --output would instead create a root-owned, often 0600 archive.
    with destination.open("xb") as stream:
        subprocess.run([docker, "save", *tags], cwd=source, stdout=stream, check=True, timeout=900)
        stream.flush()
        os.fsync(stream.fileno())


def build(args: argparse.Namespace) -> dict:
    RELEASE.assert_msbuild_auto_import_boundary(ROOT)
    if RELEASE.git_output(ROOT, "status", "--porcelain=v1", "--untracked-files=all"):
        raise RELEASE.ReleaseBuildError("NAS delivery source must be committed and clean")
    revision = RELEASE.git_output(ROOT, "rev-parse", "HEAD").strip()
    tree = RELEASE.git_output(ROOT, "rev-parse", "HEAD^{tree}").strip()
    output = args.output_root.absolute()
    boundary = ROOT / ".runtime/sandbox-storage/V01-022"
    RELEASE.assert_strict_descendant(boundary, output)
    RELEASE.assert_no_link_components(output, ROOT)
    if output.exists():
        raise RELEASE.ReleaseBuildError("NAS output already exists; choose a new owned directory")
    output.mkdir(parents=True)
    source = RELEASE.create_source_snapshot(ROOT, revision, output / "source")
    if args.context_only:
        archive = output / "source-context.tar"
        with tarfile.open(archive, "w") as context:
            for path, relative in RELEASE.iter_artifact_files(source):
                context.add(path, arcname=relative.as_posix(), recursive=False)
        bundle = output / "source.bundle"
        command(["git", "bundle", "create", str(bundle), "HEAD"], cwd=ROOT)
        result = {"status": "context_prepared_not_built", "source_revision": revision, "source_tree": tree,
                  "context": str(archive), "sha256": RELEASE.sha256_file(archive),
                  "git_bundle": str(bundle), "git_bundle_sha256": RELEASE.sha256_file(bundle)}
        RELEASE.write_json(output / "context.json", result)
        return result
    docker = args.docker or shutil.which("docker")
    if not docker:
        raise RELEASE.ReleaseBuildError("Docker CLI is required for the explicitly requested image build")
    resolved = {}
    for argument, image in BASE_IMAGES.items():
        command([docker, "pull", "--platform", "linux/amd64", image], cwd=source)
        info = json.loads(command([docker, "image", "inspect", image], cwd=source, capture=True))[0]
        if info.get("Architecture") != "amd64" or not info.get("RepoDigests"):
            raise RELEASE.ReleaseBuildError("base image has no immutable amd64 provenance")
        resolved[argument] = {"requested": image, "digest": info["RepoDigests"][0], "id": info["Id"]}
    tags = {name: f"assetlibrary/nas-{name}:{revision}" for name in ("core", "setup", "postgres")}
    image_ids = {}
    for stage in ("core", "setup"):
        invocation = [docker, "build", "--platform", "linux/amd64", "--file", "infra/docker/nas/Dockerfile",
                      "--target", stage, "--tag", tags[stage], "--build-arg", "SOURCE_REVISION=" + revision]
        for argument, identity in resolved.items():
            invocation += ["--build-arg", argument + "=" + identity["digest"]]
        command([*invocation, "."], cwd=source)
        info = json.loads(command([docker, "image", "inspect", tags[stage]], cwd=source, capture=True))[0]
        if info["Config"]["Labels"].get("org.opencontainers.image.revision") != revision:
            raise RELEASE.ReleaseBuildError("built image source label mismatch")
        image_ids[stage] = info["Id"]
    command([docker, "tag", resolved["POSTGRES_IMAGE"]["digest"], tags["postgres"]], cwd=source)
    image_ids["postgres"] = resolved["POSTGRES_IMAGE"]["id"]
    bundle = output / "assetlibrary-nas"
    bundle.mkdir()
    for filename in ("compose.yaml", "nasctl.sh", "settings.example.json", "README.md"):
        shutil.copy2(source / "infra/docker/nas" / filename, bundle / filename)
    (bundle / "nasctl.sh").chmod(0o755)
    env = ["ASSETLIBRARY_SOURCE_REVISION=" + revision]
    for name in tags:
        env += [f"ASSETLIBRARY_{name.upper()}_IMAGE={tags[name]}", f"ASSETLIBRARY_{name.upper()}_IMAGE_ID={image_ids[name]}"]
    (bundle / "images.env").write_text("\n".join(env) + "\n", encoding="utf-8", newline="\n")
    save_images_archive(docker, list(tags.values()), bundle / "images.tar", source)
    manifest = {"format_version": 1, "product": "AssetLibrary/NAS/read-only/v1", "source_revision": revision,
                "source_tree": tree, "base_images": resolved, "images": tags, "image_ids": image_ids,
                "asset_writes": False, "native_deployment_validation": "not_executed_by_builder"}
    RELEASE.write_json(bundle / "build-manifest.json", manifest)
    lines = [f"{RELEASE.sha256_file(path)}  {relative.as_posix()}" for path, relative in RELEASE.iter_artifact_files(bundle)]
    (bundle / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="ascii", newline="\n")
    result = {"status": "built_not_deployed", "source_revision": revision, "source_tree": tree, "bundle": str(bundle),
              "images_tar_sha256": RELEASE.sha256_file(bundle / "images.tar"), "image_ids": image_ids}
    RELEASE.write_json(output / "build-evidence.json", result)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-root", required=True, type=Path)
    parser.add_argument("--docker")
    parser.add_argument("--context-only", action="store_true")
    try:
        print(json.dumps(build(parser.parse_args()), sort_keys=True))
        return 0
    except (OSError, ValueError, KeyError, RELEASE.ReleaseBuildError, subprocess.SubprocessError) as error:
        print(json.dumps({"status": "failed", "error": str(error)}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
