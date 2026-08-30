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
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[2] / "packages" / "test-support"))

from file_safety import (  # noqa: E402
    CHUNK, Conflict, Failure, FileSafety, Policy, State, TaskLock,
    UnsupportedPrimitive, hash_file,
)
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

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)
        if self.external_source is not None:
            shutil.rmtree(self.external_source, ignore_errors=True)
        if self.external_target is not None:
            shutil.rmtree(self.external_target, ignore_errors=True)

    def use_actual_cross_devices(self):
        # The repository NAS mount (st_dev 147) and /tmp (st_dev 2050) are the
        # actual devices required by this Spike; no mock device classification.
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

    def test_protection_permission_space_and_cancel_are_fail_closed(self):
        self.write()
        cases = (Policy(protected_sources=frozenset({"a.bin"})),
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

    def test_ordinary_delete_reason_and_same_hash_restore(self):
        self.write("remove-me", b"delete-me")
        result = self.engine.delete("remove-me")
        trash = Path(result["source_trash"])
        self.assertEqual(result["state"], "complete")
        metadata = json.loads(Path(str(trash) + ".json").read_text())
        self.assertEqual(metadata["reason"], "delete")
        self.assertEqual(self.engine.restore(trash), "restored")
        self.assertEqual(self.engine.restore(trash), "already_restored")

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
        self.assertEqual((self.target / "item").read_bytes(), b"new-content")


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
        with self.assertRaises(Failure):
            first.reconcile_and_reclaim(False, now=99)
        first.reconcile_and_reclaim(True, now=99)
        first.release()

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
