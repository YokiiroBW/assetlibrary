# Verification

Final command: `M005_PG_BIN=/tmp/m005-independent-bootstrap-kV8peC/install/bin PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/postgres -p 'test_*.py' -v`

Result: 2 tests OK in 46.7 seconds on fresh PostgreSQL 16.15. Keyset uses the
permission-filtered EXISTS tuple cursor and `asset_library_path_keyset`; its plan
is ordered Index Only Scan without Bitmap/Incremental Sort. Path filtering is a
Parallel Seq Scan (~25–29 ms), retained as an M0-009 path-index decision. Table/index
sizes: 114 MB / 201 MB. End-to-end warm distributions including psql startup
(min/median/p95/max ms): keyset 32.255/32.997/33.556/33.556; FTS
59.560/61.203/63.476/63.476; trigram 34.585/35.226/36.483/36.483.
Handoff and architecture validators passed; no live server, cluster, cache or bytecode remains.
