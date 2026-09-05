# ALIGN-004 tests

PowerShell 7.6.4 and bundled Python 3.12 on Windows x64.

Passed: python -B -m unittest discover -s tests/spikes/server-packaging -p test_windows_service_contract.py -v (9 tests, 1.950 s).
Passed: git diff --check.

Native process/provenance suite and repository architecture/handoff verification are pending the clean implementation commit required by bootstrap.

No real service installation/start/stop/removal, registry write, Shell registration, Docker run or release/soak evidence is claimed.
