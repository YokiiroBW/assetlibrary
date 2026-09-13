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
CORPUS_SPEC = importlib.util.spec_from_file_location("native_page_corpus", Path(__file__).with_name("make_page_corpus.py"))
assert CORPUS_SPEC and CORPUS_SPEC.loader
CORPUS = importlib.util.module_from_spec(CORPUS_SPEC)
CORPUS_SPEC.loader.exec_module(CORPUS)


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

    def write_sized_images(self, source, sizes):
        entries = []
        for index, size in enumerate(sizes):
            path = source / f"{index + 1:03}.png"
            with path.open("wb") as stream:
                stream.truncate(size)
            with path.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            entries.append({"path": path.name, "bytes": size, "sha256": digest})
        (source / "manifest.json").write_text(json.dumps({"kind": "synthetic_preview_integration_inputs", "files": entries}), encoding="utf-8")

    def test_image_count_accepts_128_and_rejects_129(self):
        for count in (128, 129):
            with self.subTest(count=count), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                source, runtime = root / "corpus", root / "runtime"
                source.mkdir()
                runtime.mkdir()
                self.write_sized_images(source, [1] * count)
                if count == 128:
                    copied = SERVE.stage_image_fixtures(source, runtime)
                    self.assertEqual(len(list(copied.iterdir())), 128)
                    self.assertTrue(all(path.read_bytes() == b"\0" for path in copied.iterdir()))
                else:
                    with self.assertRaisesRegex(ValueError, "bounded"):
                        SERVE.stage_image_fixtures(source, runtime)
                    self.assertFalse((runtime / "native-image-fixtures").exists())

    def test_actual_copy_accepts_64_mib_and_rejects_one_more_byte(self):
        for extra in (0, 1):
            with self.subTest(extra=extra), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                source, runtime = root / "corpus", root / "runtime"
                source.mkdir()
                runtime.mkdir()
                self.write_sized_images(source, [33554432, 33554432] + ([1] if extra else []))
                stamps = {path.name: path.stat().st_mtime_ns for path in source.iterdir()}
                if extra:
                    with self.assertRaisesRegex(ValueError, "bound"):
                        SERVE.stage_image_fixtures(source, runtime)
                else:
                    SERVE.stage_image_fixtures(source, runtime)
                copied = runtime / "native-image-fixtures"
                self.assertEqual(sum(path.stat().st_size for path in copied.iterdir()), 67108864)
                self.assertEqual(stamps, {path.name: path.stat().st_mtime_ns for path in source.iterdir()})

    def test_single_image_still_rejects_more_than_32_mib(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, runtime = root / "corpus", root / "runtime"
            source.mkdir()
            runtime.mkdir()
            self.write_sized_images(source, [33554433])
            with self.assertRaisesRegex(ValueError, "too large"):
                SERVE.stage_image_fixtures(source, runtime)
            self.assertEqual(list((runtime / "native-image-fixtures").iterdir()), [])

    def test_page_generator_reuses_real_synthetic_bytes_and_stages_120(self):
        before = {name: (CORPUS.sha256(CORPUS.SOURCE / name), (CORPUS.SOURCE / name).stat().st_mtime_ns) for name in CORPUS.NAMES}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output = CORPUS.generate(root / "corpus")
            entries = json.loads((output / "manifest.json").read_text(encoding="utf-8"))["files"]
            self.assertEqual(len(entries), 120)
            self.assertEqual([entry["path"].split(".")[0] for entry in entries], [f"{index:03}" for index in range(1, 121)])
            runtime = root / "runtime"
            runtime.mkdir()
            copied = SERVE.stage_image_fixtures(output, runtime)
            self.assertEqual(len(list(copied.iterdir())), 120)
            self.assertEqual({CORPUS.sha256(path) for path in copied.iterdir()}, {value[0] for value in before.values()})
            with self.assertRaises(FileExistsError):
                CORPUS.generate(output)
        self.assertEqual(before, {name: (CORPUS.sha256(CORPUS.SOURCE / name), (CORPUS.SOURCE / name).stat().st_mtime_ns) for name in CORPUS.NAMES})

    def test_page_generator_refuses_output_outside_test_storage(self):
        with self.assertRaisesRegex(ValueError, "temporary"):
            CORPUS.generate(CORPUS.ROOT / "docs" / "page-corpus-must-not-exist")

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
