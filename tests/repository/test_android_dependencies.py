"""Fail-closed runtime dependency audit boundaries, without network access."""
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from datetime import datetime, timedelta, timezone

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("android_dependencies", ROOT / "scripts/validate_android_dependencies.py")
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


class AndroidDependencyAuditTests(unittest.TestCase):
    def test_missing_and_error_advisory_results_never_become_clean_audit(self):
        invalid = ({"results": []}, {"results": [{"error": "upstream_unavailable"}]}, {"results": [{"vulns": None}]}, {"results": [{"next_page_token": "more"}]})
        for result in invalid:
            with self.subTest(result=result), patch.object(AUDIT.urllib.request, "urlopen", return_value=io.BytesIO(json.dumps(result).encode())):
                with self.assertRaises(ValueError):
                    AUDIT.query_osv([("sample.runtime", "library", "1.0.0")])

    def test_advisories_remain_associated_with_the_exact_dependency(self):
        result = {"results": [{}, {"vulns": [{"id": "TEST-ADVISORY"}]}]}
        coordinates = [("sample.runtime", "clean", "1.0.0"), ("sample.runtime", "affected", "2.0.0")]
        with patch.object(AUDIT.urllib.request, "urlopen", return_value=io.BytesIO(json.dumps(result).encode())):
            rows = AUDIT.query_osv(coordinates)
        self.assertEqual(rows[0]["vulnerabilities"], [])
        self.assertEqual(rows[1]["coordinate"], "sample.runtime:affected:2.0.0")
        self.assertEqual(rows[1]["vulnerabilities"], [{"id": "TEST-ADVISORY"}])

    def test_inventory_rejects_dynamic_or_path_like_dependencies(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "inventory.tsv"
            for text in ("", "g\tn\t1.+\n", "../g\tn\t1.0.0\n", "g\tn\tunspecified\n"):
                path.write_text(text, encoding="utf-8")
                with self.subTest(text=text), self.assertRaises(ValueError):
                    AUDIT.runtime_inventory(path)

    def test_recorded_response_must_be_fresh_and_match_exact_query_inventory(self):
        with tempfile.TemporaryDirectory() as temporary:
            inventory = Path(temporary) / "inventory.tsv"
            response = Path(temporary) / "response.json"
            inventory.write_text("sample.runtime\tlibrary\t1.0.0\n", encoding="utf-8")
            coordinates = AUDIT.runtime_inventory(inventory)
            record = {"endpoint": "https://api.osv.dev/v1/querybatch", "inventory_sha256": AUDIT.digest(inventory), "checked_at": datetime.now(timezone.utc).isoformat(), "queries": AUDIT.osv_queries(coordinates), "results": [{}]}
            response.write_text(json.dumps(record), encoding="utf-8")
            self.assertEqual(AUDIT.recorded_osv(response, inventory, coordinates)[0]["vulnerabilities"], [])
            for change in ({"inventory_sha256": "0" * 64}, {"queries": []}, {"checked_at": (datetime.now(timezone.utc) - timedelta(days=2)).isoformat()}):
                response.write_text(json.dumps({**record, **change}), encoding="utf-8")
                with self.subTest(change=change), self.assertRaises(ValueError):
                    AUDIT.recorded_osv(response, inventory, coordinates)

    def test_license_parent_evidence_and_unreviewed_license_rejection(self):
        with tempfile.TemporaryDirectory() as temporary:
            cache = Path(temporary)
            parent = cache / "sample.runtime/parent/1.0.0/hash/parent.pom"
            child = cache / "sample.runtime/child/1.0.0/hash/child.pom"
            parent.parent.mkdir(parents=True)
            child.parent.mkdir(parents=True)
            parent.write_text('<project><licenses><license><name>Approved</name></license></licenses></project>', encoding="utf-8")
            child.write_text('<project><parent><groupId>sample.runtime</groupId><artifactId>parent</artifactId><version>1.0.0</version></parent></project>', encoding="utf-8")
            evidence = AUDIT.licenses(cache, ("sample.runtime", "child", "1.0.0"), {"license_names": {"Approved": "MIT"}})
            self.assertEqual(evidence["inherited_from"]["accepted_licenses"], ["MIT"])
            with self.assertRaises(ValueError):
                AUDIT.licenses(cache, ("sample.runtime", "child", "1.0.0"), {"license_names": {}})


if __name__ == "__main__":
    unittest.main()
