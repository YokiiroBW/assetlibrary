from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import tracemalloc
import unittest
from unittest import mock
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[2] / "packages" / "test-support"))

from file_safety import (  # noqa: E402
    CHUNK, Conflict, Failure, FileSafety, Policy, State, TaskLock,
    UnsupportedPrimitive, hash_file,
)
import file_safety as file_safety_module  # noqa: E402
from performance import BYTES_100_GIB  # noqa: E402


class Fixture(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="m006-"))
        self.source = self.root / "source"
        self.target = self.root / "target"
        self.runtime = self.root / "runtime"
        self.source.mkdir(); self.target.mkdir()
        self.engine = FileSafety(self.source, self.target, self.runtime)
        self.external_source = None
        self.external_target = None
        self.external_dirs = []

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)
        if self.external_source is not None:
            shutil.rmtree(self.external_source, ignore_errors=True)
        if self.external_target is not None:
            shutil.rmtree(self.external_target, ignore_errors=True)
        for directory in self.external_dirs:
            shutil.rmtree(directory, ignore_errors=True)

    def use_actual_cross_devices(self):
        # The repository NAS mount (st_dev 147) and /tmp (st_dev 2050) are the
        # actual devices required by this Spike; no mock device classification.
        if self.external_source is not None:
            shutil.rmtree(self.external_source, ignore_errors=True)
        if self.external_target is not None:
            shutil.rmtree(self.external_target, ignore_errors=True)
        nas = HERE.parents[2] / ".runtime" / "sandbox-storage" / "M0-006" / f"test-{os.getpid()}"
        shutil.rmtree(nas, ignore_errors=True)
        nas.mkdir(parents=True)
        target = Path(tempfile.mkdtemp(prefix="m006-target-"))
        self.external_source, self.external_target = nas, target
        self.source, self.target = nas, target
        self.engine = FileSafety(self.source, self.target, self.runtime)

    def write(self, name="a.bin", data=b"alpha" * 1000):
        path = self.source / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path


