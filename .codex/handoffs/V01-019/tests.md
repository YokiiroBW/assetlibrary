# V01-019 verification checkpoint — 2026-09-07

- `python -I -B -m unittest discover -s tests/release -p test_read_only_trial_package.py -v`: 5 passed, 0 skipped; `ASSETLIBRARY_TEST_TRIAL_POSTGRES_BIN` configured to PostgreSQL16.15 and `ASSETLIBRARY_TEST_TRIAL_REQUIRED=1`.
- Native test: new private temporary PostgreSQL cluster, 14 forward migrations, backup SHA-256 verified, six distinct module LOGIN roles, initialization replay preserves credentials/backup, repeated start, stop/restart, forged process creation time rejected, wrong cluster system identifier rejected.
- `python -I -B scripts/validate_architecture_baseline.py`: passed (131 governed inputs).
- `python -I -B scripts/generate_assetlink_sdks.py --check`: 7 generated files current.
- PowerShell parser passed.

Initial failures: PowerShell test harness argument passing was corrected to a temporary script; Windows pg_ctl inherited pipe handles caused a wait, corrected to no inherited capture pipes for pg_ctl; deployment LOGIN identifiers were changed to the existing migration tool's assetlibrary_ namespace. The owned test PostgreSQL was identity-checked and stopped, then the complete five-test set passed.

Pending: actual same-source Host/Web package build and complete PowerShell initialize/operator/start/status/stop flows after coordinator Host integration. No native evidence is inferred from static checks.

## Native integration checkpoint

- Merged coordinator 98e7585637f4e00bf6f83246d55e24395ef8adf4. Two real clean-commit self-contained Windows Host/Web packages built successfully, first source02f2ff8 and second d12568e. The latter ZIP SHA-256 is 1c6cc02b02270cbe4967907ee2581d4ca7e934e0916088f91f238a0d49cfb49a (candidate only).
- Actual package PowerShell initialize succeeded with18 migrations; start verified real pinned-certificate HTTPS readiness; stop preserved state. Third-process rotate-key succeeded after initialization and intervening Host start/stop. Rotate reads/unprotects the prior key first, so cross-process DP/key decryption has evidence.
- Added and passed runtime preflight for foreign private state preservation (no lock creation), changed Web bytes, unexpected package members and manifest path traversal. PG native suite now includes18 migrations and simulated full state volume while writing the new process record: its own newly started server is stopped, not left untracked.
- Native package bootstrap test failed, not skipped: original wrapper parsed a JSON log plus result as one document. Coordinator moved logs to stderr; current wrapper requires one stdout JSON and fixes UTF-8 pipe/terminal encoding. The actual core result was exit69/service_unavailable.
- Read-only diagnostics of a fixed non-secret HIBP prefix returned200 with98,561 bytes,2509 lines and no finalCRLF. The old core checker accepts at most1200 lines and mandates a trailingCRLF; this observation was handed to the authentication owner. Separate TLS connection failures also occurred; no TLS or risk-check bypass was added and no secret/prefix derived from a real credential was logged.
- Full native package test (bootstrap/replay/HTTPS login/restart/recover/tamper/PID/no-secret assertions) awaits coordinator fixes and must pass before this task is ready_for_review.

## 2026-09-07 latest accepted native evidence

The complete same-source native package test passed against build-04/source339275bf7b02fcf448d112b8789b146bc756dfbd in123.429s. It exercised the actual packaged self-contained Host and Web (copied to an isolated temporary directory), PostgreSQL18 migrations, private configuration and real risk screening. Assertions: first administrator bootstrap, same-operation replay, HTTPS page and administrator login with only the test certificate trusted, duplicate start rejection, forged local shutdown generation/nonce refusal, forged launcher process creation-time refusal, graceful stop and control-file cleanup, restart/login persistence, administrator recovery/new-secret login, package byte tampering refusal, unchanged source SHA-256/mtime, and no credentials/stop_nonce in output or logs. At most3 identical logical operator attempts are allowed only for actual service_unavailable; after3 it fails. There is no fake risk switch or TLS validation bypass.

The startup launcher now closes inherited capture handles explicitly. The first complete native test had hung after an otherwise successful bootstrap/replay/start because an old PowerShell-launched child inherited the caller's capture pipe; the owned instance was identity-checked and stopped, and the new isolated launcher fixed that regression.

Crypto observation recorded metadata only, never key bytes and never deleted any profile container. With the other TLS test owners paused for the new observation: old forced stop had13→14→14 files,1 new/1 remaining; new graceful stop had15→16→15 files,1 new/0 remaining, shutdown=graceful. These are specific start/stop observations, not a claim that all earlier profile containers were removed. Evidence files are `.runtime/sandbox-storage/V01-019/crypto-old-stop-observation.json` and `crypto-graceful-observation.json`.

The new Host local control file passed actual C# build with zero warnings/errors after splitting record/file/watcher responsibilities to meet CA1506, plus targeted formatting. Five package/preflight tests and the real18-migration PG test passed separately;7 unique task tests have now passed. The remaining work is final artifact rebuild after the coordinator's pending Windows orphan-worker fix and final handoff refresh; no passed unrelated check will be rerun without changed inputs.
