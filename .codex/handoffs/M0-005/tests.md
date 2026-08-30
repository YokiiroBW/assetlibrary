# Verification

Final command: `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/postgres -p 'test_*.py' -v`

Result: 2 tests OK in 46.1 seconds. PostgreSQL 16.15 was built from the official
v16.15 source in `/tmp/m005-pg-install`; fresh private Unix-socket clusters were
used and removed after each run. Coverage includes same-transaction migration
locking, concurrent runners, checksum drift/rollback, task lease/heartbeat/
reclaim/cancel/idempotency, owner-aware outbox retry, permission positive/negative
examples, restart persistence, and 500,000 deterministic assets with selective
FTS/trigram assertions. Dynamic plans, sizes and five warm timings are written only
to ignored runtime storage. `git diff --check`: pass; no live server or bytecode remains.
