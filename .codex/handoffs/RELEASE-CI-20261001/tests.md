# Actual local validation — 2026-10-01

- `python -I -B scripts/verify_repository.py`: passed. Architecture 627 inputs; C# policy 668 files; migration manifest 23; manifest tests 21; architecture tests 14; generated SDK, dependency and source checks passed. Alpha decision remains blocked.
- `python -I -B -m unittest discover -s tests/repository -p test_*.py -v`: 104 passed, 0 failed, 0 skipped, 25.793 seconds. Includes nine TRX tests with subcases for passing names containing skip words, actual skip/failure, zero execution, missing/invalid/negative counters, missing/malformed evidence, aborted summary and mismatched individual outcomes.
- `python -B tests/architecture/check_release_gates.py --target v0.1-start`: allowed.
- `git diff --check`: passed before documentation staging; final staged check follows.

Diagnostic attempts are separate: the first repository invocation omitted required `-I` and yielded a packaging import error plus the stale inspection-layer assertion. The corrected original CI command yielded 104 tests / one stale assertion failure; after the narrow foundation update, the full 104 passed. No failed attempt is relabeled as success. Hosted .NET, PostgreSQL, browser and Kotlin results are pending at the new SHA.

## Hosted follow-up: frozen LF/CRLF evidence

Run 36875315275 resolved Java successfully but exposed CRLF-only hashes in two old foundation tests. The original hashes remain accepted; alternate LF hashes were verified directly against published d7b43ec Git blobs and differ only by line endings. Exactly those two byte serializations are accepted, without runtime normalization. SQL, contracts and the historical fixture files were not changed. The exact local isolated repository suite was rerun: 104 passed, no failures or skips. Hosted CI on the follow-up SHA remains required.

## Follow-up after run 36876070081

The structured native/database stage passed. The E2E fixture loader exposed the added top-level import dependency; the native test now imports its TRX reader when actually invoked, and an isolated loader regression prevents recurrence. Linux root-path testing now derives a real root from the absolute temporary path, retaining the rejection assertion without using Windows-only SystemDirectory. The NAS shell fixture excludes its two Linux-container path variables from MSYS environment conversion; no product shell/path policy was relaxed. Local full isolated repository suite: 105 passed; .NET source policy: 668 files passed. Hosted native C#/browser evidence at the next SHA is pending.

## Follow-up after run 36877324969

Windows generated trial files/directories now explicitly have the current test-user SID as owner, in addition to the existing private ACL; this is confined to newly created synthetic fixtures. Production permission enforcement is unchanged. The mobile browser sequence now follows the existing image-preview dialog, expands file information, verifies the same filename and relative path, retains no-horizontal-overflow/screenshot checks, and closes the actual dialog. It does not waive a UI failure or reduce the real browser trial. C# source policy668 and Node browser-script syntax passed; hosted execution remains pending.

## Windows owned-directory fixture follow-up

Run 36878599187 at f40a632 passed Ubuntu and the real HTTPS PostgreSQL/worker/browser job. Windows had one remaining Preview test failure before the product Protect call: elevated runners created the supposed user-owned child with Administrators as owner. The fixture now explicitly sets its current-user owner at creation while inheriting the original parent Modify/System ACLs. Assertions for the owner, protected ACL, exact two full-control principals and no inherited grants remain. Product code and permission policy are unchanged. Hosted Windows verification at the follow-up SHA is required.
