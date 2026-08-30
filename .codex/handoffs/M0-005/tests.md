# Verification

Final command: `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/postgres -p 'test_*.py' -v`

Result: 2 tests OK in 45.2 seconds. PostgreSQL 16.15 was built from the official
v16.15 source in `/tmp/m005-final-bootstrap`; fresh private Unix-socket clusters were
used and removed after each run. Coverage includes same-transaction migration
locking, concurrent runners, checksum drift/rollback, task lease/heartbeat/
reclaim/cancel/idempotency, owner-aware outbox retry, permission positive/negative
examples, restart persistence, and 500,000 deterministic assets with selective
FTS/trigram assertions. Dynamic plans, sizes and five warm timings are written only
to ignored runtime storage. Final warm distributions (ms): keyset min 32.991 / median
34.569 / p95 36.278 / max 36.278; FTS min 60.415 / median 61.424 / p95 64.501 /
max 64.501; trigram min 35.700 / median 36.008 / p95 37.249 / max 37.249. Table/index sizes were
114 MB / 201 MB. These are end-to-end observations including psql process startup.
Latest distributions (min/median/p95/max ms): keyset 32.316/32.683/36.054/36.054,
FTS 59.803/60.626/63.834/63.834, trigram 35.375/36.090/36.409/36.409. Table/index
sizes: 114 MB / 201 MB. Path-filter EXPLAIN is included in dynamic evidence.
`git diff --check`: pass; no live server or bytecode remains.
