from __future__ import annotations

import argparse
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("native_client_serve", Path(__file__).with_name("serve.py"))
assert SPEC and SPEC.loader
SERVE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SERVE)


class NativeClientRunnerTests(unittest.TestCase):
    def write_image_manifest(self, source, relative="tiny.png", content=b"synthetic", **overrides):
        manifest = {"kind": "synthetic_preview_integration_inputs", "files": [
            {"path": relative, "bytes": len(content), "sha256": hashlib.sha256(content).hexdigest()}]}
        manifest.update(overrides)
        (source / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    def test_images_are_explicit_bounded_verified_copies(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, runtime = root / "corpus", root / "runtime"
            source.mkdir()
            runtime.mkdir()
            original = source / "tiny.png"
            original.write_bytes(b"synthetic")
            stamp = original.stat().st_mtime_ns
            self.write_image_manifest(source)
            copied = SERVE.stage_image_fixtures(source, runtime)
            self.assertEqual((copied / "tiny.png").read_bytes(), original.read_bytes())
            self.assertEqual(original.stat().st_mtime_ns, stamp)
            self.assertFalse((copied / "manifest.json").exists())

    def test_image_manifest_cannot_copy_outside_paths_or_unknown_corpora(self):
        for relative in ("../escape.png", "/absolute.png", "C:/private.png", "folder\\private.png"):
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                source, runtime = root / "corpus", root / "runtime"
                source.mkdir()
                runtime.mkdir()
                self.write_image_manifest(source, relative=relative)
                with self.assertRaises(ValueError):
                    SERVE.stage_image_fixtures(source, runtime)
                self.assertFalse((root / "escape.png").exists())
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.write_image_manifest(root, kind="ordinary_personal_folder")
            with self.assertRaisesRegex(ValueError, "synthetic"):
                SERVE.stage_image_fixtures(root, root)

    def test_changed_fixture_is_not_silently_used(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, runtime = root / "corpus", root / "runtime"
            source.mkdir()
            runtime.mkdir()
            (source / "tiny.png").write_bytes(b"different")
            self.write_image_manifest(source)
            with self.assertRaisesRegex(ValueError, "changed"):
                SERVE.stage_image_fixtures(source, runtime)

    def test_lifetime_cannot_be_unbounded(self):
        self.assertEqual(SERVE.lifetime("7200"), 7200)
        for value in ("0", "-1", "7201"):
            with self.assertRaises(argparse.ArgumentTypeError):
                SERVE.lifetime(value)

    def test_without_execute_never_reads_tools_or_starts_resources(self):
        self.assertEqual(SERVE.main(["--dotnet", "missing", "--postgres-bin", "missing",
                                     "--web-root", "missing"]), 77)

    def test_rendezvous_cannot_escape_the_owned_temporary_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            evidence = root / "evidence"
            runtime = root / "runtime"
            evidence.mkdir()
            runtime.mkdir()
            ready = {"origin": "https://localhost:12345", "connection_file": str(root / "outside.json"),
                     "stop_file": str(runtime / "stop")}
            (evidence / "ready.json").write_text(json.dumps(ready), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "escaped"):
                SERVE.read_ready(evidence, runtime)

    def test_rendezvous_keeps_the_actual_localhost_certificate_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            ready = {"origin": "https://10.0.2.2:12345", "connection_file": str(root / "connection.json"),
                     "stop_file": str(root / "stop")}
            (root / "ready.json").write_text(json.dumps(ready), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "localhost"):
                SERVE.read_ready(root, root)

    def test_process_check_recognizes_the_current_process(self):
        self.assertTrue(SERVE.process_exists(os.getpid()))


if __name__ == "__main__":
    unittest.main()
