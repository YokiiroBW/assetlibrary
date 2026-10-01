# Release CI repair — 2026-10-01

The published source at d7b43eca94136e90303cce585f2b96c80223f3af failed GitHub run 36871866817. Both OS jobs rejected the short Adoptium build string; the PostgreSQL job falsely treated a passing test name containing `Skipped` as a skipped execution.

Both Java selectors now pin the complete vendor SemVer `21.0.12+8.0.LTS`, retaining the reviewed JDK patch and build. The human runtime/dependency lock remains `21.0.12+8`. The read-only native test writes unique TRX evidence; a shared test-only reader requires a completed, nonempty run, zero unsuccessful counters, matching individual passed outcomes, and readable valid evidence. The interactive database test reuses it and still requires exactly one executed test. Subprocess exit-code and platform selection gates remain in force.

The exact local CI repository command also exposed a pre-existing foundation assertion that forbade the TS-099 inspection infrastructure. The assertion now allows exactly the four existing read-only inspection files, forbids write access/modes and file mutation, and retains I/O exclusion from Domain/Application/Contracts. Product code, migrations, permissions, public contracts and dependencies are unchanged.

Local validation passed: repository verifier, 104 isolated repository tests (including nine result-gate regression tests), and v0.1-start. Alpha audit remains valid and blocked. Hosted CI at the new SHA is pending; this report does not claim it passed. The published rc.1 tag is immutable and will not be moved.

Official selector behavior: https://github.com/actions/setup-java/blob/v4/src/util.ts#L53-L60 and https://github.com/actions/setup-java/blob/v4/src/distributions/temurin/installer.ts#L34-L50.

## Hosted follow-up: frozen LF/CRLF evidence

Run 36875315275 resolved Java successfully but exposed CRLF-only hashes in two old foundation tests. The original hashes remain accepted; alternate LF hashes were verified directly against published d7b43ec Git blobs and differ only by line endings. Exactly those two byte serializations are accepted, without runtime normalization. SQL, contracts and the historical fixture files were not changed. The exact local isolated repository suite was rerun: 104 passed, no failures or skips. Hosted CI on the follow-up SHA remains required.

## Follow-up after run 36876070081

The structured native/database stage passed. The E2E fixture loader exposed the added top-level import dependency; the native test now imports its TRX reader when actually invoked, and an isolated loader regression prevents recurrence. Linux root-path testing now derives a real root from the absolute temporary path, retaining the rejection assertion without using Windows-only SystemDirectory. The NAS shell fixture excludes its two Linux-container path variables from MSYS environment conversion; no product shell/path policy was relaxed. Local full isolated repository suite: 105 passed; .NET source policy: 668 files passed. Hosted native C#/browser evidence at the next SHA is pending.

## Follow-up after run 36877324969

Windows generated trial files/directories now explicitly have the current test-user SID as owner, in addition to the existing private ACL; this is confined to newly created synthetic fixtures. Production permission enforcement is unchanged. The mobile browser sequence now follows the existing image-preview dialog, expands file information, verifies the same filename and relative path, retains no-horizontal-overflow/screenshot checks, and closes the actual dialog. It does not waive a UI failure or reduce the real browser trial. C# source policy668 and Node browser-script syntax passed; hosted execution remains pending.
