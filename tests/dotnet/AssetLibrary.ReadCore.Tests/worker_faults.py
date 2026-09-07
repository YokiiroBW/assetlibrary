"""Owned process fault fixture; it never opens an asset path."""
from pathlib import Path
import json
import os
import sys
import time

fault, pid_file = sys.argv[1:3]
Path(pid_file).write_text(str(os.getpid()), encoding="ascii")
if fault == "never_read":
    time.sleep(60)
    raise SystemExit(0)

sys.stdin.buffer.read(16 * 1024 + 1)
if fault == "nonzero":
    raise SystemExit(7)
if fault == "oversized":
    print("x" * (64 * 1024 + 1), flush=True)
elif fault == "stderr_limit":
    sys.stderr.write("x" * 65536)
    sys.stderr.flush()
    time.sleep(60)
elif fault == "wrong_count":
    print(json.dumps({"version": 1, "type": "complete", "observedEntries": 99}), flush=True)
elif fault == "incomplete":
    print(json.dumps({"version": 1, "type": "entry", "relativePath": "file.bin", "kind": 0,
                      "contentLength": 1, "lastWriteTimeUtc": "2026-09-07T00:00:00Z", "attributes": 0}), flush=True)
else:
    raise SystemExit(8)