class StateAndPaths(Fixture):
    def test_state_names_and_reject_escape(self):
        self.assertEqual(State.COMPLETE.value, "complete")
        self.write()
        for bad in ("/absolute", "../escape", "a/../../escape"):
            with self.subTest(bad=bad):
                with self.assertRaises(Failure):
                    self.engine.move(bad, "x")
        (self.source / "link").symlink_to(self.root)
        with self.assertRaises(Failure):
            self.engine.move("link/a.bin", "x")
        self.write("source-final", b"source")
        (self.source / "source-final-link").symlink_to(self.source / "source-final")
        with self.assertRaises(Failure):
            self.engine.move("source-final-link", "new")
        (self.target / "outside").mkdir()
        (self.target / "target-parent").symlink_to(self.target / "outside", target_is_directory=True)
        with self.assertRaises(Failure):
            self.engine.move("source-final", "target-parent/escaped")
        (self.target / "target-final").symlink_to(self.source / "source-final")
        with self.assertRaises(Failure):
            self.engine.move("source-final", "target-final")

    def test_cancel_durable_state_has_no_lock_or_stage(self):
        self.write("cancelled", b"cancel")
        result = self.engine.move("cancelled", "cancelled", op_id="cancel-op", policy=Policy(cancel=True))
        self.assertEqual(result["state"], "cancelled")
        self.assertFalse((self.runtime / "operations" / "cancel-op" / "lock").exists())
        self.assertFalse((self.target / ".m006-stage" / "cancel-op").exists())
        (self.target / "cancelled").write_bytes(b"unexpected")
        recovered = self.engine.recover("cancel-op", owner="worker-1")
        self.assertEqual(recovered["state"], "manual_attention")
        self.assertTrue((self.source / "cancelled").exists())

    def test_cancelled_replace_keeps_existing_target_and_never_moves(self):
        self.write("cancel-replace", b"incoming")
        (self.target / "cancel-replace").write_bytes(b"old-target")
        result = self.engine.move("cancel-replace", "cancel-replace", op_id="cancel-replace",
                                  replace=True, policy=Policy(cancel=True))
        self.assertEqual(result["state"], State.CANCELLED.value)
        recovered = self.engine.recover("cancel-replace", owner="worker-1")
        self.assertEqual(recovered["state"], State.CANCELLED.value)
        self.assertEqual((self.source / "cancel-replace").read_bytes(), b"incoming")
        self.assertEqual((self.target / "cancel-replace").read_bytes(), b"old-target")
        self.assertFalse((self.target / ".m006-stage" / "cancel-replace").exists())
        self.assertFalse((self.runtime / "operations" / "cancel-replace" / "lock").exists())

    def test_same_device_noreplace_and_collision(self):
        same = FileSafety(self.source, self.source, self.runtime)
        self.write("a.bin", b"same-device")
        result = same.move("a.bin", "b.bin")
        self.assertEqual(result["state"], State.COMPLETE.value)
        self.assertEqual((self.source / "b.bin").read_bytes(), b"same-device")
        self.write("c.bin", b"new")
        (self.source / "d.bin").write_bytes(b"old")
        with self.assertRaises(Conflict):
            same.move("c.bin", "d.bin")
        self.assertTrue((self.source / "c.bin").exists())

    def test_rename_pins_every_ancestor_against_symlink_swap(self):
        base = self.root / "pinned"
        source_parent = base / "source"
        target_parent = base / "target" / "nested" / "deep"
        source_parent.mkdir(parents=True)
        target_parent.mkdir(parents=True)
        external = Path(tempfile.mkdtemp(prefix="m006-external-"))
        self.external_dirs.append(external)
        (external / "dest").write_bytes(b"must-not-touch")
        source = source_parent / "source"
        source.write_bytes(b"payload")
        nested = base / "target" / "nested"
        moved_nested = external / "nested-relocated"
        original_open = os.open
        swapped = False

        def swap_before_deep(path, flags, mode=0o777, *, dir_fd=None):
            nonlocal swapped
            if path == "deep" and dir_fd is not None and not swapped:
                nested.rename(moved_nested)
                nested.symlink_to(external, target_is_directory=True)
                swapped = True
            return original_open(path, flags, mode, dir_fd=dir_fd)

        try:
            with mock.patch.object(file_safety_module.os, "open", side_effect=swap_before_deep):
                with self.assertRaises(Failure):
                    file_safety_module._rename_noreplace(
                        source, target_parent / "dest", source_root=base / "source",
                        target_root=base / "target")
            self.assertTrue(swapped)
            self.assertEqual((external / "dest").read_bytes(), b"must-not-touch")
            self.assertFalse((moved_nested / "deep" / "dest").exists())
            self.assertEqual(source.read_bytes(), b"payload")
        finally:
            if nested.is_symlink():
                nested.unlink()
            if moved_nested.exists():
                moved_nested.rename(nested)

    def test_protection_permission_space_and_cancel_are_fail_closed(self):
        self.write()
        cases = (Policy(protected_sources=frozenset({"a.bin"})),
                 Policy(protected_targets=frozenset({"target.bin"})),
                 Policy(permission_denied=True), Policy(insufficient_space=True), Policy(cancel=True))
        for policy in cases:
            with self.subTest(policy=policy):
                if policy.cancel:
                    result = self.engine.move("a.bin", "target.bin", policy=policy)
                    self.assertEqual(result["state"], "cancelled")
                else:
                    with self.assertRaises((Failure, PermissionError, OSError)):
                        self.engine.move("a.bin", "target.bin", policy=policy)
                self.assertTrue((self.source / "a.bin").exists())
        self.assertFalse((self.target / "target.bin").exists())

    def test_explicit_replacement_metadata_and_restore(self):
        self.write("incoming", b"new")
        (self.target / "item").write_bytes(b"old")
        result = self.engine.move("incoming", "item", replace=True)
        self.assertEqual(result["state"], "complete")
        self.assertEqual((self.target / "item").read_bytes(), b"new")
        replacement = Path(result["replacement_trash"])
        metadata = json.loads(Path(str(replacement) + ".json").read_text())
        self.assertEqual(metadata["reason"], "replacement")
        self.assertEqual(metadata["size"], 3)
        self.assertEqual(hash_file(replacement)[0], metadata["sha256"])
        self.assertEqual(self.engine.restore(replacement, target="restored-old"), "restored")
        self.assertEqual((self.target / "restored-old").read_bytes(), b"old")

    def test_replace_target_appearing_after_preflight_is_not_trashed(self):
        self.write("appears", b"incoming")

        def appear(name, extra):
            if name == "after_preflight_journal":
                (self.target / "appears").write_bytes(b"concurrent")

        self.engine.hooks = appear
        with self.assertRaises(Conflict):
            self.engine.move("appears", "appears", op_id="replace-appears", replace=True)
        self.assertTrue((self.source / "appears").exists())
        self.assertEqual((self.target / "appears").read_bytes(), b"concurrent")
        self.assertFalse((self.target / ".m006-trash" / "replace-appears").exists())

    def test_replace_target_identity_change_is_not_trashed(self):
        self.write("changes", b"incoming")
        (self.target / "changes").write_bytes(b"old")

        def change(name, extra):
            if name == "after_preflight_journal":
                (self.target / "changes").write_bytes(b"changed")

        self.engine.hooks = change
        with self.assertRaises(Conflict):
            self.engine.move("changes", "changes", op_id="replace-changes", replace=True)
        self.assertTrue((self.source / "changes").exists())
        self.assertEqual((self.target / "changes").read_bytes(), b"changed")
        self.assertFalse((self.target / ".m006-trash" / "replace-changes").exists())

    def test_ordinary_delete_reason_and_same_hash_restore(self):
        self.write("remove-me", b"delete-me")
        result = self.engine.delete("remove-me")
        trash = Path(result["source_trash"])
        self.assertEqual(result["state"], "complete")
        metadata = json.loads(Path(str(trash) + ".json").read_text())
        self.assertEqual(metadata["reason"], "delete")
        self.assertEqual(self.engine.restore(trash), "restored")
        self.assertEqual(self.engine.restore(trash), "already_restored")

    def test_inspect_derives_physical_trash_and_rejects_tampered_external_path(self):
        self.use_actual_cross_devices()
        self.write("observed", b"observe")
        op = "observe-op"
        self.engine.move("observed", "observed", op_id=op)
        observation = self.engine.inspect(op)
        self.assertTrue(observation["source_trash"]["exists"])
        self.assertTrue(observation["source_trash"]["metadata_exists"])
        journal_path = self.runtime / "operations" / op / "journal.json"
        journal = json.loads(journal_path.read_text())
        outside = Path(tempfile.mkdtemp(prefix="m006-external-")) / "victim"
        self.external_dirs.append(outside.parent)
        outside.write_bytes(b"must-not-touch")
        journal["target"] = str(outside)
        journal_path.write_text(json.dumps(journal))
        with self.assertRaises(Failure):
            self.engine.inspect(op)
        self.assertEqual(outside.read_bytes(), b"must-not-touch")
        shutil.rmtree(outside.parent)

    def test_operation_id_and_trash_metadata_symlink_fail_closed(self):
        self.write("safe", b"safe")
        for op in ("../escape", "/absolute"):
            with self.assertRaises(Failure):
                self.engine.move("safe", "safe", op_id=op)
        result = self.engine.delete("safe", op_id="meta-link")
        trash = Path(result["source_trash"])
        metadata = Path(str(trash) + ".json")
        metadata.unlink()
        metadata.symlink_to(self.root / "external-meta-target")
        with self.assertRaises(Failure):
            self.engine.restore(trash)
        metadata.unlink()
        forged = self.root / "m006-external-forged"
        forged.mkdir()
        payload = forged / "payload"
        payload.write_bytes(b"forged")
        (forged / "payload.json").write_text(json.dumps({"schema_version": "m0-006.trash.v1"}))
        with self.assertRaises(Failure):
            self.engine.restore(payload)

    def test_precommit_cleanup_ignores_tampered_external_stage(self):
        self.use_actual_cross_devices()
        self.write("stage-cleanup", b"stage-cleanup")
        external = Path(tempfile.mkdtemp(prefix="m006-external-"))
        self.external_dirs.append(external)
        victim = external / "victim"
        victim.write_bytes(b"must-survive")

        def tamper(name, extra):
            if name == "after_verified_journal":
                journal_path = self.runtime / "operations" / "stage-cleanup" / "journal.json"
                value = json.loads(journal_path.read_text())
                value["stage"] = str(victim)
                journal_path.write_text(json.dumps(value))
                raise Failure("injected post-verification failure")

        self.engine.hooks = tamper
        with self.assertRaises(Failure):
            self.engine.move("stage-cleanup", "stage-cleanup", op_id="stage-cleanup")
        self.assertEqual(victim.read_bytes(), b"must-survive")
        self.assertFalse((self.target / ".m006-stage" / "stage-cleanup" / "payload").exists())
        self.assertTrue((self.source / "stage-cleanup").exists())

    def test_restore_rejects_root_role_or_relative_path_mismatch(self):
        self.write("restore-check", b"restore")
        result = self.engine.delete("restore-check", op_id="restore-check")
        trash = Path(result["source_trash"])
        metadata = Path(str(trash) + ".json")
        record = json.loads(metadata.read_text())
        record["root_role"] = "replacement"
        metadata.write_text(json.dumps(record))
        with self.assertRaises(Failure):
            self.engine.restore(trash)

    def test_unknown_journal_state_is_rejected(self):
        self.write("unknown", b"state")
        result = self.engine.move("unknown", "unknown", op_id="unknown-op")
        path = self.runtime / "operations" / "unknown-op" / "journal.json"
        value = json.loads(path.read_text()); value["state"] = "future_state"; path.write_text(json.dumps(value))
        with self.assertRaises(Failure):
            self.engine.inspect("unknown-op")

    def test_hash_streaming_is_bounded_and_64bit_logical_boundary(self):
        payload = (b"abcdefgh" * (2 * 1024 * 1024)) + b"tail"
        expected_size = len(payload)
        self.write("large.bin", payload)
        del payload
        tracemalloc.start()
        digest, size = hash_file(self.source / "large.bin")
        _, peak = tracemalloc.get_traced_memory(); tracemalloc.stop()
        self.assertEqual(size, expected_size); self.assertEqual(len(digest), 64)
        self.assertLess(peak, 8 * CHUNK)
        self.assertGreater(BYTES_100_GIB, 2**32)
        self.assertEqual(BYTES_100_GIB, 100 * 1024**3)

    def test_16mib_cross_move_records_bounded_measurement(self):
        self.use_actual_cross_devices()
        self.write("16m.bin", (b"0123456789abcdef" * (1024 * 1024)))
        tracemalloc.start(); started = time.monotonic()
        result = self.engine.move("16m.bin", "16m.bin")
        elapsed = time.monotonic() - started
        _, peak = tracemalloc.get_traced_memory(); tracemalloc.stop()
        self.assertEqual(result["state"], "complete")
        self.assertLess(peak, 12 * CHUNK)
        self.assertEqual(hash_file(self.target / "16m.bin")[1], 16 * 1024 * 1024)
        self.engine.last_evidence.update(actual_bytes=16 * 1024 * 1024,
                                         elapsed_seconds=elapsed, peak_tracemalloc_bytes=peak)
        print(f"M0-006 evidence actual_bytes=16777216 elapsed_seconds={elapsed:.6f} "
              f"peak_tracemalloc_bytes={peak}")


