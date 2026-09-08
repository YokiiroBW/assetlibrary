#!/usr/bin/env python3
"""Bounded, test-only verification of an explicitly selected NAS image. Python 3.8+."""
from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import struct
import sys
import time
import uuid
import zlib


WORKER = "/app/workers/image-preview/AssetLibrary.ImagePreview.Worker"
MARKER_TARGET = "/app/workers/preview-denied.marker"
CORPUS_ID = "eca30895b0ccfe4f9333334f6b67ddd258b476e5be0acab2795491e4f71c10df"
MAGIC = 0x31495041
HEADER = struct.Struct("<6I")
SUCCESS = {
    "landscape.jpg": ((512, 341), (1600, 1066)),
    "landscape.png": ((512, 341), (1600, 1066)),
    "landscape.webp": ((512, 341), (1600, 1066)),
    "rotate-six.jpg": ((341, 512), (1066, 1600)),
    "transparent.png": ((384, 512), (900, 1200)),
    "中文目录/重复内容.dat": ((512, 341), (1600, 1066)),
}
ERRORS = {"active.svg": 5, "not-an-image.png": 5, "oversized-header.png": 6, "truncated.jpg": 4}


class CheckFailed(RuntimeError):
    pass


def require(condition, code):
    if not condition:
        raise CheckFailed(code)


def regular(path):
    current = path.absolute()
    while True:
        info = current.lstat()
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, "st_file_attributes", 0) & 0x400, "linked_input")
        if current == current.parent:
            break
        current = current.parent
    require(path.is_file(), "nonregular_input")


def digest(path):
    regular(path)
    result = hashlib.sha256()
    count = 0
    with path.open("rb") as stream:
        while True:
            chunk = stream.read(65536)
            if not chunk:
                return result.hexdigest()
            count += len(chunk)
            require(count <= 32 * 1024 * 1024, "source_hash_limit")
            result.update(chunk)


def read_json(path, maximum=262144):
    regular(path)
    require(path.stat().st_size <= maximum, "json_input_limit")
    return json.loads(path.read_text(encoding="utf-8"))


def corpus_snapshot(root):
    manifest = read_json(root / "manifest.json")
    require(manifest.get("kind") == "synthetic_preview_integration_inputs", "wrong_corpus_kind")
    entries = manifest.get("files", [])
    require(isinstance(entries, list) and len(entries) == 10, "wrong_corpus_count")
    identity = sorted((entry["path"], entry["bytes"], entry["sha256"]) for entry in entries)
    encoded = json.dumps(identity, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    require(hashlib.sha256(encoded).hexdigest() == CORPUS_ID, "unexpected_corpus_identity")
    require({entry[0] for entry in identity} == set(SUCCESS) | set(ERRORS), "wrong_corpus_paths")
    snapshot = {}
    for name, size, expected in identity:
        path = root.joinpath(*name.split("/"))
        regular(path)
        info = path.stat()
        actual = digest(path)
        require(info.st_size == size and actual == expected, "corpus_content_changed")
        snapshot[name] = {"bytes": size, "sha256": actual, "mtime_ns": info.st_mtime_ns}
    return snapshot


def validate_png(png, dimensions, maximum):
    require(57 <= len(png) <= maximum and png[:8] == b"\x89PNG\r\n\x1a\n", "invalid_png_size_or_signature")
    offset, chunks, color = 8, 0, None
    seen = set()
    while offset < len(png):
        chunks += 1
        require(chunks <= 4096 and offset + 12 <= len(png), "invalid_png_chunk_count")
        length = struct.unpack_from(">I", png, offset)[0]
        require(length <= len(png) - offset - 12, "invalid_png_chunk_length")
        kind = png[offset + 4:offset + 8]
        content = png[offset + 8:offset + 8 + length]
        crc = struct.unpack_from(">I", png, offset + 8 + length)[0]
        require(zlib.crc32(kind + content) & 0xffffffff == crc, "invalid_png_crc")
        if kind == b"IHDR":
            require(offset == 8 and length == 13, "invalid_png_header")
            require(struct.unpack(">II", content[:8]) == dimensions, "wrong_png_dimensions")
            color = content[9]
            require(content[8] == 8 and color in (2, 6) and content[10:] == bytes(3), "wrong_png_profile")
        elif kind in (b"sRGB", b"sBIT"):
            require(color is not None and b"IDAT" not in seen and kind not in seen, "invalid_png_metadata_order")
            require((length == 1 and content[0] <= 3) if kind == b"sRGB"
                    else content == bytes([8]) * (4 if color == 6 else 3), "invalid_png_metadata")
        elif kind == b"IDAT":
            require(color is not None and length > 0, "invalid_png_data")
        elif kind == b"IEND":
            require(b"IDAT" in seen and length == 0 and offset + 12 == len(png), "invalid_png_end")
            return
        else:
            raise CheckFailed("unexpected_png_chunk")
        seen.add(kind)
        offset += length + 12
    raise CheckFailed("missing_png_end")


async def bounded_read(stream, limit):
    result = bytearray()
    while True:
        chunk = await stream.read(min(65536, limit + 1 - len(result)))
        if not chunk:
            return bytes(result)
        result.extend(chunk)
        require(len(result) <= limit, "pipe_output_limit")


async def stop_cli(process):
    if process.returncode is None:
        try:
            process.kill()
        except ProcessLookupError:
            pass
        await asyncio.wait_for(process.wait(), 5)


async def command(docker, arguments, timeout=20):
    process = await asyncio.create_subprocess_exec(docker, *arguments, stdin=asyncio.subprocess.DEVNULL,
                                                 stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE)
    tasks = [asyncio.ensure_future(bounded_read(process.stdout, 262144)),
             asyncio.ensure_future(bounded_read(process.stderr, 16384)), asyncio.ensure_future(process.wait())]
    try:
        output, errors, code = await asyncio.wait_for(asyncio.gather(*tasks), timeout)
        require(code == 0, "docker_" + "_".join(arguments[:2]) + "_failed")
        return output
    finally:
        await stop_cli(process)
        for task in tasks:
            if not task.done():
                task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)


