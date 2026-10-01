# Actual local validation — 2026-10-01

- `python -I -B scripts/verify_repository.py`: passed. Architecture 627 inputs; C# policy 668 files; migration manifest 23; manifest tests 21; architecture tests 14; generated SDK, dependency and source checks passed. Alpha decision remains blocked.
- `python -I -B -m unittest discover -s tests/repository -p test_*.py -v`: 104 passed, 0 failed, 0 skipped, 25.793 seconds. Includes nine TRX tests with subcases for passing names containing skip words, actual skip/failure, zero execution, missing/invalid/negative counters, missing/malformed evidence, aborted summary and mismatched individual outcomes.
- `python -B tests/architecture/check_release_gates.py --target v0.1-start`: allowed.
- `git diff --check`: passed before documentation staging; final staged check follows.

Diagnostic attempts are separate: the first repository invocation omitted required `-I` and yielded a packaging import error plus the stale inspection-layer assertion. The corrected original CI command yielded 104 tests / one stale assertion failure; after the narrow foundation update, the full 104 passed. No failed attempt is relabeled as success. Hosted .NET, PostgreSQL, browser and Kotlin results are pending at the new SHA.

## Hosted follow-up: frozen LF/CRLF evidence

Run 36875315275 resolved Java successfully but exposed CRLF-only hashes in two old foundation tests. The original hashes remain accepted; alternate LF hashes were verified directly against published d7b43ec Git blobs and differ only by line endings. Exactly those two byte serializations are accepted, without runtime normalization. SQL, contracts and the historical fixture files were not changed. The exact local isolated repository suite was rerun: 104 passed, no failures or skips. Hosted CI on the follow-up SHA remains required.
