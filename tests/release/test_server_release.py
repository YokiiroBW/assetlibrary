from __future__ import annotations

import hashlib
import importlib.util
import io
import json
import os
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.error
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
TASK_ROOT = ROOT / ".runtime" / "sandbox-storage" / "V01-008"
TASK_MARKER = ".assetlibrary-v01-008-task-root"
SMOKE_MARKER = ".assetlibrary-v01-008-process-smoke"
MARKER_VALUE = "AssetLibrary/V01-008/release-output/v1\n"


def load_script(name: str):
    path = ROOT / "scripts" / f"{name}.py"
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(module)
    return module


BUILDER = load_script("build_server_release")
VALIDATOR = load_script("validate_server_release")


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", 0))
        return int(listener.getsockname()[1])


def http_json(port: int, endpoint: str) -> dict[str, object]:
    with urllib.request.urlopen(f"http://127.0.0.1:{port}/{endpoint}", timeout=0.4) as response:
        return json.loads(response.read(4096))


class ReleaseDefinitionTests(unittest.TestCase):
    def test_repository_release_definitions_pass(self) -> None:
        errors, report = VALIDATOR.validate_repository(ROOT)

        self.assertEqual(errors, [])
        self.assertEqual(report["status"], "passed")
        self.assertEqual(
            report["platform_evidence"],
            {
                "windows_service": "blocked_missing_environment",
                "docker": "blocked_missing_environment",
                "linux_systemd": "blocked_missing_environment",
            },
        )

    def test_release_output_cleanup_requires_exact_owned_descendant(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            task_root = BUILDER.initialize_task_root(
                repository,
                ".runtime/sandbox-storage/V01-008",
            )
            output = task_root / "release-build"
            output.mkdir()
            (output / "unowned.txt").write_text("preserve", encoding="utf-8")

            with self.assertRaises(BUILDER.ReleaseBuildError):
                BUILDER.reset_owned_output(task_root, output)
            self.assertTrue((output / "unowned.txt").is_file())
            with self.assertRaises(BUILDER.ReleaseBuildError):
                BUILDER.reset_owned_output(task_root, task_root)
            with self.assertRaises(BUILDER.ReleaseBuildError):
                BUILDER.reset_owned_output(task_root, repository / "outside")

            (output / BUILDER.OUTPUT_MARKER).write_text(
                BUILDER.MARKER_VALUE,
                encoding="utf-8",
                newline="\n",
            )
            BUILDER.reset_owned_output(task_root, output)
            self.assertEqual(
                (output / BUILDER.OUTPUT_MARKER).read_text(encoding="utf-8"),
                BUILDER.MARKER_VALUE,
            )

    def test_release_provenance_tracks_targets_rejects_ancestor_imports_and_preserves_metadata_head(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)
            repository = parent / "repository"
            (repository / "eng").mkdir(parents=True)
            issuance_inputs = list(BUILDER.REQUIRED_ISSUANCE_INPUTS)
            (repository / "Directory.Build.props").write_text("<Project />\n", encoding="utf-8")
            (repository / "eng/server-release-policy.json").write_text(
                json.dumps({"issuance_inputs": issuance_inputs}),
                encoding="utf-8",
            )

            def git(*arguments: str) -> None:
                subprocess.run(
                    ["git", "-C", str(repository), *arguments],
                    check=True,
                    capture_output=True,
                    text=True,
                    timeout=10,
                )

            git("init", "-q", "-b", "main")
            git("add", ".")
            git("-c", "user.name=AssetLibrary Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "initial")
            initial_revision, initial_head = BUILDER.resolve_provenance(repository)
            initial_digest = BUILDER.issuance_input_tree_sha256(repository, initial_revision)
            self.assertEqual(initial_revision, initial_head)
            self.assertRegex(initial_digest, r"^[0-9a-f]{64}$")

            (repository / "Directory.Build.targets").write_text("<Project />\n", encoding="utf-8")
            with self.assertRaisesRegex(BUILDER.ReleaseBuildError, "issuance inputs must be committed"):
                BUILDER.resolve_provenance(repository)

            git("add", "Directory.Build.targets")
            git("-c", "user.name=AssetLibrary Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "targets")
            target_revision, target_head = BUILDER.resolve_provenance(repository)
            target_digest = BUILDER.issuance_input_tree_sha256(repository, target_revision)
            self.assertEqual(target_revision, target_head)
            self.assertNotEqual(target_revision, initial_revision)
            self.assertNotEqual(target_digest, initial_digest)

            (repository / "eng/server-release-policy.json").write_text(
                json.dumps(
                    {
                        "issuance_inputs": [
                            "Directory.Build.props",
                            "Directory.Build.targets",
                        ]
                    }
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(BUILDER.ReleaseBuildError, "protected release input set"):
                BUILDER.resolve_provenance(repository)
            (repository / "eng/server-release-policy.json").write_text(
                json.dumps({"issuance_inputs": issuance_inputs}),
                encoding="utf-8",
            )

            (repository / "metadata.txt").write_text("outside issuance\n", encoding="utf-8")
            git("add", "metadata.txt")
            git("-c", "user.name=AssetLibrary Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "metadata")
            metadata_revision, metadata_head = BUILDER.resolve_provenance(repository)
            self.assertEqual(metadata_revision, target_revision)
            self.assertNotEqual(metadata_head, target_revision)

            (parent / "Directory.Build.targets").write_text("<Project />\n", encoding="utf-8")
            with self.assertRaisesRegex(BUILDER.ReleaseBuildError, "outside the repository"):
                BUILDER.resolve_provenance(repository)

    def test_release_build_pins_auto_imports_and_rejects_environment_overrides(self) -> None:
        repository = Path("repository").absolute()
        properties = BUILDER.build_properties(repository, "a" * 40)
        self.assertIn(
            f"-p:DirectoryBuildPropsPath={repository / 'Directory.Build.props'}",
            properties,
        )
        self.assertIn(
            f"-p:DirectoryBuildTargetsPath={repository / 'Directory.Build.targets'}",
            properties,
        )
        with mock.patch.dict(os.environ, {"DirectoryBuildTargetsPath": "outside.targets"}):
            with self.assertRaisesRegex(BUILDER.ReleaseBuildError, "external MSBuild import overrides"):
                BUILDER.dotnet_environment(Path("task"), Path("dotnet"))

    def test_validator_rejects_windows_path_aliases_before_host_path_conversion(self) -> None:
        unsafe = (
            r"win-x64/..\..\Windows\win.ini",
            r"win-x64/\\server\share\payload",
            r"win-x64/C:\Windows\win.ini",
            r"win-x64/file.dll:stream",
            "win-x64/NUL.txt",
            "win-x64/NUL .txt",
            "win-x64/COM¹.txt",
            "win-x64/CONIN$",
            "win-x64/trailing.",
            "win-x64/trailing ",
            "win-x64/control\x01name",
        )
        for value in unsafe:
            with self.subTest(value=value), self.assertRaises(ValueError):
                VALIDATOR.safe_manifest_relative(value)
        self.assertEqual(
            VALIDATOR.safe_manifest_relative("win-x64/AssetLibrary.CoreServer.Host.exe").as_posix(),
            "win-x64/AssetLibrary.CoreServer.Host.exe",
        )

    def test_archive_manifest_requires_the_only_allowed_name_before_path_use(self) -> None:
        revision = "b" * 40
        expected = f"archives/assetlibrary-core-server-win-x64-{revision[:12]}.zip"
        runtime, relative = VALIDATOR.expected_archive_relative(
            {"runtime_identifier": "win-x64", "path": expected},
            revision,
        )
        self.assertEqual(runtime, "win-x64")
        self.assertEqual(relative.as_posix(), expected)
        for path in (r"..\outside.zip", r"\\server\share\payload.zip", "archives/other.zip"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                VALIDATOR.expected_archive_relative(
                    {"runtime_identifier": "win-x64", "path": path},
                    revision,
                )

    def test_validator_opens_manifest_files_once_without_following_links(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "root"
            nested = root / "nested"
            nested.mkdir(parents=True)
            payload = nested / "payload.bin"
            payload.write_bytes(b"trusted payload")

            with VALIDATOR.open_contained_regular_file(
                root,
                PurePosixPath("nested/payload.bin"),
            ) as (stream, length):
                self.assertEqual(length, len(b"trusted payload"))
                self.assertEqual(stream.read(), b"trusted payload")

            with self.assertRaises((OSError, ValueError)):
                with VALIDATOR.open_contained_regular_file(
                    root,
                    PurePosixPath("nested/missing.bin"),
                ):
                    pass

            outside = Path(directory) / "outside.bin"
            outside.write_bytes(b"outside")
            alias = nested / "alias.bin"
            try:
                alias.symlink_to(outside)
            except OSError:
                pass
            else:
                with self.assertRaises((OSError, ValueError)):
                    with VALIDATOR.open_contained_regular_file(
                        root,
                        PurePosixPath("nested/alias.bin"),
                    ):
                        pass

    def test_archive_validation_rejects_declared_member_length_before_streaming(self) -> None:
        member_name = "assetlibrary-core-server-win-x64/payload.bin"
        archive_stream = io.BytesIO()
        with zipfile.ZipFile(archive_stream, mode="w") as archive:
            archive.writestr(member_name, b"abc")
        errors: list[str] = []

        VALIDATOR.verify_archive(
            archive_stream,
            "assetlibrary-core-server-win-x64-revision.zip",
            "win-x64",
            {member_name: (hashlib.sha256(b"abc").hexdigest(), 2)},
            errors,
        )

        self.assertEqual(errors, [f"archive content length mismatch: {member_name}"])

    def test_runtime_tree_aggregate_covers_every_file_in_ordinal_path_order(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            publish = Path(directory)
            runtime = publish / "win-x64"
            runtime.mkdir()
            (runtime / "z.dll").write_bytes(b"z")
            (runtime / "a.exe").write_bytes(b"alpha")
            manifest = BUILDER.create_file_manifest(publish, ["win-x64"])
            records = []
            for name in ("a.exe", "z.dll"):
                path = runtime / name
                records.append(
                    f"{hashlib.sha256(path.read_bytes()).hexdigest()}\t{path.stat().st_size}\t{name}\n"
                )
            expected = hashlib.sha256("".join(records).encode("utf-8")).hexdigest()
            self.assertEqual(manifest["runtime_aggregate_sha256"], {"win-x64": expected})

    def test_runtime_evidence_binding_covers_revision_rid_and_tree_digest(self) -> None:
        revision = "a" * 40
        tree_digest = "b" * 64
        expected = BUILDER.runtime_evidence_binding_sha256(
            revision,
            "win-x64",
            tree_digest,
        )
        self.assertEqual(
            expected,
            VALIDATOR.runtime_evidence_binding_sha256(revision, "win-x64", tree_digest),
        )
        self.assertNotEqual(
            expected,
            BUILDER.runtime_evidence_binding_sha256("c" * 40, "win-x64", tree_digest),
        )
        self.assertNotEqual(
            expected,
            BUILDER.runtime_evidence_binding_sha256(revision, "linux-x64", tree_digest),
        )

    def test_source_snapshot_materializes_the_selected_commit_not_live_tree(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)
            repository = parent / "repository"
            (repository / "eng").mkdir(parents=True)
            (repository / "Directory.Build.props").write_text("<Project />\n", encoding="utf-8")
            (repository / "eng/server-release-policy.json").write_text(
                json.dumps({"issuance_inputs": list(BUILDER.REQUIRED_ISSUANCE_INPUTS)}),
                encoding="utf-8",
            )
            subprocess.run(
                ["git", "-C", str(repository), "init", "-q", "-b", "main"],
                check=True,
                timeout=10,
            )
            subprocess.run(
                ["git", "-C", str(repository), "add", "."],
                check=True,
                timeout=10,
            )
            subprocess.run(
                [
                    "git",
                    "-C",
                    str(repository),
                    "-c",
                    "user.name=AssetLibrary Test",
                    "-c",
                    "user.email=test@example.invalid",
                    "commit",
                    "-qm",
                    "snapshot",
                ],
                check=True,
                timeout=10,
            )
            revision = subprocess.run(
                ["git", "-C", str(repository), "rev-parse", "HEAD"],
                check=True,
                capture_output=True,
                text=True,
                timeout=10,
            ).stdout.strip()
            (repository / "Directory.Build.props").write_text(
                "<Project><Target Name='LiveMutation' /></Project>\n",
                encoding="utf-8",
            )
            snapshot_root = parent / "snapshot"
            source_root = BUILDER.create_source_snapshot(repository, revision, snapshot_root)
            self.assertEqual(
                (source_root / "Directory.Build.props").read_text(encoding="utf-8"),
                "<Project />\n",
            )
            BUILDER.remove_source_snapshot(snapshot_root)
            self.assertFalse(snapshot_root.exists())

            failed_snapshot = parent / "failed-snapshot"
            with self.assertRaises(BUILDER.ReleaseBuildError):
                BUILDER.create_source_snapshot(repository, "f" * 40, failed_snapshot)
            self.assertFalse(failed_snapshot.exists())

    def test_archive_member_names_reject_traversal_and_absolute_paths(self) -> None:
        for value in ("../escape", "a/../../escape", "/absolute", "./relative"):
            with self.subTest(value=value), self.assertRaises(BUILDER.ReleaseBuildError):
                BUILDER.validate_relative_archive_name(value)

    def test_archives_are_byte_reproducible_and_normalized(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source"
            source.mkdir()
            (source / "AssetLibrary.CoreServer.Host").write_bytes(b"host\n")
            nested = source / "nested"
            nested.mkdir()
            (nested / "contract.json").write_text("{}\n", encoding="utf-8", newline="\n")
            outputs: list[str] = []
            for index in range(2):
                zip_path = root / f"release-{index}.zip"
                tar_path = root / f"release-{index}.tar.gz"
                BUILDER.deterministic_zip(source, zip_path, "assetlibrary-core-server-win-x64")
                BUILDER.deterministic_tar_gz(source, tar_path, "assetlibrary-core-server-linux-x64")
                outputs.extend((hashlib.sha256(zip_path.read_bytes()).hexdigest(), hashlib.sha256(tar_path.read_bytes()).hexdigest()))

            self.assertEqual(outputs[0], outputs[2])
            self.assertEqual(outputs[1], outputs[3])

    def test_release_lock_requires_exact_rid_and_unchanged_neutral_graph(self) -> None:
        neutral = {
            "version": 2,
            "dependencies": {
                "net10.0": {
                    "Example.Package": {
                        "type": "Direct",
                        "requested": "[1.2.3, )",
                        "resolved": "1.2.3",
                        "contentHash": "A" * 88,
                    }
                }
            },
        }
        generated = {
            "version": 2,
            "dependencies": {
                "net10.0": neutral["dependencies"]["net10.0"],
                "net10.0/win-x64": {
                    "Example.Runtime": {
                        "type": "Transitive",
                        "resolved": "1.2.3",
                        "contentHash": "B" * 88,
                    }
                },
            },
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            base_path = root / "base.lock.json"
            generated_path = root / "generated.lock.json"
            base_path.write_text(json.dumps(neutral), encoding="utf-8")
            generated_path.write_text(json.dumps(generated), encoding="utf-8")

            BUILDER.validate_release_lock(generated_path, base_path, "win-x64")

            generated["dependencies"]["net10.0"]["Example.Package"]["resolved"] = "9.9.9"
            generated_path.write_text(json.dumps(generated), encoding="utf-8")
            with self.assertRaises(BUILDER.ReleaseBuildError):
                BUILDER.validate_release_lock(generated_path, base_path, "win-x64")

    def test_native_host_executable_name_preserves_the_host_suffix(self) -> None:
        root = Path("publish")

        self.assertEqual(
            BUILDER.host_executable_path(root, "win-x64"),
            root / "win-x64" / "AssetLibrary.CoreServer.Host.exe",
        )
        self.assertEqual(
            BUILDER.host_executable_path(root, "linux-x64"),
            root / "linux-x64" / "AssetLibrary.CoreServer.Host",
        )


class HostProcessSmokeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        dotnet_candidate = os.environ.get("ASSETLIBRARY_TEST_DOTNET") or shutil.which("dotnet")
        if not dotnet_candidate:
            raise RuntimeError("ASSETLIBRARY_TEST_DOTNET must identify the exact .NET 10.0.111 host")
        located = shutil.which(dotnet_candidate) if Path(dotnet_candidate).name == dotnet_candidate else dotnet_candidate
        if not located:
            raise RuntimeError("ASSETLIBRARY_TEST_DOTNET was not found on PATH")
        cls.dotnet = str(Path(located).resolve(strict=True))
        version = subprocess.check_output([cls.dotnet, "--version"], text=True, timeout=10).strip()
        if version != "10.0.111":
            raise RuntimeError(f"process smoke requires .NET SDK 10.0.111, found {version}")
        cls.host_dll = (
            ROOT
            / "services/core-server/Host/bin/Release/net10.0/AssetLibrary.CoreServer.Host.dll"
        )
        if not cls.host_dll.is_file():
            raise RuntimeError("build the Release host before running process smoke")

        TASK_ROOT.mkdir(parents=True, exist_ok=True)
        (TASK_ROOT / TASK_MARKER).write_text(MARKER_VALUE, encoding="utf-8", newline="\n")
        cls.smoke_root = TASK_ROOT / "process-smoke"
        if cls.smoke_root.exists():
            marker = cls.smoke_root / SMOKE_MARKER
            if not marker.is_file() or marker.read_text(encoding="utf-8") != MARKER_VALUE:
                raise RuntimeError("refusing to clean an unowned process-smoke directory")
            shutil.rmtree(cls.smoke_root)
        cls.smoke_root.mkdir()
        (cls.smoke_root / SMOKE_MARKER).write_text(MARKER_VALUE, encoding="utf-8", newline="\n")

    @classmethod
    def tearDownClass(cls) -> None:
        if cls.smoke_root.resolve().parent != TASK_ROOT.resolve():
            raise RuntimeError("process-smoke cleanup boundary escaped")
        marker = cls.smoke_root / SMOKE_MARKER
        if not marker.is_file() or marker.read_text(encoding="utf-8") != MARKER_VALUE:
            raise RuntimeError("process-smoke owner marker disappeared")
        shutil.rmtree(cls.smoke_root)

    def setUp(self) -> None:
        self.case = Path(tempfile.mkdtemp(prefix="case-", dir=self.smoke_root))
        (self.case / SMOKE_MARKER).write_text(MARKER_VALUE, encoding="utf-8", newline="\n")
        self.processes: list[subprocess.Popen[str]] = []

    def tearDown(self) -> None:
        for process in self.processes:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=5)
            if process.stdout:
                process.stdout.close()
            if process.stderr:
                process.stderr.close()
        if self.case.resolve().parent != self.smoke_root.resolve():
            raise RuntimeError("case cleanup boundary escaped")
        if (self.case / SMOKE_MARKER).read_text(encoding="utf-8") != MARKER_VALUE:
            raise RuntimeError("case owner marker disappeared")
        shutil.rmtree(self.case)

    def environment(self) -> dict[str, str]:
        environment = {
            key: value
            for key, value in os.environ.items()
            if not key.startswith("ASSETLIBRARY_") and key != "ASPNETCORE_URLS"
        }
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        environment["DOTNET_NOLOGO"] = "1"
        return environment

    def command(self, *arguments: str) -> list[str]:
        return [self.dotnet, str(self.host_dll), *arguments]

    def start_host(self, state: Path, port: int) -> subprocess.Popen[str]:
        flags = subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0
        process = subprocess.Popen(
            self.command(
                "--state-path",
                str(state),
                "--environment",
                "Production",
                "--bind-host",
                "127.0.0.1",
                "--probe-host",
                "127.0.0.1",
                "--port",
                str(port),
            ),
            cwd=self.case,
            env=self.environment(),
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            creationflags=flags,
        )
        self.processes.append(process)
        return process

    def wait_healthy(self, process: subprocess.Popen[str], port: int) -> None:
        deadline = time.monotonic() + 8
        while time.monotonic() < deadline:
            if process.poll() is not None:
                stdout, stderr = process.communicate(timeout=1)
                self.fail(f"host exited before health: {stdout} {stderr}")
            try:
                if http_json(port, "healthz").get("status") == "ok":
                    return
            except (OSError, json.JSONDecodeError, urllib.error.URLError):
                time.sleep(0.1)
        self.fail("host did not become healthy within 8 seconds")

    def graceful_stop(self, process: subprocess.Popen[str]) -> tuple[int, str]:
        if os.name == "nt" or (hasattr(os, "geteuid") and os.geteuid() == 0):
            process.send_signal(signal.CTRL_BREAK_EVENT)
        else:
            process.send_signal(signal.SIGTERM)
        return_code = process.wait(timeout=5)
        stdout = process.stdout.read() if process.stdout else ""
        stderr = process.stderr.read() if process.stderr else ""
        return return_code, stdout + stderr

    def test_build_info_and_fail_closed_commands(self) -> None:
        result = subprocess.run(
            self.command("--build-info"),
            cwd=self.case,
            env=self.environment(),
            text=True,
            capture_output=True,
            timeout=5,
            check=True,
        )
        info = json.loads(result.stdout)
        self.assertEqual(info["contract"], "v01-008/1")
        self.assertEqual(info["source_revision"], "unissued")
        serialized = json.dumps(info)
        self.assertNotIn(os.environ.get("USERNAME", "__missing_user__"), serialized)
        self.assertNotIn(os.environ.get("COMPUTERNAME", "__missing_machine__"), serialized)
        self.assertNotIn(str(ROOT), serialized)

        missing = subprocess.run(
            self.command(),
            cwd=self.case,
            env=self.environment(),
            text=True,
            capture_output=True,
            timeout=5,
        )
        self.assertEqual(missing.returncode, 64)
        self.assertEqual(json.loads(missing.stderr)["code"], "invalid_state_path")

        state = self.case / "state"
        state.mkdir()
        missing_environment = subprocess.run(
            self.command("--state-path", str(state)),
            cwd=self.case,
            env=self.environment(),
            text=True,
            capture_output=True,
            timeout=5,
        )
        self.assertEqual(missing_environment.returncode, 64)
        self.assertEqual(json.loads(missing_environment.stderr)["code"], "invalid_environment")

        unavailable = subprocess.run(
            self.command("--health-probe", "--port", str(free_port())),
            cwd=self.case,
            env=self.environment(),
            text=True,
            capture_output=True,
            timeout=4,
        )
        self.assertEqual(unavailable.returncode, 69)

    def test_health_ready_probe_graceful_restart_and_zero_listener_residue(self) -> None:
        state = self.case / "state"
        state.mkdir()
        port = free_port()
        first = self.start_host(state, port)
        self.wait_healthy(first, port)
        self.assertEqual(
            http_json(port, "healthz"),
            {"status": "ok", "contract": "v01-008/1"},
        )
        self.assertEqual(
            http_json(port, "readyz"),
            {
                "status": "ready",
                "contract": "v01-008/1",
                "scope": "host_only",
                "business_api_ready": False,
                "production_file_writes_enabled": False,
            },
        )
        probe = subprocess.run(
            self.command("--health-probe", "--probe-host", "127.0.0.1", "--port", str(port)),
            cwd=self.case,
            env=self.environment(),
            timeout=4,
        )
        self.assertEqual(probe.returncode, 0)
        code, output = self.graceful_stop(first)
        self.assertEqual(code, 0)
        self.assertNotIn(str(state), output)
        self.assertFalse(any(path.name.startswith(".assetlibrary-host-write-probe") for path in state.iterdir()))

        restarted = self.start_host(state, port)
        self.wait_healthy(restarted, port)
        code, output = self.graceful_stop(restarted)
        self.assertEqual(code, 0)
        self.assertNotIn(str(state), output)
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
            listener.bind(("127.0.0.1", port))

    def test_occupied_port_and_unavailable_state_fail_without_residue(self) -> None:
        state = self.case / "state"
        state.mkdir()
        port = free_port()
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as occupied:
            occupied.bind(("127.0.0.1", port))
            occupied.listen()
            result = subprocess.run(
                self.command("--state-path", str(state), "--environment", "Production", "--port", str(port)),
                cwd=self.case,
                env=self.environment(),
                text=True,
                capture_output=True,
                timeout=5,
            )
        self.assertEqual(result.returncode, 70)
        self.assertEqual(json.loads(result.stderr)["code"], "host_start_failed")

        not_directory = self.case / "not-directory"
        not_directory.write_text("fixture", encoding="utf-8")
        result = subprocess.run(
            self.command(
                "--state-path",
                str(not_directory),
                "--environment",
                "Production",
                "--port",
                str(free_port()),
            ),
            cwd=self.case,
            env=self.environment(),
            text=True,
            capture_output=True,
            timeout=5,
        )
        self.assertEqual(result.returncode, 64)
        self.assertEqual(json.loads(result.stderr)["code"], "state_path_unavailable")
        self.assertFalse(any(path.name.startswith(".assetlibrary-host-write-probe") for path in state.iterdir()))

    def test_read_only_state_fails_closed_on_non_root_linux(self) -> None:
        state = self.case / "read-only-state"
        state.mkdir()
        if os.name == "nt":
            unavailable = self.case / "state-file"
            unavailable.write_text("fixture", encoding="utf-8")
            expected_code = "state_path_unavailable"
            state_argument = unavailable
        else:
            state.chmod(0o500)
            expected_code = "state_path_unwritable"
            state_argument = state
        try:
            result = subprocess.run(
                self.command(
                    "--state-path",
                    str(state_argument),
                    "--environment",
                    "Production",
                    "--port",
                    str(free_port()),
                ),
                cwd=self.case,
                env=self.environment(),
                text=True,
                capture_output=True,
                timeout=5,
            )
        finally:
            if state_argument == state:
                state.chmod(0o700)
        self.assertEqual(result.returncode, 64)
        self.assertEqual(json.loads(result.stderr)["code"], expected_code)


if __name__ == "__main__":
    unittest.main()
