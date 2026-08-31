from __future__ import annotations

import os
import unittest

from isolation_probe import linux_isolation_evidence, windows_candidate_evidence


class IsolationProbeTests(unittest.TestCase):
    @unittest.skipUnless(os.name == "posix", "Linux namespace probe requires a Linux host")
    def test_linux_namespace_probe_is_executed_and_unproven_capabilities_stay_false(self) -> None:
        evidence = linux_isolation_evidence()
        self.assertIn("returncode", evidence["unshare_network"])
        expected_network = any(evidence[name].get("returncode") == 0 for name in ("unshare_network", "bubblewrap_network"))
        self.assertEqual(evidence["enforced_network_namespace"], expected_network)
        self.assertEqual(evidence["delegated_user_cgroup_executed"], evidence["systemd_user_scope"].get("returncode") == 0)
        self.assertFalse(evidence["filesystem_confinement_proven"])

    @unittest.skipIf(os.name == "nt", "Windows evidence is covered by WindowsIsolationProbeTests")
    def test_windows_mapping_is_not_reported_as_executed_without_windows(self) -> None:
        evidence = windows_candidate_evidence()
        self.assertFalse(evidence["executed"])


@unittest.skipUnless(os.name == "nt", "Windows Job Object evidence requires Windows 11")
class WindowsIsolationProbeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.evidence = windows_candidate_evidence()

    def test_job_active_process_limit_is_executed(self) -> None:
        result = self.evidence["active_process_limit"]
        self.assertTrue(result["passed"], result)
        self.assertEqual(result["configured_limit"], 1)
        self.assertTrue(result["second_assignment_denied"])

    def test_job_process_memory_limit_is_executed(self) -> None:
        result = self.evidence["process_memory_limit"]
        self.assertTrue(result["passed"], result)
        self.assertTrue(result["worker_memory_error"])
        self.assertLessEqual(result["peak_process_memory_bytes"], result["configured_bytes"])

    def test_job_process_cpu_time_limit_is_executed(self) -> None:
        result = self.evidence["cpu_time_limit"]
        self.assertTrue(result["passed"], result)
        self.assertTrue(result["process_time_limit_flag"])
        self.assertFalse(result["completion_report_created"])
        self.assertEqual(result["active_processes_after_limit"], 0)

    def test_job_kill_on_close_reaps_descendant(self) -> None:
        result = self.evidence["kill_on_close_descendant"]
        self.assertTrue(result["passed"], result)
        self.assertTrue(result["descendant_in_job"])
        self.assertTrue(result["root_dead_after_close"])
        self.assertTrue(result["descendant_dead_after_close"])

    def test_restricted_token_candidate_does_not_overclaim_file_or_network_sandbox(self) -> None:
        result = self.evidence["restricted_token"]
        self.assertTrue(result["passed"], result)
        self.assertTrue(result["token_is_restricted"])
        self.assertTrue(result["disable_max_privilege_observed"])
        self.assertTrue(result["group_only_acl_accessible"])
        self.assertTrue(result["same_user_escape_accessible"])
        self.assertTrue(result["loopback_network_accessible"])
        self.assertFalse(result["filesystem_confinement_proven"])
        self.assertFalse(result["network_denial_proven"])


if __name__ == "__main__":
    unittest.main()
