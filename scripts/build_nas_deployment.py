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
import re
import shutil
import subprocess
import tarfile
import uuid


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
IMAGE_PREVIEW_PATH = "/app/workers/image-preview"
IMAGE_PREVIEW_FILES = (
    "AssetLibrary.ImagePreview.Worker", "libSkiaSharp.so",
    "LICENSE.SkiaSharp.txt", "THIRD-PARTY-NOTICES.SkiaSharp.txt",
)

IMAGE_SUPERVISOR_PATH = "/app/image-supervisor"
IMAGE_SUPERVISOR_FILES = ("AssetLibrary.ImageSupervisor", "LICENSE.NET.txt", "THIRD-PARTY-NOTICES.NET.txt")
IMAGE_SOCKET_PATH = "/run/assetlibrary-image/decoder.sock"
IMAGE_ENV_KEYS = {"core": "CORE", "setup": "SETUP", "postgres": "POSTGRES", "image": "IMAGE_PREVIEW"}


def _native_image_manifest(package: Path, revision: str, source: Path, image_id: str, *,
                           payload: tuple[str, ...], native: tuple[str, ...], directory: str,
                           lock_relative: str, component: str) -> dict:
    """Verify the actual image payload without executing the decoder or enabling it."""
    RELEASE.assert_no_link_components(package, package)
    if re.fullmatch(r"[0-9a-f]{40}", revision) is None:
        raise RELEASE.ReleaseBuildError("invalid " + component + " source revision")
    if re.fullmatch(r"sha256:[0-9a-f]{64}", image_id) is None:
        raise RELEASE.ReleaseBuildError("invalid " + component + " container image identity")
    expected = {*payload, "SHA256SUMS", "SOURCE_REVISION"}
    files = {relative.as_posix(): path for path, relative in RELEASE.iter_artifact_files(package)}
    if set(files) != expected or {path.name for path in package.iterdir()} != expected:
        raise RELEASE.ReleaseBuildError(component + " payload has missing or unexpected files")
    if files["SOURCE_REVISION"].stat().st_size != 41 or files["SOURCE_REVISION"].read_text(encoding="ascii") != revision + "\n":
        raise RELEASE.ReleaseBuildError(component + " source revision differs from the image")
    if files["SHA256SUMS"].stat().st_size > 1024:
        raise RELEASE.ReleaseBuildError(component + " checksum list exceeds its bound")
    checksums = {}
    for line in files["SHA256SUMS"].read_text(encoding="ascii").splitlines():
        match = re.fullmatch(r"([0-9a-f]{64})  ([A-Za-z0-9_.-]+)", line)
        if match is None or match[2] in checksums:
            raise RELEASE.ReleaseBuildError("invalid " + component + " checksum list")
        checksums[match[2]] = match[1]
    if set(checksums) != set(payload):
        raise RELEASE.ReleaseBuildError(component + " checksum list must cover every payload file")
    for name in payload:
        path = files[name]
        if not 0 < path.stat().st_size <= 256 * 1024 * 1024:
            raise RELEASE.ReleaseBuildError(component + " file is empty or exceeds its bound")
        if RELEASE.sha256_file(path) != checksums[name]:
            raise RELEASE.ReleaseBuildError(component + " content checksum differs")
    for name in native:
        with files[name].open("rb") as stream:
            header = stream.read(64)
        if (len(header) != 64 or header[:7] != b"\x7fELF\x02\x01\x01"
                or header[16:18] not in (b"\x02\x00", b"\x03\x00") or header[18:20] != b"\x3e\x00"):
            raise RELEASE.ReleaseBuildError(component + " requires native Linux x86-64 ELF artifacts")
    if os.name != "nt":
        if any(not path.stat().st_mode & 0o004 for path in files.values()):
            raise RELEASE.ReleaseBuildError(component + " payload is not readable by the non-root runtime")
        if not files[payload[0]].stat().st_mode & 0o001:
            raise RELEASE.ReleaseBuildError(component + " worker is not executable by the non-root runtime")
    lock = source / lock_relative
    RELEASE.assert_no_link_components(lock, source)
    return {
        "source_revision": revision, "runtime_identifier": "linux-x64", "image_id": image_id,
        "executable": directory + "/" + payload[0],
        "release_lock_sha256": RELEASE.sha256_file(lock),
        "files": [{"path": name, "length": path.stat().st_size, "sha256": RELEASE.sha256_file(path)}
                  for name, path in sorted(files.items())],
        "enabled_by_default": False, "platform_validation": "not_executed_by_builder",
    }


def image_preview_manifest(package: Path, revision: str, source: Path, image_id: str) -> dict:
    return _native_image_manifest(package, revision, source, image_id, payload=IMAGE_PREVIEW_FILES,
                                  native=IMAGE_PREVIEW_FILES[:2], directory=IMAGE_PREVIEW_PATH,
                                  lock_relative="services/worker-supervisor/ImagePreview/locks/AssetLibrary.ImagePreview.Worker.linux-x64.lock.json",
                                  component="image preview")


