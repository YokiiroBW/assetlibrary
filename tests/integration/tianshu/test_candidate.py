"""Offline proposal checks; these do not implement or certify an asset analyzer."""
from __future__ import annotations

import copy
from datetime import datetime
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from jsonschema import Draft202012Validator, FormatChecker, ValidationError

ROOT = Path(__file__).resolve().parents[3]
DOCS = ROOT / "docs/integrations/tianshu"
SCHEMA = json.loads((DOCS / "candidate.schema.json").read_text(encoding="utf-8"))
FORMATS = FormatChecker()


@FORMATS.checks("date-time", raises=ValueError)
def utc_timestamp(value):
    # jsonschema's RFC3339 plugin is optional; never silently omit this check.
    if not isinstance(value, str):
        return True
    return "T" in value and value.endswith("Z") and datetime.fromisoformat(value).tzinfo is not None


VALIDATOR = Draft202012Validator(SCHEMA, format_checker=FORMATS)


def validate_report(report):
    VALIDATOR.validate(report)
    entries = {entry["entry_id"]: entry for entry in report["entries"]}
    if len(entries) != len(report["entries"]):
        raise ValueError("duplicate entry ID")
    seen = set()
    for candidate in report["candidates"]:
        if candidate["candidate_id"] in seen:
            raise ValueError("duplicate candidate ID")
        seen.add(candidate["candidate_id"])
        members = candidate["entry_ids"]
        if not set(members) <= entries.keys():
            raise ValueError("candidate outside the scoped report")
        if candidate["kind"] == "preferred" and candidate["preferred_entry_id"] not in members:
            raise ValueError("preferred entry outside group")
        if candidate["kind"] == "exact_duplicate":
            hashes = {entries[entry_id]["sha256"] for entry_id in members}
            if None in hashes or len(hashes) != 1:
                raise ValueError("exact duplicate requires matching full hashes")


class CandidateTests(unittest.TestCase):
    def setUp(self):
        self.report = json.loads((DOCS / "examples/review-candidates.json").read_text(encoding="utf-8"))

    def test_schema_and_both_examples(self):
        Draft202012Validator.check_schema(SCHEMA)
        for example in (DOCS / "examples").glob("*.json"):
            validate_report(json.loads(example.read_text(encoding="utf-8")))

    def test_missing_capability_cannot_return_successful_candidates(self):
        self.report["analysis_state"] = "unavailable"
        self.report["unavailable_reason"] = "not_implemented"
        with self.assertRaises(ValidationError):
            validate_report(self.report)

    def test_execution_and_injected_paths_or_authority_are_rejected(self):
        for field, value in (("execution_available", True), ("delete", True),
                             ("root_path", "//nas/private"), ("principal_id", "admin")):
            with self.subTest(field=field):
                report = {**self.report, field: value}
                self.assertTrue(list(VALIDATOR.iter_errors(report)))

    def test_exact_duplicate_cannot_use_name_or_unknown_hash(self):
        for change in ("name", "missing", "different"):
            with self.subTest(change=change):
                report = copy.deepcopy(self.report)
                if change == "name":
                    report["candidates"][0]["evidence"] = "filename"
                else:
                    report["entries"][1]["sha256"] = None if change == "missing" else "0" * 64
                with self.assertRaises((ValidationError, ValueError)):
                    validate_report(report)

    def test_group_and_preferred_references_stay_in_scope(self):
        for change in ("member", "preferred", "duplicate"):
            with self.subTest(change=change):
                report = copy.deepcopy(self.report)
                if change == "member":
                    report["candidates"][0]["entry_ids"][0] = "ffffffff-ffff-4fff-8fff-ffffffffffff"
                elif change == "preferred":
                    report["candidates"][3]["preferred_entry_id"] = report["entries"][3]["entry_id"]
                else:
                    report["entries"].append(report["entries"][0])
                with self.assertRaises(ValueError):
                    validate_report(report)

    def test_report_size_version_and_timestamp_are_bounded(self):
        for field, value in (("entries", self.report["entries"] * 30),
                             ("version", "1.0.0"), ("revision", 0), ("observed_at", "yesterday")):
            with self.subTest(field=field):
                self.assertTrue(list(VALIDATOR.iter_errors({**self.report, field: value})))

    def test_synthetic_proposal_validation_does_not_modify_originals(self):
        # Data labels describe test relationships; these bytes are not real images or an analyzer output.
        files = {"original.bin": b"synthetic-original", "intentional-copy.bin": b"synthetic-original",
                 "revision.bin": b"synthetic-revision", "animation.bin": b"synthetic-animation",
                 "original.sidecar": b"synthetic-sidecar", "same-name/original.bin": b"different-content"}
        with tempfile.TemporaryDirectory(prefix="ts060-contract-") as temporary:
            root = Path(temporary)
            for name, payload in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(payload)
            def snapshot():
                return {path.relative_to(root).as_posix(): (hashlib.sha256(path.read_bytes()).hexdigest(), path.stat().st_mtime_ns)
                        for path in root.rglob("*") if path.is_file()}
            before = snapshot()
            validate_report(self.report)
            self.assertEqual(before, snapshot())
            self.assertEqual(before["original.bin"][0], before["intentional-copy.bin"][0])
            self.assertNotEqual(before["original.bin"][0], before["same-name/original.bin"][0])
            self.assertEqual(self.report["entries"][0]["sha256"], before["original.bin"][0])


if __name__ == "__main__":
    unittest.main()
