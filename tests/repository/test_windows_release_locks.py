from __future__ import annotations

import base64
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/validate_windows_release_locks.py"
SPEC = importlib.util.spec_from_file_location("windows_release_locks", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
LOCKS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(LOCKS)
HASH = base64.b64encode(b"a" * 64).decode("ascii")
OTHER_HASH = base64.b64encode(b"b" * 64).decode("ascii")


class WindowsReleaseLockTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.release_directory = self.root / LOCKS.LOCK_DIRECTORY
        self.release_directory.mkdir(parents=True)
        self.paths = {}
        for project, (directory, framework) in LOCKS.PROJECTS.items():
            package = {"type": "Direct", "requested": "[1.2.3, )", "resolved": "1.2.3", "contentHash": HASH}
            normal = {"version": 2, "dependencies": {framework: {"Fixture.Package": package}}}
            if project == "AssetLibrary.Windows.Settings":
                normal["dependencies"][f"{framework}/win-x64"] = copy.deepcopy(normal["dependencies"][framework])
            release = copy.deepcopy(normal)
            release["dependencies"].setdefault(f"{framework}/win-x64", {})
            normal_path = self.root / directory / "packages.lock.json"
            normal_path.parent.mkdir(parents=True, exist_ok=True)
            release_path = self.release_directory / f"{project}.win-x64.lock.json"
            self.write(normal_path, normal)
            self.write(release_path, release)
            self.paths[project] = (normal_path, release_path, framework)
        self.normal_path, self.release_path, self.framework = self.paths["AssetLibrary.Windows.Setup"]

    @staticmethod
    def write(path: Path, value: dict) -> None:
        path.write_text(json.dumps(value), encoding="utf-8")

    def mutate(self, path: Path, change) -> None:
        value = json.loads(path.read_text(encoding="utf-8"))
        change(value)
        self.write(path, value)

    def test_exact_six_projects_and_required_groups_pass(self) -> None:
        self.assertEqual(len(LOCKS.PROJECTS), 6)
        self.assertEqual(LOCKS.validate(self.root), 1)

    def test_missing_and_extra_locks_fail(self) -> None:
        saved = self.release_path.read_text(encoding="utf-8")
        self.release_path.unlink()
        with self.assertRaisesRegex(ValueError, "lock set mismatch.*missing="):
            LOCKS.validate(self.root)
        self.release_path.write_text(saved, encoding="utf-8")
        self.write(self.release_directory / "Unexpected.win-x64.lock.json", {})
        with self.assertRaisesRegex(ValueError, "lock set mismatch.*extra="):
            LOCKS.validate(self.root)

    def test_missing_default_lock_fails(self) -> None:
        self.normal_path.unlink()
        with self.assertRaisesRegex(ValueError, "missing lock"):
            LOCKS.validate(self.root)

    def test_missing_wrong_and_extra_rid_groups_fail(self) -> None:
        original = self.release_path.read_text(encoding="utf-8")
        for groups in ({self.framework: {}}, {self.framework: {}, f"{self.framework}/linux-x64": {}},
                       {self.framework: {}, f"{self.framework}/win-x64": {}, "net9.0/win-x64": {}}, []):
            with self.subTest(groups=groups):
                self.write(self.release_path, {"version": 2, "dependencies": groups})
                with self.assertRaisesRegex(ValueError, "target groups"):
                    LOCKS.validate(self.root)
        self.release_path.write_text(original, encoding="utf-8")
        self.mutate(self.normal_path, lambda lock: lock["dependencies"].update({f"{self.framework}/win-x64": {}}))
        with self.assertRaisesRegex(ValueError, "target groups"):
            LOCKS.validate(self.root)

    def test_settings_default_requires_its_existing_rid_group(self) -> None:
        normal, _, framework = self.paths["AssetLibrary.Windows.Settings"]
        self.mutate(normal, lambda lock: lock["dependencies"].pop(f"{framework}/win-x64"))
        with self.assertRaisesRegex(ValueError, "target groups"):
            LOCKS.validate(self.root)

    def test_missing_invalid_and_short_hashes_fail_in_both_lock_types(self) -> None:
        for path in (self.normal_path, self.release_path):
            original = path.read_text(encoding="utf-8")
            for digest in (None, "", "not-base64", base64.b64encode(b"a" * 32).decode("ascii")):
                with self.subTest(path=path.name, digest=digest):
                    self.mutate(path, lambda lock: lock["dependencies"][self.framework]["Fixture.Package"].update(contentHash=digest))
                    with self.assertRaisesRegex(ValueError, "SHA-512"):
                        LOCKS.validate(self.root)
                    path.write_text(original, encoding="utf-8")
            self.mutate(path, lambda lock: lock["dependencies"][self.framework]["Fixture.Package"].pop("contentHash"))
            with self.assertRaisesRegex(ValueError, "SHA-512"):
                LOCKS.validate(self.root)
            path.write_text(original, encoding="utf-8")

    def test_same_id_version_and_hash_drift_fail(self) -> None:
        original = self.release_path.read_text(encoding="utf-8")
        for field, value in (("resolved", "1.2.4"), ("contentHash", OTHER_HASH)):
            with self.subTest(field=field):
                self.mutate(self.release_path, lambda lock: lock["dependencies"][self.framework]["Fixture.Package"].update({field: value}))
                with self.assertRaisesRegex(ValueError, "version/hash mismatch"):
                    LOCKS.validate(self.root)
                self.release_path.write_text(original, encoding="utf-8")

    def test_default_package_cannot_be_removed_or_disguised_as_project(self) -> None:
        self.mutate(self.release_path, lambda lock: lock["dependencies"][self.framework].clear())
        with self.assertRaisesRegex(ValueError, "normal package missing"):
            LOCKS.validate(self.root)
        self.mutate(self.release_path, lambda lock: lock["dependencies"][self.framework].update({"Fixture.Package": {"type": "Project"}}))
        with self.assertRaisesRegex(ValueError, "invalid project dependency"):
            LOCKS.validate(self.root)

    def test_release_only_build_package_is_hashed_and_globally_consistent(self) -> None:
        extra = {"type": "Direct", "resolved": "10.0.11", "contentHash": HASH}
        self.mutate(self.release_path, lambda lock: lock["dependencies"][self.framework].update({"Microsoft.NET.ILLink.Tasks": extra}))
        self.assertEqual(LOCKS.validate(self.root), 2)
        _, other, framework = self.paths["AssetLibrary.Windows.AssetHost"]
        self.mutate(other, lambda lock: lock["dependencies"][f"{framework}/win-x64"].update({"Microsoft.NET.ILLink.Tasks": {**extra, "contentHash": OTHER_HASH}}))
        with self.assertRaisesRegex(ValueError, "version/hash mismatch across release locks"):
            LOCKS.validate(self.root)

    def test_rid_group_cannot_override_baseline_package(self) -> None:
        package = {"type": "Transitive", "resolved": "1.2.3", "contentHash": OTHER_HASH}
        self.mutate(self.release_path, lambda lock: lock["dependencies"][f"{self.framework}/win-x64"].update({"fixture.package": package}))
        with self.assertRaisesRegex(ValueError, "normal/release package version/hash mismatch"):
            LOCKS.validate(self.root)

    def test_project_version_ranges_may_change_without_package_hashes(self) -> None:
        for path, version in ((self.normal_path, "1.0.0"), (self.release_path, "0.3.0-preview.1")):
            entry = {"type": "Project", "dependencies": {"AssetLibrary.Windows.Core": f"[{version}, )"}}
            self.mutate(path, lambda lock: lock["dependencies"][self.framework].update({"assetlibrary.windows.session": entry}))
        self.assertEqual(LOCKS.validate(self.root), 1)

    def test_duplicate_json_and_case_variant_package_ids_fail(self) -> None:
        saved = self.release_path.read_text(encoding="utf-8")
        self.release_path.write_text('{"version":2,"version":2,"dependencies":{}}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON member"):
            LOCKS.validate(self.root)
        self.release_path.write_text(saved, encoding="utf-8")
        self.mutate(self.release_path, lambda lock: lock["dependencies"][self.framework].update({"fixture.package": {"type": "Transitive", "resolved": "1.2.3", "contentHash": HASH}}))
        with self.assertRaisesRegex(ValueError, "duplicate package ID"):
            LOCKS.validate(self.root)

    def test_cli_is_isolated_read_only_and_fails_closed(self) -> None:
        before = {path: path.read_bytes() for path in self.root.rglob("*.json")}
        command = [sys.executable, "-I", "-B", str(SCRIPT), "--root", str(self.root)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=10, check=False)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("WINDOWS_RELEASE_LOCKS_OK projects=6 rid=win-x64", result.stdout)
        self.assertEqual(before, {path: path.read_bytes() for path in self.root.rglob("*.json")})
        self.release_path.unlink()
        result = subprocess.run(command, capture_output=True, text=True, timeout=10, check=False)
        self.assertEqual(result.returncode, 1)
        self.assertIn("WINDOWS_RELEASE_LOCKS_INVALID", result.stdout)
        self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()