def image_supervisor_manifest(package: Path, revision: str, source: Path, image_id: str) -> dict:
    manifest = _native_image_manifest(package, revision, source, image_id, payload=IMAGE_SUPERVISOR_FILES,
                                      native=IMAGE_SUPERVISOR_FILES[:1], directory=IMAGE_SUPERVISOR_PATH,
                                      lock_relative="services/worker-supervisor/ImageSupervisor/locks/AssetLibrary.ImageSupervisor.linux-x64.lock.json",
                                      component="image supervisor")
    manifest.update({"socket": IMAGE_SOCKET_PATH, "decoder_uid": 1655, "core_uid": 1654,
                     "state_file": "/run/assetlibrary-image/supervisor-state.bin",
                     "runtime_validation": "required_on_target_not_performed_by_builder",
                     "startup_capabilities": ["CHOWN", "SETUID", "SETGID", "KILL"],
                     "steady_capabilities": ["CHOWN", "SETUID", "SETGID", "KILL"]})
    return manifest


def _inspect_native_image(docker: str, image_id: str, destination: Path, source: Path, revision: str,
                          directory: str, validate) -> dict:
    name = "assetlibrary-preview-inspect-" + uuid.uuid4().hex
    command([docker, "create", "--name", name, "--network", "none", "--read-only",
             "--entrypoint", "/bin/false", image_id], cwd=source, capture=True)
    try:
        destination.mkdir()
        command([docker, "cp", name + ":" + directory + "/.", str(destination)], cwd=source)
        return validate(destination, revision, source, image_id)
    finally:
        # This exact, newly created inspection container was never started.
        command([docker, "rm", name], cwd=source)


def inspect_image_preview(docker: str, image_id: str, destination: Path, source: Path, revision: str) -> dict:
    return _inspect_native_image(docker, image_id, destination, source, revision, IMAGE_PREVIEW_PATH, image_preview_manifest)


def inspect_image_supervisor(docker: str, image_id: str, destination: Path, source: Path, revision: str) -> dict:
    return _inspect_native_image(docker, image_id, destination, source, revision, IMAGE_SUPERVISOR_PATH, image_supervisor_manifest)


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
    tags = {name: f"assetlibrary/nas-{name}:{revision}" for name in IMAGE_ENV_KEYS}
    image_ids = {}
    for stage in ("core", "setup", "image"):
        invocation = [docker, "build", "--platform", "linux/amd64", "--file", "infra/docker/nas/Dockerfile",
                      "--target", stage, "--tag", tags[stage], "--build-arg", "SOURCE_REVISION=" + revision]
        for argument, identity in resolved.items():
            invocation += ["--build-arg", argument + "=" + identity["digest"]]
        command([*invocation, "."], cwd=source)
        info = json.loads(command([docker, "image", "inspect", tags[stage]], cwd=source, capture=True))[0]
        if info["Config"]["Labels"].get("org.opencontainers.image.revision") != revision:
            raise RELEASE.ReleaseBuildError("built image source label mismatch")
        image_ids[stage] = info["Id"]
    preview = inspect_image_preview(docker, image_ids["core"], output / "image-preview-inspection", source, revision)
    supervisor = inspect_image_supervisor(docker, image_ids["image"], output / "image-supervisor-inspection", source, revision)
    container_decoder = inspect_image_preview(docker, image_ids["image"], output / "image-container-decoder-inspection", source, revision)
    if container_decoder["files"] != preview["files"]:
        raise RELEASE.ReleaseBuildError("Core and image container decoder payloads differ")
    command([docker, "tag", resolved["POSTGRES_IMAGE"]["digest"], tags["postgres"]], cwd=source)
    image_ids["postgres"] = resolved["POSTGRES_IMAGE"]["id"]
    bundle = output / "assetlibrary-nas"
    bundle.mkdir()
    for filename in ("compose.yaml", "nasctl.sh", "settings.example.json", "README.md"):
        shutil.copy2(source / "infra/docker/nas" / filename, bundle / filename)
    (bundle / "nasctl.sh").chmod(0o755)
    env = ["ASSETLIBRARY_SOURCE_REVISION=" + revision]
    for name in tags:
        env += [f"ASSETLIBRARY_{IMAGE_ENV_KEYS[name]}_IMAGE={tags[name]}", f"ASSETLIBRARY_{IMAGE_ENV_KEYS[name]}_IMAGE_ID={image_ids[name]}"]
    (bundle / "images.env").write_text("\n".join(env) + "\n", encoding="utf-8", newline="\n")
    save_images_archive(docker, list(tags.values()), bundle / "images.tar", source)
    manifest = {"format_version": 1, "product": "AssetLibrary/NAS/read-only/v1", "source_revision": revision,
                "source_tree": tree, "base_images": resolved, "images": tags, "image_ids": image_ids,
                "asset_writes": False, "native_deployment_validation": "not_executed_by_builder",
                "image_preview": preview, "image_container": {"supervisor": supervisor, "decoder": container_decoder}}
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
