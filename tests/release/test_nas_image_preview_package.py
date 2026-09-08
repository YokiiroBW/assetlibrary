from __future__ import annotations

import hashlib
import importlib.util
import os
from pathlib import Path
import shlex
import shutil
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("nas_preview_builder", ROOT / "scripts/build_nas_deployment.py")
assert SPEC and SPEC.loader
BUILDER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BUILDER)
REVISION = "a" * 40
IMAGE = "sha256:" + "b" * 64


class NasImagePreviewPackageTests(unittest.TestCase):
    def test_docker_restore_and_publish_resolve_the_same_self_contained_aot_graph(self):
        dockerfile = (ROOT / "infra/docker/nas/Dockerfile").read_text(encoding="utf-8").replace("\\\n", " ")
        instruction = next(line for line in dockerfile.splitlines()
                           if line.startswith("RUN dotnet restore services/worker-supervisor/ImagePreview/"))
        restore, publish = [shlex.split(part) for part in instruction.removeprefix("RUN ").split("&&")[:2]]
        properties = []
        for arguments in (restore, publish):
            values = dict(argument[3:].split("=", 1) for argument in arguments if argument.startswith("-p:"))
            if "--self-contained" in arguments:
                values["SelfContained"] = arguments[arguments.index("--self-contained") + 1]
            properties.append(values)
        for name in ("RuntimeIdentifier", "PublishAot", "SelfContained", "AssetLibraryReleaseLockRoot"):
            with self.subTest(property=name):
                self.assertIn(name, properties[0])
                self.assertEqual(properties[0][name], properties[1][name])
        self.assertEqual("true", properties[0]["SelfContained"])
        self.assertIn("--locked-mode", restore)
        self.assertIn("--no-restore", publish)

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="nas-preview-package-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.package = self.root / "image-preview"
        self.package.mkdir()
        header = bytearray(64)
        header[:7] = b"\x7fELF\x02\x01\x01"
        header[16:20] = b"\x03\x00\x3e\x00"
        for name in BUILDER.IMAGE_PREVIEW_FILES:
            (self.package / name).write_bytes(bytes(header) if name in BUILDER.IMAGE_PREVIEW_FILES[:2] else b"Synthetic license fixture\n")
        (self.package / "SOURCE_REVISION").write_text(REVISION + "\n", encoding="ascii", newline="\n")
        self.checksums()
        for path in self.package.iterdir():
            path.chmod(0o644)
        (self.package / BUILDER.IMAGE_PREVIEW_FILES[0]).chmod(0o755)

    def checksums(self):
        lines = [hashlib.sha256((self.package / name).read_bytes()).hexdigest() + "  " + name
                 for name in BUILDER.IMAGE_PREVIEW_FILES]
        (self.package / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="ascii", newline="\n")

    def manifest(self):
        return BUILDER.image_preview_manifest(self.package, REVISION, ROOT, IMAGE)

    def test_manifest_binds_actual_payload_image_revision_and_release_lock(self):
        manifest = self.manifest()
        self.assertEqual(REVISION, manifest["source_revision"])
        self.assertEqual(IMAGE, manifest["image_id"])
        self.assertEqual("linux-x64", manifest["runtime_identifier"])
        self.assertEqual("/app/workers/image-preview/AssetLibrary.ImagePreview.Worker", manifest["executable"])
        self.assertFalse(manifest["enabled_by_default"])
        self.assertEqual("not_executed_by_builder", manifest["platform_validation"])
        self.assertEqual(6, len(manifest["files"]))
        for entry in manifest["files"]:
            self.assertEqual(hashlib.sha256((self.package / entry["path"]).read_bytes()).hexdigest(), entry["sha256"])
        lock = ROOT / "services/worker-supervisor/ImagePreview/locks/AssetLibrary.ImagePreview.Worker.linux-x64.lock.json"
        self.assertEqual(hashlib.sha256(lock.read_bytes()).hexdigest(), manifest["release_lock_sha256"])

    def test_missing_worker_native_library_notices_or_integrity_files_fail(self):
        for path in list(self.package.iterdir()):
            with self.subTest(file=path.name):
                content, mode = path.read_bytes(), path.stat().st_mode
                path.unlink()
                try:
                    with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "missing or unexpected"):
                        self.manifest()
                finally:
                    path.write_bytes(content)
                    path.chmod(mode)

    def test_unexpected_managed_or_debug_output_fails(self):
        for name in ("AssetLibrary.ImagePreview.Worker.dll", "worker.dbg", "worker.pdb"):
            with self.subTest(file=name):
                path = self.package / name
                path.write_bytes(b"not a release payload")
                try:
                    with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "missing or unexpected"):
                        self.manifest()
                finally:
                    path.unlink()

    def test_source_revision_mismatch_fails_even_with_valid_files(self):
        (self.package / "SOURCE_REVISION").write_text("c" * 40 + "\n", encoding="ascii", newline="\n")
        with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "source revision differs"):
            self.manifest()

    def test_modified_payload_cannot_be_published_with_its_old_checksum(self):
        for name in BUILDER.IMAGE_PREVIEW_FILES:
            with self.subTest(file=name):
                path = self.package / name
                content = path.read_bytes()
                path.write_bytes(content + b"changed")
                try:
                    with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "checksum differs"):
                        self.manifest()
                finally:
                    path.write_bytes(content)

    def test_wrong_native_platform_fails_even_with_recomputed_checksum(self):
        path = self.package / BUILDER.IMAGE_PREVIEW_FILES[0]
        original = path.read_bytes()
        invalid = [b"MZ" + bytes(62), original[:18] + b"\xb7\x00" + original[20:], b"\x7fELF\x01" + original[5:]]
        for content in invalid:
            with self.subTest(header=content[:20]):
                path.write_bytes(content)
                self.checksums()
                with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "Linux x86-64 ELF"):
                    self.manifest()

    def test_empty_artifact_fails_even_with_recomputed_checksum(self):
        (self.package / "LICENSE.SkiaSharp.txt").write_bytes(b"")
        self.checksums()
        with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "empty or exceeds"):
            self.manifest()

    def test_ambiguous_partial_or_unsafe_checksum_lists_fail(self):
        path = self.package / "SHA256SUMS"
        original = path.read_text(encoding="ascii")
        line = original.splitlines()[0] + "\n"
        for content in (original + line, line, "0" * 64 + "  ../outside\n", "x" * 1025):
            with self.subTest(content=content[:70]):
                path.write_text(content, encoding="ascii", newline="\n")
                with self.assertRaises(BUILDER.RELEASE.ReleaseBuildError):
                    self.manifest()

    def test_inspection_never_starts_container_and_removes_it_on_invalid_payload(self):
        (self.package / "libSkiaSharp.so").unlink()
        calls = []

        def command(arguments, **kwargs):
            calls.append(arguments)
            if arguments[1] == "cp":
                shutil.copytree(self.package, Path(arguments[-1]), dirs_exist_ok=True)
            return "c" * 64

        with mock.patch.object(BUILDER, "command", side_effect=command):
            with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "missing or unexpected"):
                BUILDER.inspect_image_preview("docker", IMAGE, self.root / "inspection", ROOT, REVISION)
        self.assertEqual(["create", "cp", "rm"], [call[1] for call in calls])
        name = calls[0][calls[0].index("--name") + 1]
        self.assertEqual(name, calls[-1][-1])
        self.assertIn("--network", calls[0])
        self.assertIn("--read-only", calls[0])

    @unittest.skipIf(os.name == "nt", "POSIX executable permission is verified on the Linux package runner")
    def test_non_executable_worker_fails(self):
        (self.package / BUILDER.IMAGE_PREVIEW_FILES[0]).chmod(0o644)
        with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "not executable"):
            self.manifest()

    @unittest.skipIf(os.name == "nt", "POSIX runtime access is verified on the Linux package runner")
    def test_root_only_payload_fails(self):
        (self.package / "libSkiaSharp.so").chmod(0o600)
        with self.assertRaisesRegex(BUILDER.RELEASE.ReleaseBuildError, "not readable"):
            self.manifest()


if __name__ == "__main__":
    unittest.main()
