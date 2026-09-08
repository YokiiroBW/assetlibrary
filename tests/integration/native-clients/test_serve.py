from __future__ import annotations

import argparse
import importlib.util
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