class CrossDeviceAndRecovery(Fixture):
    def test_cross_device_actual_stage_commit_source_trash(self):
        self.use_actual_cross_devices()
        self.assertNotEqual(self.source.stat().st_dev, self.target.stat().st_dev)
        self.write("asset.bin", b"cross-device" * 10000)
        result = self.engine.move("asset.bin", "nested/asset.bin")
        self.assertEqual(result["state"], "complete")
        self.assertFalse((self.source / "asset.bin").exists())
        self.assertTrue(Path(result["source_trash"]).exists())
        self.assertEqual(hash_file(self.target / "nested/asset.bin")[1], len(b"cross-device" * 10000))

    def test_corrupt_target_is_conflict_and_source_stays(self):
        self.use_actual_cross_devices()
        self.write("a", b"original")
        op_id = "corrupt-target"
        # Materialize a normal operation but stop before target verification.
        def crash(name, extra):
            if name == "after_target_physical_commit":
                (self.target / "a").write_bytes(b"corrupted")
        self.engine.hooks = crash
        with self.assertRaises(Failure):
            self.engine.move("a", "a", op_id=op_id)
        # A replacement target with differing bytes always remains explicit.
        self.assertTrue((self.source / "a").exists())
        state = json.loads((self.runtime / "operations" / op_id / "journal.json").read_text())["state"]
        self.assertIn(state, ("conflict", "manual_attention"))

    def test_target_change_after_source_trash_never_completes(self):
        self.use_actual_cross_devices()
        self.write("post-trash", b"incoming")

        def change(name, extra):
            if name == "after_source_trashed_journal":
                (self.target / "post-trash").write_bytes(b"changed-after-trash")

        self.engine.hooks = change
        with self.assertRaises(Failure):
            self.engine.move("post-trash", "post-trash", op_id="post-trash", replace=False)
        journal = json.loads((self.runtime / "operations" / "post-trash" / "journal.json").read_text())
        self.assertEqual(journal["state"], State.CONFLICT.value)
        self.assertTrue((self.source / ".m006-trash" / "post-trash" / "source-post-trash").exists())
        self.assertNotEqual((self.target / "post-trash").read_bytes(), b"incoming")

    def test_recovery_target_change_after_source_trash_never_completes(self):
        self.use_actual_cross_devices()
        self.write("recover-post-trash", b"incoming")
        script = """import sys,os
sys.path.insert(0,sys.argv[1]); from pathlib import Path; from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]))
def h(name, extra):
 if name == 'after_target_physical_commit': os._exit(77)
e.hooks=h
try: e.move('recover-post-trash','recover-post-trash',op_id='recover-post-trash')
except Exception: os._exit(78)
"""
        child = subprocess.run([sys.executable, "-c", script, str(HERE), str(self.source),
                                str(self.target), str(self.runtime)], check=False)
        self.assertEqual(child.returncode, 77)

        def change(name, extra):
            if name == "after_source_trashed_journal":
                (self.target / "recover-post-trash").write_bytes(b"changed-after-trash")

        self.engine.hooks = change
        result = self.engine.recover("recover-post-trash", owner="worker-1")
        self.assertEqual(result["state"], State.CONFLICT.value)
        self.assertTrue((self.source / ".m006-trash" / "recover-post-trash" / "source-recover-post-trash").exists())
        self.assertFalse((self.runtime / "operations" / "recover-post-trash" / "lock").exists())

    def test_mutation_during_copy_fails_closed(self):
        self.use_actual_cross_devices()
        self.write("changing", b"x" * (3 * CHUNK))
        changed = False
        def mutate(path):
            nonlocal changed
            if not changed:
                with path.open("ab") as stream: stream.write(b"mutation")
                changed = True
        with self.assertRaises(Failure):
            self.engine.move("changing", "changing", mutation=mutate)
        self.assertTrue((self.source / "changing").exists())
        self.assertFalse((self.target / "changing").exists())

    def test_source_mutation_after_target_commit_stays_out_of_trash(self):
        self.use_actual_cross_devices()
        self.write("late-change", b"stable")
        def late_mutation(name, extra):
            if name == "after_target_committed_journal":
                (self.source / "late-change").write_bytes(b"changed-after-commit")
        self.engine.hooks = late_mutation
        with self.assertRaises(Failure):
            self.engine.move("late-change", "late-change", op_id="late-mutation")
        journal = json.loads((self.runtime / "operations" / "late-mutation" / "journal.json").read_text())
        self.assertEqual(journal["state"], "conflict")
        self.assertTrue((self.source / "late-change").exists())
        self.assertFalse((self.source / ".m006-trash" / "late-mutation" / "source-late-change").exists())

    def test_recovery_rehashes_source_before_trash(self):
        self.use_actual_cross_devices()
        self.write("recover-late", b"stable")
        script = """import sys,os
sys.path.insert(0,sys.argv[1]); from pathlib import Path; from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]))
def h(name, extra):
 if name == 'after_target_physical_commit': os._exit(77)
e.hooks=h
try: e.move('recover-late','recover-late',op_id='recover-late')
except Exception: os._exit(78)
"""
        child = subprocess.run([sys.executable, "-c", script, str(HERE), str(self.source),
                                str(self.target), str(self.runtime)], check=False)
        self.assertEqual(child.returncode, 77)
        (self.source / "recover-late").write_bytes(b"changed-after-crash")
        result = self.engine.recover("recover-late", owner="worker-1")
        self.assertIn(result["state"], (State.CONFLICT.value, State.MANUAL.value))
        self.assertTrue((self.source / "recover-late").exists())
        self.assertFalse((self.source / ".m006-trash" / "recover-late" / "source-recover-late").exists())

    def test_target_parent_symlink_after_verified_is_not_followed(self):
        self.use_actual_cross_devices()
        self.write("escape-check", b"source")
        external = Path(tempfile.mkdtemp(prefix="m006-external-"))
        self.external_dirs.append(external)
        (external / "escape-check").write_bytes(b"must-stay")
        def swap_parent(name, extra):
            if name == "after_verified_journal":
                shutil.rmtree(self.target / "nested")
                (self.target / "nested").symlink_to(external, target_is_directory=True)
        self.engine.hooks = swap_parent
        with self.assertRaises(Failure):
            self.engine.move("escape-check", "nested/escape-check", op_id="parent-swap")
        self.assertEqual((external / "escape-check").read_bytes(), b"must-stay")
        self.assertTrue((self.source / "escape-check").exists())

    def test_internal_stage_and_trash_symlink_fail_closed(self):
        self.use_actual_cross_devices()
        shutil.rmtree(self.target / ".m006-trash")
        (self.target / ".m006-trash-outside").mkdir()
        (self.target / ".m006-trash").symlink_to(self.target / ".m006-trash-outside", target_is_directory=True)
        self.write("trash-link", b"trash")
        (self.target / "trash-link").write_bytes(b"old")
        with self.assertRaises(Failure):
            self.engine.move("trash-link", "trash-link", op_id="trash-link", replace=True)
        self.assertTrue((self.source / "trash-link").exists())
        self.use_actual_cross_devices()
        (self.target / ".m006-stage-outside").mkdir()
        (self.target / ".m006-stage").symlink_to(self.target / ".m006-stage-outside", target_is_directory=True)
        self.write("stage-link", b"stage")
        with self.assertRaises(Failure):
            self.engine.move("stage-link", "stage-link", op_id="stage-link")
        self.assertTrue((self.source / "stage-link").exists())

    def test_delete_physical_gap_recovers_from_durable_delete_journal(self):
        self.use_actual_cross_devices()
        self.write("delete-crash", b"delete-crash")
        script = """import sys,os;sys.path.insert(0,sys.argv[1]);from pathlib import Path;from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]))
def h(name,extra):
 if name=='after_source_physical_trash':os._exit(77)
e.hooks=h
try:e.delete('delete-crash',op_id='delete-crash')
except Exception:os._exit(78)
"""
        child = subprocess.run([sys.executable,"-c",script,str(HERE),str(self.source),str(self.target),str(self.runtime)],capture_output=True)
        self.assertEqual(child.returncode, 77)
        recover = """import sys;sys.path.insert(0,sys.argv[1]);from pathlib import Path;from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]));print(e.recover('delete-crash',owner='worker-1')['state'])
"""
        for _ in range(2):
            result = subprocess.run([sys.executable,"-c",recover,str(HERE),str(self.source),str(self.target),str(self.runtime)],capture_output=True,text=True,check=False)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(result.stdout.strip(), "complete")
        trash = self.source / ".m006-trash" / "delete-crash" / "source-delete-crash"
        self.assertTrue(trash.exists())
        self.assertTrue(Path(str(trash) + ".json").exists())
        self.assertFalse((self.source / "delete-crash").exists())
        self.assertFalse((self.runtime / "operations" / "delete-crash" / "lock").exists())

    def test_foreign_recovery_cannot_reclaim_active_slow_copy(self):
        self.use_actual_cross_devices()
        self.write("slow-copy", b"s" * (16 * CHUNK))
        marker = self.runtime / "slow-copy.marker"
        script = """import sys,time
from pathlib import Path
sys.path.insert(0,sys.argv[1]); from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4])); seen=False
def mutate(path):
 global seen
 if not seen:
  seen=True; Path(sys.argv[5]).write_text('copy-active')
 time.sleep(0.03)
e.move('slow-copy','slow-copy',op_id='slow-copy',owner='active',lock_ttl=0.10,mutation=mutate)
        """
        child = subprocess.Popen([sys.executable, "-c", script, str(HERE), str(self.source),
                                  str(self.target), str(self.runtime), str(marker)])
        try:
            deadline = time.monotonic() + 5
            while not marker.exists() and time.monotonic() < deadline:
                time.sleep(0.01)
            self.assertTrue(marker.exists())
            time.sleep(0.20)
            with self.assertRaises(Failure):
                self.engine.recover("slow-copy", owner="foreign")
            self.assertEqual(child.wait(timeout=10), 0)
        finally:
            if child.poll() is None:
                child.terminate()
                try:
                    child.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait(timeout=5)
        self.assertFalse((self.runtime / "operations" / "slow-copy" / "lock").exists())

    def test_replacement_physical_gap_recovers_old_trash_and_new_target(self):
        self.use_actual_cross_devices()
        self.write("incoming", b"new-content")
        (self.target / "item").write_bytes(b"old-content")
        op = "replacement-gap"
        script = """import sys,os;sys.path.insert(0,sys.argv[1]);from pathlib import Path;from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]))
def h(name,extra):
 if name=='after_replacement_physical_trash': os._exit(77)
e.hooks=h
try:e.move('incoming','item',op_id='replacement-gap',replace=True)
except Exception:os._exit(78)
"""
        child = subprocess.run([sys.executable,"-c",script,str(HERE),str(self.source),str(self.target),str(self.runtime)], capture_output=True)
        self.assertEqual(child.returncode, 77)
        recover = """import sys;sys.path.insert(0,sys.argv[1]);from pathlib import Path;from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]));print(e.recover('replacement-gap',owner='worker-1')['state'])
"""
        for _ in range(2):
            result = subprocess.run([sys.executable,"-c",recover,str(HERE),str(self.source),str(self.target),str(self.runtime)],capture_output=True,text=True,check=True)
            self.assertEqual(result.stdout.strip(), "complete")
        old = self.target / ".m006-trash" / op / "replacement-item.json"
        self.assertTrue(old.exists())
        old_payload = old.with_suffix("")
        old_metadata = json.loads(old.read_text())
        self.assertEqual(hash_file(old_payload), (old_metadata["sha256"], old_metadata["size"]))
        source_trash = self.source / ".m006-trash" / op / "source-incoming"
        self.assertEqual(hash_file(source_trash), (hashlib.sha256(b"new-content").hexdigest(), len(b"new-content")))
        self.assertFalse((self.target / ".m006-stage" / op / "payload").exists())
        self.assertFalse((self.runtime / "operations" / op / "lock").exists())
        self.assertEqual((self.target / "item").read_bytes(), b"new-content")

    def test_same_device_replacement_metadata_only_gap(self):
        self.write("incoming-same", b"new-same")
        (self.source / "item-same").write_bytes(b"old-same")
        script = """import sys,os;sys.path.insert(0,sys.argv[1]);from pathlib import Path;from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[2]),Path(sys.argv[3]))
def h(name,extra):
 if name=='before_replacement_physical_trash':os._exit(77)
e.hooks=h
try:e.move('incoming-same','item-same',op_id='same-replacement-gap',replace=True)
except Exception:os._exit(78)
"""
        child = subprocess.run([sys.executable,"-c",script,str(HERE),str(self.source),str(self.runtime)], capture_output=True)
        self.assertEqual(child.returncode, 77)
        recover = """import sys;sys.path.insert(0,sys.argv[1]);from pathlib import Path;from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[2]),Path(sys.argv[3]));print(e.recover('same-replacement-gap',owner='worker-1')['state'])
"""
        for _ in range(2):
            result = subprocess.run([sys.executable,"-c",recover,str(HERE),str(self.source),str(self.runtime)],capture_output=True,text=True,check=True)
            self.assertEqual(result.stdout.strip(), "complete")
        old = self.source / ".m006-trash" / "same-replacement-gap" / "replacement-item-same"
        self.assertEqual(hash_file(old), (hashlib.sha256(b"old-same").hexdigest(), len(b"old-same")))
        self.assertEqual((self.source / "item-same").read_bytes(), b"new-same")
        self.assertFalse((self.source / "incoming-same").exists())
        self.assertFalse((self.runtime / "operations" / "same-replacement-gap" / "lock").exists())
        self.assertFalse((self.source / ".m006-stage" / "same-replacement-gap").exists())

    def test_replacement_metadata_only_gap_verifies_old_target(self):
        self.use_actual_cross_devices()
        self.write("incoming", b"incoming")
        (self.target / "item").write_bytes(b"old-target")
        script = """import sys,os;sys.path.insert(0,sys.argv[1]);from pathlib import Path;from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]))
def h(name,extra):
 if name=='before_replacement_physical_trash':os._exit(77)
e.hooks=h
try:e.move('incoming','item',op_id='replacement-metadata-only',replace=True)
except Exception:os._exit(78)
"""
        child = subprocess.run([sys.executable,"-c",script,str(HERE),str(self.source),str(self.target),str(self.runtime)], capture_output=True)
        self.assertEqual(child.returncode, 77)
        recover = """import sys;sys.path.insert(0,sys.argv[1]);from pathlib import Path;from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]));print(e.recover('replacement-metadata-only',owner='worker-1')['state'])
"""
        result = subprocess.run([sys.executable,"-c",recover,str(HERE),str(self.source),str(self.target),str(self.runtime)],capture_output=True,text=True,check=True)
        self.assertEqual(result.stdout.strip(), "complete")
        old = self.target / ".m006-trash" / "replacement-metadata-only" / "replacement-item"
        self.assertEqual(hash_file(old), (hashlib.sha256(b"old-target").hexdigest(), len(b"old-target")))
        self.assertTrue(Path(str(old) + ".json").exists())

    def test_invalid_replacement_metadata_causes_no_physical_write(self):
        self.use_actual_cross_devices()
        self.write("invalid-meta", b"incoming")
        (self.target / "invalid-item").write_bytes(b"old-target")
        script = """import sys,os
sys.path.insert(0,sys.argv[1]); from pathlib import Path; from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]))
def h(name,extra):
 if name=='before_replacement_physical_trash':os._exit(77)
e.hooks=h
try:e.move('invalid-meta','invalid-item',op_id='invalid-meta',replace=True)
except Exception:os._exit(78)
"""
        child = subprocess.run([sys.executable, "-c", script, str(HERE), str(self.source),
                                str(self.target), str(self.runtime)], check=False)
        self.assertEqual(child.returncode, 77)
        metadata = self.target / ".m006-trash" / "invalid-meta" / "replacement-invalid-item.json"
        record = json.loads(metadata.read_text())
        record["reason"] = "forged"
        metadata.write_text(json.dumps(record))
        result = self.engine.recover("invalid-meta", owner="worker-1")
        self.assertIn(result["state"], (State.CONFLICT.value, State.MANUAL.value))
        self.assertEqual((self.target / "invalid-item").read_bytes(), b"old-target")
        self.assertTrue((self.source / "invalid-meta").exists())
        self.assertFalse((self.target / ".m006-trash" / "invalid-meta" / "replacement-invalid-item").exists())


