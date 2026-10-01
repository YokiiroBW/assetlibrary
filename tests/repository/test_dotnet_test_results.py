from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "dotnet_test_results", ROOT / "tests/database/dotnet_test_results.py")
assert SPEC and SPEC.loader
RESULTS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RESULTS)


class DotnetTestResultGateTests(unittest.TestCase):
    def test_e2e_fixture_loads_without_unittest_discovery_import_path(self) -> None:
        command = (
            "import importlib.util; from pathlib import Path; "
            "p=Path('tests/integration/read-only-trial/run_e2e.py').resolve(); "
            "s=importlib.util.spec_from_file_location('trial_loader',p); "
            "m=importlib.util.module_from_spec(s); s.loader.exec_module(m); "
            "f=m.load_fixture(); print(f.PostgreSqlIntegrationTests.__name__)"
        )
        result = subprocess.run([sys.executable, "-I", "-B", "-c", command],
                                cwd=ROOT, capture_output=True, text=True, timeout=20)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("PostgreSqlIntegrationTests", result.stdout.strip())

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="dotnet-result-gate-")
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "results.trx"

    def report(self, *, total: int = 1, outcome: str = "Passed",
               summary: str = "Completed", counters: dict | None = None,
               name: str = "AFilesizeAboveTheCeilingIsSkippedAndNeverHashed") -> None:
        ns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
        run = ET.Element(ns + "TestRun")
        results = ET.SubElement(run, ns + "Results")
        for index in range(total):
            ET.SubElement(results, ns + "UnitTestResult", outcome=outcome,
                          testName=name, executionId=str(index))
        result_summary = ET.SubElement(run, ns + "ResultSummary", outcome=summary)
        values = {"total": total, "executed": total, "passed": total,
                  "failed": 0, "notExecuted": 0, "error": 0, "timeout": 0}
        if counters:
            values.update(counters)
        ET.SubElement(result_summary, ns + "Counters",
                      {key: str(value) for key, value in values.items()})
        ET.ElementTree(run).write(self.path, encoding="utf-8", xml_declaration=True)

    def test_successful_names_containing_skip_words_pass(self) -> None:
        for name in ("AFilesizeAboveTheCeilingIsSkippedAndNeverHashed", "已跳过的文件不会哈希"):
            with self.subTest(name=name):
                self.report(name=name)
                self.assertEqual(1, RESULTS.require_all_tests_passed(self.path)["passed"])

    def test_actual_skipped_result_is_rejected(self) -> None:
        self.report(outcome="NotExecuted", counters={"executed": 0, "passed": 0, "notExecuted": 1})
        with self.assertRaises(AssertionError):
            RESULTS.require_all_tests_passed(self.path)

    def test_actual_failed_result_is_rejected(self) -> None:
        self.report(outcome="Failed", counters={"passed": 0, "failed": 1})
        with self.assertRaises(AssertionError):
            RESULTS.require_all_tests_passed(self.path)

    def test_zero_selected_tests_is_rejected(self) -> None:
        self.report(total=0)
        with self.assertRaises(AssertionError):
            RESULTS.require_all_tests_passed(self.path)

    def test_passing_counters_cannot_hide_an_unsuccessful_result(self) -> None:
        self.report(outcome="NotExecuted")
        with self.assertRaises(AssertionError):
            RESULTS.require_all_tests_passed(self.path)

    def test_passing_counters_cannot_hide_missing_results(self) -> None:
        self.report(counters={"total": 2, "executed": 2, "passed": 2})
        with self.assertRaises(AssertionError):
            RESULTS.require_all_tests_passed(self.path)

    def test_aborted_summary_and_additional_errors_are_rejected(self) -> None:
        for changes in ({"summary": "Aborted"}, {"counters": {"error": 1}},
                        {"counters": {"timeout": 1}}):
            with self.subTest(changes=changes):
                self.report(**changes)
                with self.assertRaises(AssertionError):
                    RESULTS.require_all_tests_passed(self.path)

    def test_missing_malformed_and_incomplete_evidence_is_rejected(self) -> None:
        with self.assertRaises(AssertionError):
            RESULTS.require_all_tests_passed(self.path)
        for content in ("<TestRun", "<TestRun />", "<Other />",
                        '<TestRun><ResultSummary outcome="Completed"><Counters total="1" /></ResultSummary></TestRun>'):
            with self.subTest(content=content):
                self.path.write_text(content, encoding="utf-8")
                with self.assertRaises(AssertionError):
                    RESULTS.require_all_tests_passed(self.path)

    def test_invalid_and_negative_counters_are_rejected(self) -> None:
        for counters in ({"total": "invalid"}, {"total": -1}, {"passed": -1}):
            with self.subTest(counters=counters):
                self.report(counters=counters)
                with self.assertRaises(AssertionError):
                    RESULTS.require_all_tests_passed(self.path)


if __name__ == "__main__":
    unittest.main()
