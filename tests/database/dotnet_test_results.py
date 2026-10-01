"""Require complete, successful .NET execution from its structured TRX evidence."""
from __future__ import annotations

import xml.etree.ElementTree as ET
from pathlib import Path


def require_all_tests_passed(path: Path) -> dict[str, int]:
    try:
        run = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as exc:
        raise AssertionError(f"Cannot read .NET TRX evidence: {path}") from exc
    summaries = run.findall("{*}ResultSummary")
    if run.tag.rsplit("}", 1)[-1] != "TestRun" or len(summaries) != 1:
        raise AssertionError("TRX must contain one TestRun result summary")
    summary = summaries[0]
    counters = summary.findall("{*}Counters")
    if summary.get("outcome") != "Completed" or len(counters) != 1:
        raise AssertionError(".NET execution did not complete with result counters")
    try:
        observed = {key: int(value) for key, value in counters[0].attrib.items()}
        total = observed["total"]
        expected = {"total": total, "executed": total, "passed": total,
                    "failed": 0, "notExecuted": 0}
    except (KeyError, ValueError) as exc:
        raise AssertionError("TRX counters are missing or invalid") from exc
    if total <= 0 or any(observed.get(key) != value for key, value in expected.items()):
        raise AssertionError(f".NET tests failed, skipped, or did not execute: {observed}")
    if any(value != 0 for key, value in observed.items() if key not in expected):
        raise AssertionError(f".NET reported an additional unsuccessful outcome: {observed}")
    results = run.findall("{*}Results/{*}UnitTestResult")
    if len(results) != total or any(result.get("outcome") != "Passed" for result in results):
        raise AssertionError("TRX individual outcomes do not match the passing counters")
    return expected
