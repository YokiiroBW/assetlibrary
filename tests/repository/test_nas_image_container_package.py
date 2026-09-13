from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]


def load(name: str, relative: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


BUILDER = load("nas_container_builder", "scripts/build_nas_deployment.py")
BOOTSTRAP = load("nas_container_bootstrap", "infra/docker/nas/bootstrap.py")
REVISION = "a" * 40
IMAGE = "sha256:" + "b" * 64
DEPLOYMENT = "6d0c7db7-a2f3-4967-bc6a-1b6d8d994cef"
NAME = "assetlibrary-test"


def inspection():
    executable = "/app/image-supervisor/AssetLibrary.ImageSupervisor"
    return [{
        "Image": IMAGE, "Path": executable, "Args": [],
        "Config": {"User": "0:0", "Entrypoint": [executable], "Cmd": [],
                   "Labels": {"io.assetlibrary.deployment": DEPLOYMENT, "io.assetlibrary.product": "nas-read-only-v1",
                              "io.assetlibrary.role": "nas-image-supervisor", "org.opencontainers.image.revision": REVISION,
                              "com.docker.compose.project": NAME, "com.docker.compose.service": "image"},
                   "Healthcheck": {"Test": ["CMD", executable, "--health"]}},
        "HostConfig": {"Init": False, "Privileged": False, "ReadonlyRootfs": True,
                       "CapAdd": ["CAP_CHOWN", "CAP_SETUID", "CAP_SETGID", "CAP_KILL"], "CapDrop": ["ALL"],
                       "SecurityOpt": ["no-new-privileges:true"], "NetworkMode": "none", "PidMode": "",
                       "IpcMode": "private", "UTSMode": "", "PortBindings": {}, "PublishAllPorts": False,
                       "Memory": 536870912, "CpuShares": 256, "RestartPolicy": {"Name": "unless-stopped"}},
        "Mounts": [{"Type": "volume", "Name": NAME + "-image-ipc", "Destination": "/run/assetlibrary-image", "RW": True}],
        "State": {"Running": True, "Pid": 100, "Health": {"Status": "healthy"}},
    }]


class NasImageContainerPolicyTests(unittest.TestCase):
    def verify(self, document, *, running=True):
        return BOOTSTRAP.verify_image_container(document, DEPLOYMENT, NAME, IMAGE, REVISION, running=running)

    def test_actual_container_metadata_and_health_are_checked_separately(self):
        self.assertEqual("image_healthy", self.verify(inspection())["status"])
        stopped = inspection()
        stopped[0]["State"] = {"Running": False, "Status": "created", "Pid": 0}
        self.assertEqual("image_configuration_verified", self.verify(stopped, running=False)["status"])
        with self.assertRaisesRegex(BOOTSTRAP.DeploymentError, "unavailable"):
            self.verify(stopped)

    def test_wrong_identity_capabilities_limits_and_namespaces_fail_closed(self):
        mutations = [
            ("Config", "User", "1655:1655"), ("Config", "Entrypoint", ["/sbin/docker-init", "--", "/app/image-supervisor/AssetLibrary.ImageSupervisor"]),
            ("Config", "Cmd", ["--unsafe"]), ("HostConfig", "Init", True), ("HostConfig", "Privileged", True),
            ("HostConfig", "ReadonlyRootfs", False), ("HostConfig", "NetworkMode", "bridge"),
            ("HostConfig", "PidMode", "host"), ("HostConfig", "IpcMode", "host"), ("HostConfig", "UTSMode", "host"),
            ("HostConfig", "CapAdd", ["CHOWN", "SETUID", "SETGID", "KILL", "SYS_ADMIN"]),
            ("HostConfig", "CapDrop", []), ("HostConfig", "SecurityOpt", ["no-new-privileges:true", "seccomp=unconfined"]),
            ("HostConfig", "SecurityOpt", ["apparmor=unconfined"]), ("HostConfig", "Memory", 0),
            ("HostConfig", "CpuShares", 1024), ("HostConfig", "PortBindings", {"1234/tcp": [{"HostPort": "1234"}]}),
            ("HostConfig", "Devices", [{"PathOnHost": "/dev/mem"}]), ("HostConfig", "VolumesFrom", ["core"]),
            ("Config", "Healthcheck", {"Test": ["CMD-SHELL", "true"]}),
            ("Config", "Env", ["PGPASSWORD=synthetic-not-a-secret"]), ("HostConfig", "Tmpfs", {"/tmp": "rw,size=1g"}),
        ]
        for section, key, value in mutations:
            with self.subTest(section=section, key=key, value=value):
                changed = inspection()
                changed[0][section][key] = value
                with self.assertRaises(BOOTSTRAP.DeploymentError):
                    self.verify(changed)
        for key in ("io.assetlibrary.deployment", "io.assetlibrary.role", "org.opencontainers.image.revision", "com.docker.compose.project"):
            changed = inspection()
            changed[0]["Config"]["Labels"][key] = "foreign"
            with self.subTest(label=key), self.assertRaisesRegex(BOOTSTRAP.DeploymentError, "identity"):
                self.verify(changed)

    def test_assets_state_database_and_docker_socket_mounts_are_rejected(self):
        for target in ("/assets/photos", "/var/lib/assetlibrary", "/var/lib/postgresql/data", "/var/run/docker.sock"):
            changed = inspection()
            changed[0]["Mounts"].append({"Type": "bind", "Source": "/synthetic", "Destination": target, "RW": False})
            with self.subTest(target=target), self.assertRaisesRegex(BOOTSTRAP.DeploymentError, "mount"):
                self.verify(changed)
        for key, value in (("RW", False), ("Type", "bind"), ("Name", "foreign-ipc")):
            changed = inspection()
            changed[0]["Mounts"][0][key] = value
            with self.subTest(key=key), self.assertRaisesRegex(BOOTSTRAP.DeploymentError, "mount"):
                self.verify(changed)

    def test_compose_keeps_optional_image_separate_and_only_core_gets_readonly_socket(self):
        compose = (ROOT / "infra/docker/nas/compose.yaml").read_text(encoding="utf-8")
        image = compose.split("  image:\n", 1)[1].split("  setup:\n", 1)[0]
        core = compose.split("  core:\n", 1)[1].split("  image:\n", 1)[0]
        setup = compose.split("  setup:\n", 1)[1].split("\nnetworks:", 1)[0]
        for required in ("profiles: [image]", "init: false", "network_mode: none", "ipc: private", "read_only: true",
                         "cap_drop: [ALL]", "cap_add: [CHOWN, SETUID, SETGID, KILL]", "mem_limit: 512m",
                         "cpu_shares: 256", "restart: unless-stopped"):
            self.assertIn(required, image)
        for forbidden in ("ports:", "/assets/", "core-state:", "postgres-data:", "setup-state:", "docker.sock", "init: true"):
            self.assertNotIn(forbidden, image)
        self.assertIn("image-ipc:/run/assetlibrary-image:ro", core)
        self.assertIn("ASSETLIBRARY_IMAGE_PREVIEW_SOCKET:", core)
        self.assertNotIn("image-ipc", setup)
        self.assertNotIn("      image:", core)
        self.assertEqual({"core", "setup", "postgres", "image"}, set(BUILDER.IMAGE_ENV_KEYS))
        self.assertEqual("IMAGE_PREVIEW", BUILDER.IMAGE_ENV_KEYS["image"])

    def test_native_supervisor_build_graph_and_pid1_do_not_add_runtime_tooling(self):
        dockerfile = (ROOT / "infra/docker/nas/Dockerfile").read_text(encoding="utf-8").replace("\\\n", " ")
        instruction = next(line for line in dockerfile.splitlines() if line.startswith("RUN dotnet restore services/worker-supervisor/ImageSupervisor/"))
        restore, publish = [shlex.split(part) for part in instruction.removeprefix("RUN ").split("&&")[:2]]
        properties = []
        for arguments in (restore, publish):
            values = dict(argument[3:].split("=", 1) for argument in arguments if argument.startswith("-p:"))
            if "--self-contained" in arguments:
                values["SelfContained"] = arguments[arguments.index("--self-contained") + 1]
            properties.append(values)
        for key in ("RuntimeIdentifier", "PublishAot", "SelfContained", "AssetLibraryReleaseLockRoot"):
            self.assertEqual(properties[0][key], properties[1][key])
        self.assertIn("--locked-mode", restore)
        self.assertIn("--no-restore", publish)
        runtime = dockerfile.split("FROM native-base AS image\n", 1)[1].split("FROM native-base AS core\n", 1)[0]
        self.assertIn('ENTRYPOINT ["/app/image-supervisor/AssetLibrary.ImageSupervisor"]', runtime)
        self.assertIn("CMD []", runtime)
        self.assertIn("chown 0:1654 /run/assetlibrary-image", runtime)
        self.assertIn("chmod 0710 /run/assetlibrary-image", runtime)
        self.assertNotIn("/opt/dotnet", runtime)
        self.assertNotIn("apt-get", runtime)


class NasImageSupervisorPayloadTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="nas-supervisor-package-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.package = self.root / "supervisor"
        self.package.mkdir()
        self.source = self.root / "source"
        lock = self.source / "services/worker-supervisor/ImageSupervisor/locks/AssetLibrary.ImageSupervisor.linux-x64.lock.json"
        lock.parent.mkdir(parents=True)
        lock.write_text('{"synthetic_lock":true}\n', encoding="ascii")
        header = bytearray(64)
        header[:7] = b"\x7fELF\x02\x01\x01"
        header[16:20] = b"\x03\x00\x3e\x00"
        for index, name in enumerate(BUILDER.IMAGE_SUPERVISOR_FILES):
            (self.package / name).write_bytes(bytes(header) if index == 0 else b"Synthetic .NET license fixture\n")
            (self.package / name).chmod(0o755 if index == 0 else 0o644)
        (self.package / "SOURCE_REVISION").write_text(REVISION + "\n", encoding="ascii", newline="\n")
        self.checksums()

    def checksums(self):
        (self.package / "SHA256SUMS").write_text("".join(hashlib.sha256((self.package / name).read_bytes()).hexdigest() + "  " + name + "\n"
                                                       for name in BUILDER.IMAGE_SUPERVISOR_FILES), encoding="ascii", newline="\n")

    def manifest(self):
        return BUILDER.image_supervisor_manifest(self.package, REVISION, self.source, IMAGE)

    def test_manifest_binds_native_binary_licenses_source_lock_and_no_default_enable(self):
        manifest = self.manifest()
        self.assertEqual(IMAGE, manifest["image_id"])
        self.assertEqual(REVISION, manifest["source_revision"])
        self.assertEqual("/app/image-supervisor/AssetLibrary.ImageSupervisor", manifest["executable"])
        self.assertEqual("/run/assetlibrary-image/decoder.sock", manifest["socket"])
        self.assertEqual({"CHOWN", "SETUID", "SETGID", "KILL"}, set(manifest["steady_capabilities"]))
        self.assertFalse(manifest["enabled_by_default"])
        self.assertEqual(5, len(manifest["files"]))

    def test_missing_extra_or_mutated_supervisor_payload_is_rejected(self):
        for path in list(self.package.iterdir()):
            original = path.read_bytes()
            path.write_bytes(original + b"changed")
            with self.subTest(name=path.name), self.assertRaises(BUILDER.RELEASE.ReleaseBuildError):
                self.manifest()
            path.write_bytes(original)
        extra = self.package / "AssetLibrary.ImageSupervisor.dll"
        extra.write_bytes(b"managed output not in native runtime package")
        with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "missing or unexpected"):
            self.manifest()

    def test_foreign_platform_or_empty_license_cannot_pass_with_new_checksum(self):
        executable = self.package / BUILDER.IMAGE_SUPERVISOR_FILES[0]
        original = executable.read_bytes()
        executable.write_bytes(b"MZ" + bytes(62))
        self.checksums()
        with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "Linux x86-64 ELF"):
            self.manifest()
        executable.write_bytes(original)
        (self.package / BUILDER.IMAGE_SUPERVISOR_FILES[1]).write_bytes(b"")
        self.checksums()
        with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "empty or exceeds"):
            self.manifest()

    def test_inspector_never_starts_a_container_and_reclaims_its_exact_id_on_error(self):
        calls = []
        (self.package / "SOURCE_REVISION").unlink()

        def command(arguments, **kwargs):
            calls.append(arguments)
            if arguments[1] == "cp":
                shutil.copytree(self.package, Path(arguments[-1]), dirs_exist_ok=True)
            return "c" * 64

        with mock.patch.object(BUILDER, "command", side_effect=command):
            with self.assertRaises(BUILDER.RELEASE.ReleaseBuildError):
                BUILDER.inspect_image_supervisor("docker", IMAGE, self.root / "inspection", self.source, REVISION)
        self.assertEqual(["create", "cp", "rm"], [call[1] for call in calls])
        self.assertEqual(calls[0][calls[0].index("--name") + 1], calls[-1][-1])
        self.assertIn("--read-only", calls[0])


