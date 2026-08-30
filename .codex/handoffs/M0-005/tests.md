# Verification

Final command: `M005_PG_BIN=/tmp/m005-independent-bootstrap-kV8peC/install/bin PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/postgres -p 'test_*.py' -v`

Result: 2 tests OK in 46.7 seconds on fresh PostgreSQL 16.15. Keyset uses the
permission-filtered EXISTS tuple cursor and `asset_library_path_keyset`; its plan
is ordered Index Only Scan without Bitmap/Incremental Sort. Path filtering is a
Parallel Seq Scan (~28.4 ms), retained as an M0-009 path-index decision. Table/index
sizes: 114 MB / 201 MB. End-to-end warm distributions including psql startup
(min/median/p95/max ms): keyset 32.406/33.323/35.190/35.190; FTS
59.480/61.445/66.773/66.773; trigram 34.856/35.190/40.346/40.346.
Handoff and architecture validators passed; no live server, cluster, cache or bytecode remains.
