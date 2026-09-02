from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from windows_file_safety_probe import (  # noqa: E402
    SANDBOX_BASE,
    ProbeFailure,
    create_staged_operation,
    directory_flush_probe,
    hash_file,
    rename_noreplace,
    validate_probe_root,
    volume_capabilities,
    write_file_durable,
)


@unittest.skipUnless(os.name == "nt", "Windows candidate probe")
class WindowsFileSafetyProbe(unittest.TestCase):
    def setUp(self):
        SANDBOX_BASE.mkdir(parents=True, exist_ok=True)
        self.root = Path(tempfile.mkdtemp(prefix="windows-probe-", dir=SANDBOX_BASE))
        self.probe = HERE / "windows_file_safety_probe.py"
        self.python = Path(sys.executable)
        validate_probe_root(self.root)

    def tearDown(self):
        validated = validate_probe_root(self.root)
        shutil.rmtree(validated)

    def run_probe(self, *arguments: str, expected_returncode: int = 0) -> subprocess.CompletedProcess[str]:
        result = subprocess.run(
            [str(self.python), str(self.probe), *arguments],
            text=True,
            capture_output=True,
            check=False,
            env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"},
        )
        self.assertEqual(result.returncode, expected_returncode, result.stderr)
        return result

    def test_root_is_fixed_local_ntfs_and_escape_is_rejected(self):
        capabilities = volume_capabilities(self.root)
        self.assertEqual(capabilities["filesystem"].upper(), "NTFS")
        self.assertTrue(capabilities["is_fixed"])
        with tempfile.TemporaryDirectory(prefix="m006-outside-") as outside:
            with self.assertRaises(ProbeFailure):
                validate_probe_root(Path(outside))

    def test_single_no_replace_primitive_preserves_collision(self):
        source = self.root / "collision" / "source.bin"
        target = self.root / "collision" / "target.bin"
        incoming = b"incoming-content"
        original = b"original-content"
        write_file_durable(source, incoming)
        write_file_durable(target, original)
        with self.assertRaises(FileExistsError) as raised:
            rename_noreplace(self.root, source, target)
        self.assertIn(raised.exception.errno, (80, 183))
        self.assertEqual(hash_file(source), (hashlib.sha256(incoming).hexdigest(), len(incoming)))
        self.assertEqual(hash_file(target), (hashlib.sha256(original).hexdigest(), len(original)))
        print(
            "M0-006 Windows collision evidence "
            f"primitive=SetFileInformationByHandle(FileRenameInfo) "
            f"replace_if_exists=false winerror={raised.exception.errno} preserved=true"
        )

    def test_two_process_race_has_exactly_one_winner(self):
        rounds = 50
        contenders = 0
        successes = 0
        collisions = 0
        for round_number in range(rounds):
            race = self.root / "races" / f"round-{round_number:03d}"
            source_a = race / "a.bin"
            source_b = race / "b.bin"
            target = race / "winner.bin"
            marker = race / "start.flag"
            payload_a = f"a-{round_number}".encode("ascii")
            payload_b = f"b-{round_number}".encode("ascii")
            write_file_durable(source_a, payload_a)
            write_file_durable(source_b, payload_b)
            relative = lambda path: str(path.relative_to(self.root))
            processes = [
                subprocess.Popen(
                    [
                        str(self.python),
                        str(self.probe),
                        "race-contender",
                        "--root",
                        str(self.root),
                        "--source",
                        relative(source),
                        "--target",
                        relative(target),
                        "--start-marker",
                        relative(marker),
                    ],
                    text=True,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"},
                )
                for source in (source_a, source_b)
            ]
            marker.write_bytes(b"start")
            outcomes = []
            for process in processes:
                stdout, stderr = process.communicate(timeout=15)
                self.assertEqual(process.returncode, 0, stderr)
                outcomes.append(json.loads(stdout)["outcome"])
            contenders += 2
            successes += outcomes.count("success")
            collisions += outcomes.count("collision")
            self.assertEqual(outcomes.count("success"), 1)
            self.assertEqual(outcomes.count("collision"), 1)
            winner = target.read_bytes()
            self.assertIn(winner, (payload_a, payload_b))
            if winner == payload_a:
                self.assertFalse(source_a.exists())
                self.assertEqual(source_b.read_bytes(), payload_b)
            else:
                self.assertFalse(source_b.exists())
                self.assertEqual(source_a.read_bytes(), payload_a)
        self.assertEqual(successes, rounds)
        self.assertEqual(collisions, rounds)
        print(
            "M0-006 Windows race evidence "
            f"rounds={rounds} contenders={contenders} successes={successes} "
            f"collisions={collisions} overwrites=0"
        )

    def test_file_flush_reopens_in_new_process_and_directory_flush_is_observed(self):
        payload = (b"flush-evidence-0123456789" * 4096) + b"tail"
        path = self.root / "flush" / "payload.bin"
        evidence = write_file_durable(path, payload, chunk_size=4096)
        result = self.run_probe(
            "hash",
            "--root",
            str(self.root),
            "--relative",
            str(path.relative_to(self.root)),
        )
        reopened = json.loads(result.stdout)
        self.assertEqual(reopened["sha256"], hashlib.sha256(payload).hexdigest())
        self.assertEqual(reopened["size"], len(payload))
        directory = directory_flush_probe(path.parent)
        self.assertIn(directory["supported"], (True, False))
        self.assertIn(directory["stage"], ("open", "flush"))
        print(
            "M0-006 Windows flush evidence "
            f"bytes={evidence['bytes']} file_flush=pass reopen_hash=pass "
            f"directory_flush_supported={str(directory['supported']).lower()} "
            f"directory_flush_stage={directory['stage']} winerror={directory['winerror']}"
        )

    def test_crash_boundaries_recover_in_two_new_processes(self):
        crashes = 0
        recoveries = 0
        for boundary in ("after_stage_flush", "after_target_rename"):
            operation = boundary.replace("_", "-")
            crashed = self.run_probe(
                "crash-fixture",
                "--root",
                str(self.root),
                "--operation",
                operation,
                "--boundary",
                boundary,
                expected_returncode=77,
            )
            self.assertEqual(crashed.stdout, "")
            crashes += 1
            for _ in range(2):
                recovered = self.run_probe(
                    "recover",
                    "--root",
                    str(self.root),
                    "--operation",
                    operation,
                )
                result = json.loads(recovered.stdout)
                self.assertEqual(result["state"], "complete")
                self.assertTrue(result["target_reopened_and_hashed"])
                self.assertTrue(result["stage_absent"])
                recoveries += 1
            stage = self.root / "operations" / operation / "stage.bin"
            target = self.root / "targets" / f"{operation}.bin"
            journal = json.loads(
                (self.root / "operations" / operation / "journal.json").read_text(encoding="utf-8")
            )
            self.assertFalse(stage.exists())
            self.assertEqual(hash_file(target), (journal["sha256"], journal["size"]))
        print(
            "M0-006 Windows crash evidence "
            f"crash_processes={crashes} exit_77={crashes} new_process_recoveries={recoveries} "
            "idempotent_passes=2_per_boundary"
        )

    def test_recovery_collision_preserves_stage_and_target(self):
        operation = "recovery-collision"
        incoming = b"incoming-recovery"
        original = b"existing-target"
        create_staged_operation(self.root, operation, incoming)
        target = self.root / "targets" / f"{operation}.bin"
        write_file_durable(target, original)
        recovered = self.run_probe(
            "recover",
            "--root",
            str(self.root),
            "--operation",
            operation,
        )
        result = json.loads(recovered.stdout)
        stage = self.root / "operations" / operation / "stage.bin"
        self.assertEqual(result["state"], "conflict")
        self.assertEqual(hash_file(stage), (hashlib.sha256(incoming).hexdigest(), len(incoming)))
        self.assertEqual(hash_file(target), (hashlib.sha256(original).hexdigest(), len(original)))


if __name__ == "__main__":
    unittest.main()
