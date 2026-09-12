import importlib.util
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("windows_package", ROOT / "infra/windows-client/build_package.py")
PACKAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGE)


class PackageTests(unittest.TestCase):
    def test_identical_dependency_is_merged_once(self):
        with tempfile.TemporaryDirectory(prefix="AssetLibrary-package-test-") as temporary:
            root = Path(temporary)
            source = root / "source.dll"
            destination = root / "output/dependency.dll"
            source.write_bytes(b"same dependency")
            PACKAGE.merge_file(source, destination)
            PACKAGE.merge_file(source, destination)
            self.assertEqual(destination.read_bytes(), source.read_bytes())

    def test_conflicting_dependency_preserves_first_component(self):
        with tempfile.TemporaryDirectory(prefix="AssetLibrary-package-test-") as temporary:
            root = Path(temporary)
            source = root / "source.dll"
            destination = root / "output.dll"
            source.write_bytes(b"first")
            PACKAGE.merge_file(source, destination)
            source.write_bytes(b"second")
            with self.assertRaisesRegex(ValueError, "conflicting bytes"):
                PACKAGE.merge_file(source, destination)
            self.assertEqual(destination.read_bytes(), b"first")

    def test_non_pe_is_rejected_without_loading_it(self):
        with tempfile.TemporaryDirectory(prefix="AssetLibrary-package-test-") as temporary:
            source = Path(temporary) / "fake.exe"
            source.write_bytes(b"synthetic file")
            with self.assertRaisesRegex(ValueError, "Not a Windows executable"):
                PACKAGE.pe_imports(source)

    def test_existing_output_is_preserved(self):
        with tempfile.TemporaryDirectory(prefix="AssetLibrary-package-test-") as temporary:
            root = Path(temporary)
            marker = root / "keep.txt"
            marker.write_text("existing output", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Output already exists"):
                PACKAGE.assemble(root, {}, root / "unused.dll", [])
            self.assertEqual(marker.read_text(encoding="utf-8"), "existing output")


if __name__ == "__main__":
    unittest.main()
