from __future__ import annotations
import tempfile
import unittest
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "packages" / "test-support"))
from performance import GeneratorConfig, FaultKind, build_fault_plan, generate_assets, write_manifest

class GeneratorTests(unittest.TestCase):
    def test_deterministic_and_distribution(self):
        config = GeneratorConfig(count=10_005, seed=7, hot_directory_count=100)
        a = list(generate_assets(config)); b = list(generate_assets(config))
        self.assertEqual(a, b)
        self.assertEqual(len(a), 10_005)
        self.assertEqual(sum(x.directory_class == "hot-100k" for x in a), 100)
        self.assertEqual(a[0].logical_size_bytes, 100 * 1024**3)
        self.assertEqual(len({x.asset_id for x in a}), len(a))
        self.assertNotEqual(a, list(generate_assets(GeneratorConfig(count=10_005, seed=8, hot_directory_count=100))))

    def test_streaming_cancel_and_atomic_cleanup(self):
        calls = 0
        def cancel():
            nonlocal calls
            calls += 1
            return calls > 4
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "cancel.jsonl"
            result = write_manifest(GeneratorConfig(count=100), output, cancel)
            self.assertTrue(result["cancelled"])
            self.assertFalse(output.exists())
            self.assertFalse(output.with_name(".cancel.jsonl.partial").exists())

    def test_path_safety_and_idempotent_replacement(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "manifest.jsonl"
            first = write_manifest(GeneratorConfig(count=5, seed=2), output)
            second = write_manifest(GeneratorConfig(count=5, seed=2), output)
            self.assertEqual(first["manifest_sha256"], second["manifest_sha256"])
            with self.assertRaises(ValueError):
                write_manifest(GeneratorConfig(count=1), Path.cwd() / "unsafe.jsonl")

    def test_symlink_component_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp)
            real = base / "real"
            real.mkdir()
            link = base / "link"
            link.symlink_to(real, target_is_directory=True)
            with self.assertRaises(ValueError):
                write_manifest(GeneratorConfig(count=1), link / "manifest.jsonl")

    def test_fault_plan_covers_required_non_destructive_events(self):
        plan = build_fault_plan(3)
        self.assertEqual({e["kind"] for e in plan.events}, {k.value for k in FaultKind})
        self.assertTrue(all("trigger" in event for event in plan.events))

if __name__ == "__main__":
    unittest.main()