class LockTests(Fixture):
    def test_owner_heartbeat_expiry_reconcile_and_release(self):
        path = self.runtime / "lock"
        first = TaskLock(path, "op", "one", ttl=2)
        self.assertEqual(first.claim(now=10), "claimed")
        with self.assertRaises(Failure):
            TaskLock(path, "op", "two").claim(now=11)
        first.heartbeat(now=11)
        with self.assertRaises(Failure):
            first.claim(now=12)
        with self.assertRaises(TypeError):
            first.reconcile_and_reclaim(False, now=99)
        with self.assertRaises(Failure):
            first.reconcile_and_reclaim(lambda op: {"operation_id": "other", "physical_reconciled": True}, now=99)
        def observed(op):
            return {"operation_id": op, "physical_reconciled": True, "source": {}, "target": {},
                    "stage": {}, "source_trash": {}, "replacement_trash": {}}
        first.reconcile_and_reclaim(observed, now=99)
        first.release()

    def test_old_owner_cannot_update_or_release_reclaimed_generation(self):
        path = self.runtime / "generation.lock"
        first = TaskLock(path, "generation-op", "owner-a", ttl=2)
        first.claim(now=10)
        second = TaskLock(path, "generation-op", "owner-b", ttl=2)

        def observed(op):
            return {"operation_id": op, "physical_reconciled": True, "source": {}, "target": {},
                    "stage": {}, "source_trash": {}, "replacement_trash": {}}

        second.reconcile_and_reclaim(observed, now=99)
        before = json.loads(path.read_text())
        with self.assertRaises(Failure):
            first.heartbeat(now=100)
        with self.assertRaises(Failure):
            first.release()
        self.assertEqual(json.loads(path.read_text()), before)
        second.release()

    def test_concurrent_o_excl_claim_exactly_one_winner(self):
        path = self.runtime / "concurrent.lock"
        script = """import sys; sys.path.insert(0, sys.argv[1]); from file_safety import TaskLock
try: print(TaskLock(__import__('pathlib').Path(sys.argv[2]),'op',sys.argv[3]).claim(), flush=True)
except Exception as e: print(type(e).__name__, flush=True)
"""
        procs = [subprocess.Popen([sys.executable, "-c", script, str(HERE), str(path), str(i)], stdout=subprocess.PIPE, text=True)
                 for i in (1, 2)]
        outcomes = [p.communicate(timeout=10)[0].strip() for p in procs]
        self.assertEqual(outcomes.count("claimed"), 1)
        self.assertEqual(sum("Failure" in item for item in outcomes), 1)

    def test_engine_recovery_reclaims_expired_lock_only_after_inspect(self):
        self.write("recover-lock", b"lock")
        op = "expired-recover"
        # Create a durable preflight journal without completing the move.
        source = self.source / "recover-lock"; target = self.target / "recover-lock"
        ident = __import__("file_safety").identity(source)
        journal = self.engine._new(op, "recover-lock", "recover-lock", source, target, ident, False, "old")
        stale = TaskLock(self.runtime / "operations" / op / "lock", op, "old", ttl=-1)
        stale.claim(now=10)
        result = self.engine.recover(op, owner="new-owner")
        self.assertEqual(result["state"], "complete")