class NasImageBuildOrchestrationTests(unittest.TestCase):
    def test_one_source_builds_four_pinned_images_and_binds_both_decoder_copies(self):
        with tempfile.TemporaryDirectory(prefix="nas-four-images-") as temporary:
            root = Path(temporary)
            shutil.copy2(ROOT / "Directory.Build.props", root / "Directory.Build.props")
            output = root / ".runtime/sandbox-storage/V01-022/build"
            calls = []

            def git_output(directory, *arguments):
                return "" if arguments[0] == "status" else REVISION

            def snapshot(directory, revision, target):
                destination = target / "infra/docker/nas"
                destination.mkdir(parents=True)
                for filename in ("compose.yaml", "nasctl.sh", "settings.example.json", "README.md"):
                    shutil.copy2(ROOT / "infra/docker/nas" / filename, destination / filename)
                return target

            def command(arguments, **kwargs):
                calls.append(arguments)
                if arguments[1:3] == ["image", "inspect"]:
                    requested = arguments[3]
                    return json.dumps([{"Architecture": "amd64", "RepoDigests": [requested + "@" + IMAGE], "Id": IMAGE,
                                        "Config": {"Labels": {"org.opencontainers.image.revision": REVISION}}}])
                return ""

            def archive(docker, tags, destination, source):
                self.assertEqual(4, len(tags))
                destination.write_bytes(b"synthetic Docker archive; not executable")

            with (mock.patch.object(BUILDER, "ROOT", root), mock.patch.object(BUILDER.RELEASE, "git_output", side_effect=git_output),
                  mock.patch.object(BUILDER.RELEASE, "create_source_snapshot", side_effect=snapshot),
                  mock.patch.object(BUILDER, "command", side_effect=command),
                  mock.patch.object(BUILDER, "inspect_image_preview", return_value={"files": [{"path": "same-worker", "sha256": "c" * 64}]}),
                  mock.patch.object(BUILDER, "inspect_image_supervisor", return_value={"source_revision": REVISION}),
                  mock.patch.object(BUILDER, "save_images_archive", side_effect=archive)):
                result = BUILDER.build(SimpleNamespace(output_root=output, docker="mock-docker", context_only=False))
            self.assertEqual("built_not_deployed", result["status"])
            self.assertEqual({"core", "setup", "postgres", "image"}, set(result["image_ids"]))
            builds = [call for call in calls if call[1] == "build"]
            self.assertEqual(["core", "setup", "image"], [call[call.index("--target") + 1] for call in builds])
            delivery = output / "assetlibrary-nas"
            env = (delivery / "images.env").read_text(encoding="utf-8")
            self.assertIn("ASSETLIBRARY_IMAGE_PREVIEW_IMAGE=assetlibrary/nas-image:" + REVISION, env)
            manifest = json.loads((delivery / "build-manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(REVISION, manifest["image_container"]["supervisor"]["source_revision"])
            self.assertEqual(manifest["image_preview"]["files"], manifest["image_container"]["decoder"]["files"])


class NasCtlImageFlowTests(unittest.TestCase):
    def setUp(self):
        candidates = [shutil.which("sh"), str(Path(shutil.which("git") or "").parent.parent / "usr/bin/sh.exe")]
        self.shell = next((value for value in candidates if value and Path(value).is_file()), None)
        if not self.shell:
            self.skipTest("A local POSIX shell is required for the NAS CLI fixture")
        self.temporary = tempfile.TemporaryDirectory(prefix="nasctl-image-fixture-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.bundle = self.root / "bundle"
        self.bin = self.root / "bin"
        self.bundle.mkdir()
        self.bin.mkdir()
        for filename in ("nasctl.sh", "compose.yaml", "README.md", "settings.example.json"):
            (self.bundle / filename).write_text((ROOT / "infra/docker/nas" / filename).read_text(encoding="utf-8"), encoding="utf-8", newline="\n")
        env = (f"ASSETLIBRARY_DEPLOYMENT_NAME={NAME}\nASSETLIBRARY_DEPLOYMENT_ID={DEPLOYMENT}\n"
               "ASSETLIBRARY_BIND_ADDRESS=127.0.0.1\nASSETLIBRARY_HTTPS_PORT=5443\nASSETLIBRARY_SETTINGS_SHA256=" + "d" * 64 + "\n")
        (self.bundle / "deployment.env").write_text(env, encoding="ascii", newline="\n")
        (self.bundle / "render-env").write_text(env, encoding="ascii", newline="\n")
        (self.bundle / "assets.compose.json").write_text('{"services":{"core":{"volumes":[]}}}\n', encoding="ascii")
        (self.bundle / "settings.json").write_text('{}\n', encoding="ascii")
        (self.bundle / "build-manifest.json").write_text('{"synthetic":true}\n', encoding="ascii")
        image_env = "ASSETLIBRARY_SOURCE_REVISION=" + REVISION + "\n"
        for key in BUILDER.IMAGE_ENV_KEYS.values():
            image_env += f"ASSETLIBRARY_{key}_IMAGE=fixture-{key}\nASSETLIBRARY_{key}_IMAGE_ID={IMAGE}\n"
        (self.bundle / "images.env").write_text(image_env, encoding="ascii", newline="\n")
        self.sums()
        fake = self.bin / "docker"
        fake.write_text(r'''#!/bin/sh
set -eu
printf '%s\n' "$*" >> "$FAKE_BUNDLE/calls"
case "$1 $2" in
  'image inspect') printf '%s\n' "$FAKE_IMAGE"; exit 0 ;;
  'volume inspect')
    if [ "${3:-}" = --format ]; then
      case "$4" in
        '{{.Driver}}') printf 'local\n' ;;
        '{{json .Options}}') printf 'null\n' ;;
        *) if [ "${FAKE_FOREIGN:-0}" = 1 ]; then printf 'foreign\n'; else printf '%s\n' "$FAKE_DEPLOYMENT"; fi ;;
      esac
    fi
    exit 0 ;;
  'network inspect') [ "${3:-}" != --format ] || printf '%s\n' "$FAKE_DEPLOYMENT"; exit 0 ;;
esac
case "$1" in
  ps) exit 0 ;;
  create) printf '%064d\n' 1; exit 0 ;;
  rm) exit 0 ;;
  info) printf 'true\n'; exit 0 ;;
  inspect) printf '[]\n'; exit 0 ;;
  run)
    for argument do
      case "$argument" in
        render-env) cat "$FAKE_BUNDLE/render-env"; exit 0 ;;
        render-mounts) cat "$FAKE_BUNDLE/assets.compose.json"; exit 0 ;;
        verify-image-container) cat >/dev/null; printf '{"status":"verified-fixture"}\n'; exit 0 ;;
      esac
    done
    exit 0 ;;
  compose)
    action=; quiet=0
    for argument do
      case "$argument" in up|create|stop|down|ps) action=$argument ;; --quiet) quiet=1 ;; esac
      last=$argument
    done
    if [ "$action" = ps ] && [ "$quiet" = 1 ]; then printf '%064d\n' 2; fi
    if [ "$action" = up ] && [ "$last" = image ] && [ "${FAKE_HEALTH_FAIL:-0}" = 1 ]; then exit 1; fi
    exit 0 ;;
esac
exit 9
''', encoding="ascii", newline="\n")
        fake.chmod(0o755)
        if os.name == "nt":
            # The bundled Git shell omits sha256sum; this test-only shim performs
            # real SHA256 checks with the same Python running the test suite.
            checker = self.bundle / "checksums.py"
            checker.write_text("import hashlib, pathlib, sys\nfor line in sys.stdin:\n expected, name = line.rstrip().split('  ', 1)\n if hashlib.sha256(pathlib.Path(name).read_bytes()).hexdigest() != expected: sys.exit(1)\n", encoding="ascii")
            python = self.posix(Path(sys.executable))
            checksum = self.bin / "sha256sum"
            checksum.write_text(f'#!/bin/sh\nexec "{python}" -I -B "$FAKE_BUNDLE/checksums.py" "$@"\n', encoding="ascii", newline="\n")
            checksum.chmod(0o755)

    @staticmethod
    def posix(path):
        text = path.as_posix()
        return "/" + text[0].lower() + text[2:] if os.name == "nt" and len(text) > 2 and text[1] == ":" else text

    def sums(self):
        names = ("nasctl.sh", "compose.yaml", "README.md", "settings.example.json", "images.env", "build-manifest.json")
        (self.bundle / "SHA256SUMS").write_text("".join(hashlib.sha256((self.bundle / name).read_bytes()).hexdigest() + "  " + name + "\n" for name in names), encoding="ascii", newline="\n")

    def run_cli(self, action="start", **overrides):
        environment = os.environ.copy()
        for key in ("ASSETLIBRARY_IMAGE_PREVIEW_SOCKET", "ASSETLIBRARY_IMAGE_PREVIEW_WORKER"):
            environment.pop(key, None)
        environment.update({"FAKE_BIN": self.posix(self.bin), "FAKE_BUNDLE": self.posix(self.bundle), "FAKE_TOOLS": self.posix(Path(self.shell).parent), "FAKE_IMAGE": IMAGE, "FAKE_DEPLOYMENT": DEPLOYMENT})
        environment.update(overrides)
        script = ('PATH="$FAKE_BIN:$FAKE_TOOLS:$PATH"; export PATH; '
                  '[ "$(command -v docker)" = "$FAKE_BIN/docker" ] || exit 99; exec sh "$FAKE_BUNDLE/nasctl.sh" "$@"')
        return subprocess.run([self.shell, "-c", script, "nasctl-fixture", action], cwd=self.bundle,
                              env=environment, capture_output=True, text=True, timeout=20)

    def calls(self):
        path = self.bundle / "calls"
        return path.read_text(encoding="utf-8").splitlines() if path.exists() else []

    def test_image_failure_is_reported_after_core_start_and_preserves_state(self):
        result = self.run_cli(ASSETLIBRARY_IMAGE_PREVIEW_SOCKET=BUILDER.IMAGE_SOCKET_PATH, FAKE_HEALTH_FAIL="1")
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertIn("Core is running, but image preview is unavailable", result.stderr)
        calls = self.calls()
        core = next(index for index, call in enumerate(calls) if " up " in call and call.endswith(" core"))
        image = next(index for index, call in enumerate(calls) if " up " in call and call.endswith(" image"))
        self.assertLess(core, image)
        self.assertFalse(any("volume rm" in call or "supervisor-state" in call for call in calls))
        preparation = [call for call in calls if call.startswith("create ")]
        self.assertEqual(1, len(preparation))
        self.assertIn("--entrypoint /bin/false", preparation[0])
        self.assertNotIn("/assets", preparation[0])

    def test_disabled_backend_starts_core_without_image_and_stops_an_old_image_service(self):
        result = self.run_cli()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(any(" up " in call and call.endswith(" core") for call in self.calls()))
        self.assertFalse(any(" up " in call and call.endswith(" image") for call in self.calls()))
        self.assertTrue(any(" stop " in call and call.endswith(" image") for call in self.calls()))

    def test_persisted_socket_survives_start_and_explicit_empty_disables_it(self):
        with (self.bundle / "deployment.env").open("a", encoding="ascii", newline="\n") as stream:
            stream.write("ASSETLIBRARY_IMAGE_PREVIEW_SOCKET=" + BUILDER.IMAGE_SOCKET_PATH + "\n")
        result = self.run_cli()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(any(" up " in call and call.endswith(" image") for call in self.calls()))
        (self.bundle / "calls").unlink()
        result = self.run_cli(ASSETLIBRARY_IMAGE_PREVIEW_SOCKET="")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse(any(" up " in call and call.endswith(" image") for call in self.calls()))
        self.assertTrue(any(" stop " in call and call.endswith(" image") for call in self.calls()))

    def test_duplicate_optional_socket_including_empty_lines_is_rejected(self):
        with (self.bundle / "deployment.env").open("a", encoding="ascii", newline="\n") as stream:
            stream.write("ASSETLIBRARY_IMAGE_PREVIEW_SOCKET=\nASSETLIBRARY_IMAGE_PREVIEW_SOCKET=\n")
        result = self.run_cli()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Duplicate optional deployment field", result.stderr)
        self.assertFalse(any(" up " in call for call in self.calls()))

    def test_unsafe_backend_choice_control_drift_and_foreign_volume_fail_before_start(self):
        for overrides in ({"ASSETLIBRARY_IMAGE_PREVIEW_SOCKET": "/tmp/other.sock"},
                          {"ASSETLIBRARY_IMAGE_PREVIEW_SOCKET": BUILDER.IMAGE_SOCKET_PATH, "ASSETLIBRARY_IMAGE_PREVIEW_WORKER": "/app/workers/image-preview/AssetLibrary.ImagePreview.Worker"},
                          {"FAKE_FOREIGN": "1"}):
            with self.subTest(overrides=overrides):
                result = self.run_cli(**overrides)
                self.assertNotEqual(0, result.returncode)
                self.assertFalse(any(" up " in call or call.startswith("create ") for call in self.calls()))
                (self.bundle / "calls").unlink(missing_ok=True)
        with (self.bundle / "compose.yaml").open("a", encoding="utf-8") as stream:
            stream.write("# modified delivery control\n")
        result = self.run_cli()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("control file changed", result.stderr)
        self.assertEqual([], self.calls())


if __name__ == "__main__":
    unittest.main()
