# Verification

`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/postgres -p 'test_*.py' -v`

Result: 2 tests OK. PostgreSQL 16.15 was built from the official v16.15 source in
`/tmp/m005-pg-install`; each test used a fresh private Unix-socket cluster under
`/tmp/m005-pg-*`, then stopped and removed it. 500,000-row load took approximately
45.3 seconds on Linux x86-64; plans and observed execution times are in
`docs/spikes/M0-005/500k-plan.txt`.

`git diff --check`: pass. No live test server, generated cluster, credentials, or
Python bytecode remains in the worktree.
