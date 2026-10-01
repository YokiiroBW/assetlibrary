# Release CI repair — 2026-10-01

The published source at d7b43eca94136e90303cce585f2b96c80223f3af failed GitHub run 36871866817. Both OS jobs rejected the short Adoptium build string; the PostgreSQL job falsely treated a passing test name containing `Skipped` as a skipped execution.

Both Java selectors now pin the complete vendor SemVer `21.0.12+8.0.LTS`, retaining the reviewed JDK patch and build. The human runtime/dependency lock remains `21.0.12+8`. The read-only native test writes unique TRX evidence; a shared test-only reader requires a completed, nonempty run, zero unsuccessful counters, matching individual passed outcomes, and readable valid evidence. The interactive database test reuses it and still requires exactly one executed test. Subprocess exit-code and platform selection gates remain in force.

The exact local CI repository command also exposed a pre-existing foundation assertion that forbade the TS-099 inspection infrastructure. The assertion now allows exactly the four existing read-only inspection files, forbids write access/modes and file mutation, and retains I/O exclusion from Domain/Application/Contracts. Product code, migrations, permissions, public contracts and dependencies are unchanged.

Local validation passed: repository verifier, 104 isolated repository tests (including nine result-gate regression tests), and v0.1-start. Alpha audit remains valid and blocked. Hosted CI at the new SHA is pending; this report does not claim it passed. The published rc.1 tag is immutable and will not be moved.

Official selector behavior: https://github.com/actions/setup-java/blob/v4/src/util.ts#L53-L60 and https://github.com/actions/setup-java/blob/v4/src/distributions/temurin/installer.ts#L34-L50.
