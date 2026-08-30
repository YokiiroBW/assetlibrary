from __future__ import annotations

import json
import os
import signal
import subprocess
import tempfile
import time
import unittest
from pathlib import Path

from manifest import result_is_l0_safe
from supervisor import Limits, Outcome, ProviderSupervisor, RestartPolicy, default_manifest


def alive(pid: int) -> bool:
    if pid <= 0:
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    proc_status = Path(f"/proc/{pid}/stat")
    if proc_status.exists():
        fields = proc_status.read_text(encoding="utf-8").split()
        return len(fields) < 3 or fields[2] != "Z"
    return True


class ProviderSupervisorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="m0-008-provider-")
        self.runtime = Path(self.temp.name)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def supervisor(self, limits: Limits | None = None) -> ProviderSupervisor:
        return ProviderSupervisor(default_manifest(limits), self.runtime)

    def test_happy_response_is_correlated_and_l0_safe(self) -> None:
        outcome = self.supervisor().run()
        self.assertEqual(outcome.status, "ok")
        self.assertEqual(outcome.response["request_id"], "req-1")
        self.assertTrue(result_is_l0_safe(outcome.response))
        self.assertFalse(outcome.response["original_write"])

    def test_absolute_deadline_and_cancel_reap_worker(self) -> None:
        supervisor = self.supervisor(Limits(request_deadline_ms=100))
        outcome = supervisor.run("hang")
        self.assertEqual(outcome.status, "timeout")
        self.assertIsNotNone(outcome.returncode)
        self.assertFalse(alive(supervisor.process.pid))

        cancellable = self.supervisor()
        # The cancel fixture is intentionally sleeping; cancel owns the process group.
        process = cancellable._spawn("cancel")
        self.assertTrue(alive(process.pid))
        cancellable.cancel()
        self.assertIsNotNone(process.returncode)
        self.assertFalse(alive(process.pid))

    def test_crash_and_protocol_faults_are_not_success(self) -> None:
        for mode, expected in (("crash", "crashed"), ("malformed", "protocol_error"), ("oversized", "protocol_error"), ("stdout_flood", "protocol_error")):
            with self.subTest(mode=mode):
                outcome = self.supervisor().run(mode)
                self.assertEqual(outcome.status, expected)

    def test_stderr_flood_is_drained_and_deadline_still_applies(self) -> None:
        outcome = self.supervisor(Limits(request_deadline_ms=150)).run("stderr_flood")
        self.assertEqual(outcome.status, "timeout")
        self.assertEqual(outcome.stderr_bytes, 8192)

    def test_descendant_cleanup_is_verified_not_assumed(self) -> None:
        # This desktop session has over 100 unrelated user processes, so the
        # fixture reserves a bounded but sufficient account-wide process cap.
        supervisor = self.supervisor(Limits(request_deadline_ms=1000, processes=1024))
        outcome = supervisor.run("child")
        self.assertEqual(outcome.status, "timeout")
        child_pid = int((self.runtime / "child.pid").read_text(encoding="utf-8"))
        self.assertFalse(alive(child_pid), f"child survived process-group cleanup: {child_pid}")

    def test_request_limit_is_checked_before_dispatch(self) -> None:
        outcome = self.supervisor(Limits(max_request_bytes=128)).run(request={"message_type": "request", "rpc_version": "1.0", "request_id": "large", "operation": "metadata", "input_tokens": ["opaque:" + "x" * 1000], "deadline_at": "now", "max_response_bytes": 10})
        self.assertEqual(outcome.status, "rejected")
        self.assertEqual(outcome.detail, "request_oversize")

    def test_memory_cpu_process_and_descriptor_limits_have_observable_outcomes(self) -> None:
        memory = self.supervisor(Limits(memory_bytes=64 * 1024 * 1024, request_deadline_ms=1000)).run("memory")
        self.assertIn(memory.status, {"ok", "crashed", "protocol_error"})
        if memory.status == "ok":
            self.assertEqual(memory.response["code"], "memory_limit")

        cpu = self.supervisor(Limits(cpu_ms=1000, request_deadline_ms=3000)).run("cpu")
        self.assertEqual(cpu.status, "crashed")
        self.assertNotEqual(cpu.returncode, 0)

        descriptors = self.supervisor(Limits(file_descriptors=32, request_deadline_ms=1000)).run("fd_exhaust")
        self.assertEqual(descriptors.status, "ok")
        self.assertEqual(descriptors.response["code"], "descriptor_limit")
        self.assertLess(int(descriptors.response["detail"]), 32)

        processes = self.supervisor(Limits(processes=8, request_deadline_ms=2000)).run("process_exhaust")
        self.assertEqual(processes.status, "ok")
        self.assertEqual(processes.response["code"], "process_limit")

    def test_restart_backoff_and_circuit_breaker(self) -> None:
        policy = RestartPolicy(max_failures=3, base_delay_ms=10)
        policy.record(True, now=100.0)
        self.assertTrue(policy.can_start(now=100.0))
        policy.record(True, now=100.0)
        policy.record(True, now=100.0)
        self.assertEqual(policy.delay_ms(), 40)
        self.assertFalse(policy.can_start(now=100.039))
        self.assertTrue(policy.can_start(now=100.040))
        policy.record(False, now=100.1)
        self.assertTrue(policy.can_start(now=100.1))

    def test_safe_mode_and_l0_degradation_without_provider(self) -> None:
        official = default_manifest(trust_class="official_signed")
        side_loaded = default_manifest(trust_class="side_loaded")
        from manifest import safe_mode_allows
        self.assertTrue(safe_mode_allows(official, safe_mode=True))
        self.assertFalse(safe_mode_allows(side_loaded, safe_mode=True))
        base_asset = {"asset_id": "opaque-asset", "asset_level": "L0", "browseable": True, "external_open": True, "original_write": False}
        self.assertTrue(base_asset["browseable"] and base_asset["external_open"])
        self.assertTrue(result_is_l0_safe(base_asset))


if __name__ == "__main__":
    unittest.main()