class Runner:
    def __init__(self, args, manifest, evidence, snapshot):
        self.args, self.evidence, self.snapshot = args, evidence, snapshot
        self.image = manifest["image_ids"]["core"]
        self.revision = manifest["source_revision"]
        self.token = "nas-worker-" + uuid.uuid4().hex
        self.report = {"kind": "nas_worker_verification", "status": "running", "source_revision": self.revision,
                       "image_id": self.image, "corpus_identity": CORPUS_ID, "cases": [], "owned_container_ids": [],
                       "harness_sha256": digest(Path(__file__)), "build_manifest_sha256": digest(args.build_manifest),
                       "source_unchanged": False, "cleanup_verified": False, "production_deployment": False}
        self.current_id = None
        self.creation_unconfirmed = False

    def save(self):
        temporary = self.evidence / "result.pending.json"
        temporary.write_text(json.dumps(self.report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        os.replace(str(temporary), str(self.evidence / "result.json"))

    async def inspect(self, identifier):
        raw = await command(self.args.docker, ["container", "inspect", identifier])
        data = json.loads(raw)
        require(isinstance(data, list) and len(data) == 1, "invalid_container_inspect")
        info = data[0]
        require(info["Id"] == identifier and info["Image"] == self.image
                and info["Config"]["Labels"].get("io.assetlibrary.worker-test") == self.token, "foreign_container_identity")
        return info

    async def clean(self):
        if self.current_id is None:
            require(not self.creation_unconfirmed, "container_creation_cleanup_unconfirmed")
            return
        identifier = self.current_id
        await self.inspect(identifier)
        await command(self.args.docker, ["container", "rm", "--force", identifier])
        remaining = await command(self.args.docker, ["container", "ls", "--all", "--quiet", "--no-trunc", "--filter", "id=" + identifier])
        require(not remaining.strip(), "container_cleanup_unconfirmed")
        self.current_id = None

    def verify_container_policy(self, info, probe):
        config, host = info["Config"], info["HostConfig"]
        require(config["User"] == "1654:1654" and config["Entrypoint"] == [WORKER], "wrong_container_identity_or_entrypoint")
        require(host["ReadonlyRootfs"] is True and host["NetworkMode"] == "none" and host["Init"] is True, "wrong_container_isolation")
        require(host["Memory"] == 1073741824 and host["CpuShares"] == 1024
                and host.get("PidsLimit") in (None, 0) and host.get("CpuQuota", 0) == 0
                and host.get("NanoCpus", 0) == 0, "wrong_container_resource_limits")
        require({item.upper() for item in host["CapDrop"]} == {"ALL"}
                and set(host["SecurityOpt"]) in ({"no-new-privileges"}, {"no-new-privileges:true"}), "wrong_container_security_options")
        require(set(host["Tmpfs"]) == {"/tmp"}, "wrong_container_tmpfs")
        options = set(host["Tmpfs"]["/tmp"].split(","))
        require(len(options) == 6 and {"rw", "noexec", "nosuid", "nodev", "mode=1777"} <= options
                and bool(options & {"size=64m", "size=67108864"}), "wrong_container_tmpfs_options")
        mounts = [mount for mount in info["Mounts"] if mount["Type"] != "tmpfs"]
        require(all(mount["Destination"] == "/tmp" and mount["RW"] is True
                    for mount in info["Mounts"] if mount["Type"] == "tmpfs"), "unexpected_tmpfs_mount")
        if probe == "isolation":
            require(len(mounts) == 1 and mounts[0]["Type"] == "bind" and mounts[0]["Source"] == str(self.args.marker)
                    and mounts[0]["Destination"] == MARKER_TARGET and mounts[0]["RW"] is False, "wrong_probe_canary_mount")
        else:
            require(not mounts, "unexpected_container_mount")
        self.report["container_policy"] = {"user": config["User"], "memory_bytes": host["Memory"],
            "cpu_shares": host["CpuShares"], "cpu_quota": host.get("CpuQuota", 0), "pids_limit": host.get("PidsLimit"),
            "network": "none", "readonly": True, "init": True, "cap_drop": ["ALL"],
            "no_new_privileges": True, "tmpfs": host["Tmpfs"], "seccomp_override": False}

    async def acquire(self, case, probe):
        cidfile = self.evidence / (case + ".cid")
        args = ["container", "create", "--cidfile", str(cidfile), "--name", self.token + "-" + case,
                "--label", "io.assetlibrary.worker-test=" + self.token, "--label", "io.assetlibrary.worker-case=" + case,
                "--interactive", "--init", "--user", "1654:1654", "--cap-drop", "ALL",
                "--security-opt", "no-new-privileges:true", "--read-only", "--network", "none",
                "--memory", "1g", "--cpu-shares", "1024", "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=64m,mode=1777",
                "--workdir", "/app/workers/image-preview", "--entrypoint", WORKER]
        if probe == "isolation":
            args.extend(["--mount", "type=bind,src=" + str(self.args.marker) + ",dst=" + MARKER_TARGET + ",readonly"])
        args.append(self.image)
        if probe:
            args.append("--probe-" + probe)
        self.creation_unconfirmed = True
        try:
            await command(self.args.docker, args)
        finally:
            # Discover by this run's unique labels if a timed-out create wrote no cidfile.
            if cidfile.exists():
                regular(cidfile)
                require(cidfile.stat().st_size <= 65, "cidfile_limit")
                identifiers = [cidfile.read_text(encoding="ascii").strip()]
            else:
                raw = await command(self.args.docker, ["container", "ls", "--all", "--quiet", "--no-trunc",
                                    "--filter", "label=io.assetlibrary.worker-test=" + self.token,
                                    "--filter", "label=io.assetlibrary.worker-case=" + case])
                identifiers = raw.decode("ascii").split()
            require(len(identifiers) <= 1, "ambiguous_owned_container")
            if identifiers:
                identifier = identifiers[0]
                require(re.fullmatch(r"[0-9a-f]{64}", identifier) is not None, "invalid_container_id")
                self.current_id = identifier
                self.report["owned_container_ids"].append(identifier)
                self.save()
                await self.inspect(identifier)
            self.creation_unconfirmed = False
        require(self.current_id is not None, "container_create_unconfirmed")
        self.verify_container_policy(await self.inspect(self.current_id), probe)

    async def send_image(self, process, name, profile):
        ready = await asyncio.wait_for(process.stdout.readexactly(24), 10)
        require(HEADER.unpack(ready) == (MAGIC, 1, 0, 0, 0, 0), "worker_not_ready")
        path = self.args.corpus.joinpath(*name.split("/"))
        length = self.snapshot[name]["bytes"]
        process.stdin.write(HEADER.pack(MAGIC, 2, profile, length, 0, 0))
        total, hashed = 0, hashlib.sha256()
        with path.open("rb") as source:
            while True:
                chunk = source.read(65536)
                if not chunk:
                    break
                total += len(chunk)
                require(total <= length, "source_changed_during_send")
                hashed.update(chunk)
                process.stdin.write(chunk)
                await process.stdin.drain()
        require(total == length and hashed.hexdigest() == self.snapshot[name]["sha256"], "source_changed_during_send")
        process.stdin.close()
        return await bounded_read(process.stdout, 24 + (2 if profile == 0 else 12) * 1024 * 1024)

    async def execute(self, case, name=None, profile=0, probe=None):
        record = {"case": case, "passed": False, "parent_timed_out": False}
        self.report["cases"].append(record)
        started = time.monotonic()
        process = None
        tasks = []
        try:
            await self.acquire(case, probe)
            process = await asyncio.create_subprocess_exec(self.args.docker, "container", "start", "--attach", "--interactive", self.current_id,
                                                          stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE)
            if probe:
                process.stdin.close()
                stdout = bounded_read(process.stdout, 16384)
            else:
                stdout = self.send_image(process, name, profile)
            tasks = [asyncio.ensure_future(stdout), asyncio.ensure_future(bounded_read(process.stderr, 16384)),
                     asyncio.ensure_future(process.wait())]
            try:
                output, errors, cli_exit = await asyncio.wait_for(asyncio.gather(*tasks), 25)
            except asyncio.TimeoutError:
                record["parent_timed_out"] = True
                raise CheckFailed("worker_parent_deadline")
            info = await self.inspect(self.current_id)
            state = info["State"]
            record.update({"container_id": self.current_id, "exit_code": state["ExitCode"], "oom_killed": state["OOMKilled"],
                           "started_at": state["StartedAt"], "finished_at": state["FinishedAt"],
                           "docker_cli_exit": cli_exit, "stdout_bytes": len(output), "stderr_bytes": len(errors),
                           "stderr_sha256": hashlib.sha256(errors).hexdigest()})
            require(not state["Running"] and not state["OOMKilled"], "worker_running_or_oom_killed")
            if probe == "cpu":
                require(state["ExitCode"] == 137 and cli_exit == 137 and not output, "cpu_limit_not_confirmed")
            elif probe:
                require(state["ExitCode"] == 0 and cli_exit == 0, "probe_failed")
                data = json.loads(output)
                require(isinstance(data, dict) and set(data) == {"probe", "passed", "initial_seccomp", "seccomp", "no_new_privileges",
                        "file_denied", "socket_denied", "connect_denied", "other_process_signal_denied", "cross_process_memory_denied",
                        "fork_denied", "clone_denied", "exec_denied", "initial_io_uring_error", "io_uring_denied", "created_threads"}, "invalid_probe_schema")
                require(all(type(value) in (bool, int) for key, value in data.items() if key != "probe"), "invalid_probe_value")
                require(all(type(data[key]) is int and 0 <= data[key] <= 65535
                            for key in ("initial_seccomp", "seccomp", "no_new_privileges", "initial_io_uring_error", "created_threads")), "invalid_probe_counter")
                require(data.get("probe") == probe and data.get("passed") is True, "probe_report_failed")
                if probe == "threads":
                    require(0 < data["created_threads"] < 256, "thread_creation_or_reap_unproven")
                if probe == "isolation":
                    for flag in ("file_denied", "socket_denied", "connect_denied", "other_process_signal_denied", "cross_process_memory_denied",
                                 "fork_denied", "clone_denied", "exec_denied", "io_uring_denied"):
                        require(data.get(flag) is True, "isolation_boundary_not_denied")
                    require(data.get("seccomp") == 2 and data.get("no_new_privileges") == 1, "isolation_not_enforced")
                    require(type(data.get("initial_seccomp")) is int and type(data.get("initial_io_uring_error")) is int, "outer_policy_evidence_missing")
                record["probe"] = data
            else:
                require(len(output) >= 24, "missing_result_header")
                magic, status, returned_profile, size, width, height = HEADER.unpack(output[:24])
                require(magic == MAGIC and size == len(output) - 24, "invalid_result_length")
                if name in ERRORS:
                    require((status, returned_profile, size, width, height) == (ERRORS[name], 0, 0, 0, 0)
                            and state["ExitCode"] == 1 and cli_exit == 1, "wrong_rejection_status")
                else:
                    require(status == 3 and returned_profile == profile and state["ExitCode"] == 0 and cli_exit == 0, "image_decode_failed")
                    dimensions = SUCCESS[name][profile]
                    require((width, height) == dimensions, "wrong_result_dimensions")
                    validate_png(output[24:], dimensions, (2 if profile == 0 else 12) * 1024 * 1024)
                    derivative = self.evidence / (case + ".png")
                    with derivative.open("xb") as stream:
                        stream.write(output[24:])
                    record.update({"derivative": derivative.name, "png_sha256": hashlib.sha256(output[24:]).hexdigest(),
                                   "width": width, "height": height, "png_bytes": size})
                record["worker_status"] = status
            record["passed"] = True
        except Exception as error:
            record["error"] = str(error) if isinstance(error, CheckFailed) else type(error).__name__
            raise
        finally:
            try:
                if process is not None:
                    await stop_cli(process)
                for task in tasks:
                    if not task.done():
                        task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
            finally:
                try:
                    await self.clean()
                finally:
                    record["elapsed_seconds"] = round(time.monotonic() - started, 3)
                    record["cleanup_verified"] = self.current_id is None and not self.creation_unconfirmed
                    self.save()

    async def run_cases(self):
        kernel = json.loads(await command(self.args.docker, ["info", "--format", "{{json .KernelVersion}}"] ))
        require(isinstance(kernel, str) and len(kernel) <= 128 and "\n" not in kernel, "invalid_daemon_kernel")
        self.report["docker_kernel"] = kernel
        raw = await command(self.args.docker, ["image", "inspect", self.image])
        info = json.loads(raw)[0]
        require(info["Id"] == self.image and info["Architecture"] == "amd64" and info["Os"] == "linux"
                and info["Config"]["Labels"].get("org.opencontainers.image.revision") == self.revision, "image_provenance_mismatch")
        for index, name in enumerate(SUCCESS):
            for profile in (0, 1):
                await self.execute("image-{}-{}".format(index, profile), name=name, profile=profile)
        for profile in (0, 1):
            cases = {record["case"]: record for record in self.report["cases"]}
            require(cases["image-0-{}".format(profile)]["png_sha256"] == cases["image-5-{}".format(profile)]["png_sha256"], "renamed_jpeg_derivative_changed")
        self.report["renamed_jpeg_equal"] = True
        for index, name in enumerate(ERRORS):
            await self.execute("rejection-{}".format(index), name=name)
        for probe in ("isolation", "memory", "threads", "cpu"):
            await self.execute("probe-" + probe, probe=probe)

    async def run(self):
        self.save()
        try:
            await asyncio.wait_for(self.run_cases(), 600)
            self.report["status"] = "passed"
        except BaseException as error:
            self.report["status"] = "failed"
            self.report["error"] = str(error) if isinstance(error, CheckFailed) else type(error).__name__
        finally:
            try:
                await self.clean()
                self.report["cleanup_verified"] = self.current_id is None and not self.creation_unconfirmed
                self.report["source_unchanged"] = corpus_snapshot(self.args.corpus) == self.snapshot
                require(self.report["source_unchanged"], "corpus_changed_after_run")
            except Exception as error:
                self.report["status"] = "failed"
                self.report["finalization_error"] = str(error) if isinstance(error, CheckFailed) else type(error).__name__
            self.save()
        return 0 if self.report["status"] == "passed" else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-manifest", type=Path, required=True)
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--marker", type=Path, required=True, help="Existing daemon-host-visible read-only synthetic canary")
    parser.add_argument("--evidence", type=Path, required=True, help="New directory under an existing owned parent")
    parser.add_argument("--docker", default="docker")
    args = parser.parse_args()
    try:
        manifest = read_json(args.build_manifest)
        preview = manifest.get("image_preview", {})
        revision, image = manifest.get("source_revision", ""), manifest.get("image_ids", {}).get("core", "")
        require(re.fullmatch(r"[0-9a-f]{40}", revision) is not None and re.fullmatch(r"sha256:[0-9a-f]{64}", image) is not None, "invalid_build_identity")
        require(preview.get("source_revision") == revision and preview.get("image_id") == image
                and preview.get("runtime_identifier") == "linux-x64" and preview.get("executable") == WORKER, "worker_manifest_identity_mismatch")
        args.corpus = args.corpus.absolute()
        snapshot = corpus_snapshot(args.corpus)
        args.marker = args.marker.absolute()
        regular(args.marker)
        expected_marker = b"assetlibrary-image-canary-v1"
        require(args.marker.stat().st_size == len(expected_marker) and args.marker.read_bytes() == expected_marker, "invalid_probe_marker")
        require(args.marker.stat().st_mode & 0o444 == 0o444 and not args.marker.stat().st_mode & 0o222, "marker_must_be_read_only")
        require(not any(value in str(args.marker) for value in (",", "\n", "\r")), "unsafe_marker_mount_path")
        evidence = args.evidence.absolute()
        require(not evidence.exists() and evidence.parent.is_dir(), "evidence_must_be_new_under_owned_parent")
        current = evidence.parent
        while True:
            require(not current.is_symlink(), "linked_evidence_parent")
            if current == current.parent:
                break
            current = current.parent
        require(os.path.commonpath([str(evidence), str(args.corpus)]) != str(args.corpus), "evidence_inside_corpus")
        evidence.mkdir(mode=0o700)
        runner = Runner(args, manifest, evidence, snapshot)
        code = asyncio.run(runner.run())
        print(json.dumps({key: runner.report[key] for key in ("status", "source_revision", "image_id", "source_unchanged", "cleanup_verified")}, sort_keys=True))
        return code
    except Exception as error:
        print(json.dumps({"status": "failed", "error": str(error) if isinstance(error, CheckFailed) else type(error).__name__}, sort_keys=True))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
