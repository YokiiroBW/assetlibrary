from __future__ import annotations

import unittest

from isolation_probe import linux_isolation_evidence, windows_candidate_evidence


class IsolationProbeTests(unittest.TestCase):
    def test_linux_namespace_probe_is_executed_and_unproven_capabilities_stay_false(self) -> None:
        evidence = linux_isolation_evidence()
        self.assertIn("returncode", evidence["unshare_network"])
        self.assertFalse(evidence["enforced_network_namespace"], evidence)
        self.assertFalse(evidence["filesystem_confinement_proven"])

    def test_windows_mapping_is_not_reported_as_executed(self) -> None:
        evidence = windows_candidate_evidence()
        self.assertFalse(evidence["executed"])


if __name__ == "__main__":
    unittest.main()