class CrashMatrix(Fixture):
    def _crash_subprocess(self, hook: str, cross: bool):
        source = self.source; target = self.target
        if cross and source.stat().st_dev == target.stat().st_dev:
            target = Path(tempfile.mkdtemp(prefix="m006-target-"))
            self.external_target = target
        self.write("crash.bin", b"crash-matrix" * 4096)
        op_id = "crash-" + hook.replace("_", "-")
        script = """import sys, os
sys.path.insert(0, sys.argv[1]); from pathlib import Path; from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4]))
def h(name, extra):
 if name == sys.argv[6]: os._exit(77)
e.hooks=h
try: e.move('crash.bin',sys.argv[7],op_id=sys.argv[5])
except Exception: os._exit(78)
"""
        target_rel = f"nested/{op_id}.bin"
        crashed = subprocess.run([sys.executable, "-c", script, str(HERE), str(source), str(target), str(self.runtime), op_id, hook, target_rel], capture_output=True, text=True, check=False)
        self.assertEqual(crashed.returncode, 77, crashed.stderr if crashed.stderr else "crash hook was not reached")
        # New process, then a second new process: recovery is idempotent.
        recover = """import sys; sys.path.insert(0,sys.argv[1]); from pathlib import Path; from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[3]),Path(sys.argv[4])); print(e.recover(sys.argv[5],owner='worker-1')['state'])
"""
        for _ in range(2):
            completed = subprocess.run([sys.executable, "-c", recover, str(HERE), str(source), str(target), str(self.runtime), op_id], text=True, capture_output=True, check=False)
            self.assertEqual(completed.returncode, 0, completed.stderr)
            self.assertEqual(completed.stdout.strip(), "complete")
        self.assertFalse(source.joinpath("crash.bin").exists())
        self.assertTrue(source.joinpath(".m006-trash", op_id, "source-crash.bin").exists())
        self.assertTrue(source.joinpath(".m006-trash", op_id, "source-crash.bin.json").exists())
        self.assertFalse(target.joinpath(".m006-stage", op_id, "payload").exists())
        self.assertFalse(self.runtime.joinpath("operations", op_id, "lock").exists())
        self.assertEqual(hash_file(target / target_rel)[1], len(b"crash-matrix" * 4096))

    def test_cross_device_durable_and_physical_gaps(self):
        self.use_actual_cross_devices()
        self.assertNotEqual(self.source.stat().st_dev, self.target.stat().st_dev)
        for hook in ("after_preflight_journal", "after_stage_physical", "after_staged_journal",
                     "after_verified_journal", "after_target_physical_commit",
                     "after_target_committed_journal", "after_source_physical_trash",
                     "before_source_physical_trash",
                     "after_source_trashed_journal", "after_complete_journal"):
            with self.subTest(hook=hook): self._crash_subprocess(hook, True)

    def test_same_device_commit_gap_when_supported(self):
        same = FileSafety(self.source, self.source, self.runtime)
        self.write("crash.bin", b"same-device-crash")
        op_id = "same-device-crash"
        script = """import sys, os; sys.path.insert(0,sys.argv[1]); from pathlib import Path; from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[2]),Path(sys.argv[3]))
def h(name, extra):
 if name == 'after_target_physical_commit': os._exit(77)
e.hooks=h; e.move('crash.bin','dest.bin',op_id='same-device-crash')
"""
        child = subprocess.run([sys.executable, "-c", script, str(HERE), str(self.source), str(self.runtime)], check=False)
        self.assertEqual(child.returncode, 77)
        recover = """import sys; sys.path.insert(0,sys.argv[1]); from pathlib import Path; from file_safety import FileSafety
e=FileSafety(Path(sys.argv[2]),Path(sys.argv[2]),Path(sys.argv[3])); print(e.recover('same-device-crash',owner='worker-1')['state'])
"""
        for _ in range(2):
            result = subprocess.run([sys.executable, "-c", recover, str(HERE), str(self.source), str(self.runtime)], capture_output=True, text=True, check=True)
            self.assertEqual(result.stdout.strip(), "complete")
        self.assertFalse((self.source / "crash.bin").exists())
        self.assertFalse((self.runtime / "operations" / op_id / "lock").exists())
        self.assertEqual((self.source / "dest.bin").read_bytes(), b"same-device-crash")


if __name__ == "__main__":
    unittest.main()
