"""Run the official metadata-only 500k profile in an isolated sandbox."""
from __future__ import annotations
import json, resource, sys, time
from pathlib import Path
root = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(root / "packages" / "test-support"))
from performance import GeneratorConfig, write_manifest

out_dir = root / ".runtime" / "sandbox-storage" / "M0-007"
out_dir.mkdir(parents=True, exist_ok=True)
manifest = out_dir / "manifest-500k.jsonl"
started = time.perf_counter()
summary = write_manifest(GeneratorConfig(count=500_000, seed=20260831), manifest)
assert summary["records"] == 500_000
assert summary["hot_directory_records"] == 100_000
assert summary["has_100gib_asset"] is True
elapsed = time.perf_counter() - started
summary.update({
    "wall_seconds": round(elapsed, 6),
    "peak_rss_kib": resource.getrusage(resource.RUSAGE_SELF).ru_maxrss,
    "records_per_second": round(summary["records"] / elapsed, 2),
})
(out_dir / "summary-500k.json").write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")
print(json.dumps(summary, sort_keys=True))
