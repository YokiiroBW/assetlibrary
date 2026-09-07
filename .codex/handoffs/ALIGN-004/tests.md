# ALIGN-004 verification

Implementation under test: `e6074f265f7de023512b79ffcf15ee1d31fec5ac` on Windows x64. Final verification date: 2026-09-07. Python 3.12.14, PowerShell 7.6.5 and repository-pinned .NET SDK 10.0.111 were used for the native run. The earlier unchanged service-adapter regression run used PowerShell 7.6.4 and bundled Python 3.12.

## Test results

| Command | Result | Evidence |
| --- | --- | --- |
| `python -B -m unittest discover -s tests/spikes/server-packaging -p test_windows_service_contract.py -v` | 9 passed, 0 failed, 0 skipped; 1.950 s | Retained completed implementation-checkpoint result. Three source-contract tests and six executable PowerShell mock/temporary-file cases. The service script and this test file did not change afterward; this successful run was not repeated. |
| `python -B -m unittest discover -s tests/spikes/server-packaging -p test_spike.py -v` with `M0_004_DOTNET_DIR` selecting SDK 10.0.111 | 11 passed, 0 failed, 0 skipped; 158.649 s | Complete log: `.runtime/ALIGN-004/native-suite.log`. Initial dual-runtime publish, Windows native behavior and three independent cold dual-runtime publishes completed. |

Total: **20 unique test methods**, with no failures or skips. Parameterized response/CLI cases and repeated cold publishes are assertions within these methods and are not added to the total.

The previous native-suite invocation was interrupted before a complete Windows artifact and provenance manifest existed; it is not counted as a completed run. The completed run above emitted no build warnings. The intervening source commits addressed the duplicate central package version and existing analyzer errors; they did not suppress analyzers or change package versions.

Native behavior covered embedded build information, both supported CLI spellings, missing/duplicate options, invalid port/bind configuration, unwritable data setup, health and readiness, graceful exit, restart and occupied port. Probe cases covered correct/mismatched contracts, wrong field types, non-object and malformed JSON, excessive body size, HTTP failure and a body delayed beyond the deadline.

Adapter regressions covered `$Arguments` forwarding and native exit failures, CIM query failure, service identity mismatches, nonempty-data retention and repeated cleanup, live-service/escaping-path rejection, and rollback after a failed service deletion. Mocks load only PowerShell function definitions and never execute the adapter's mutation dispatch.

## Provenance

The native suite ran from a clean committed worktree. Each cold publish clears only the bootstrap's checked task-local build/artifact paths and disables build servers. The three repeated publish runs produced the same full `files.sha256` manifest and aggregate digest for both `linux-x64` and `win-x64` on this host.

- Issuance/source commit: `e6074f265f7de023512b79ffcf15ee1d31fec5ac`.
- Embedded informational version: `0.1.0-spike+e6074f265f7de023512b79ffcf15ee1d31fec5ac`.
- Manifest SHA-256: `613a607457e72693a3fe6f40eff4c8a9933a97af274ce2701d2f7215e00002ad`.
- Linux executable SHA-256: `a2e5c0a1d967b573721b94663d67a4e9fcca4483c8c461c54c2cf9d2a48586f3` (78,256 bytes).
- Windows executable SHA-256: `5bd86009f3363efce781f08d26ce316d49331b4e5cd8ecdbbb907bccd5500f96` (162,816 bytes).
- Complete native-run log SHA-256: `d19fae5e4b04062f9fe4cd85da3ac3a4d2292cb6d2ff983c918db8e4c5a841a3`.

The full manifest and source marker remain under `.runtime/sandbox-storage/M0-004/artifact/`. These hashes describe this source issuance; they do not replace the original M0-004 artifact evidence or claim cross-host binary identity.

## Read-only environment verification and final checks

The actual script's `preflight` returned `executable_present=true`, `service_present=false`, `owned_registration=false`, `data_path_present=false`, `staging_path_present=false` and `elevated=false`. `verify-absent` returned `{"service":false,"registry":false,"data":false}`. Both commands exited 0. There were no remaining task-owned Python, dotnet or Spike processes after the suite.

Final checks passed: `python -I -B scripts/validate_handoff.py` (37 required artifacts, including its internal `validate_architecture_baseline.py` call); result validation against `contracts/handoffs/task-result.schema.json` using the existing repository `SchemaStore`; exact agreement between the 9 changed files and `result.json`; and `git diff --check`. The schema check covers every validation keyword present in this handoff schema and rejects a missing required architecture review. No new validation dependency was installed. The successful architecture check was not separately repeated.

## Limits

No real service install/start/stop/remove, registry write, Shell registration, Docker run, release/soak exercise or real asset access is claimed. This run cross-published Linux output but did not execute it on Linux. Windows Service and Docker release evidence remain pending under the existing gates. Generated task-local build/package caches are ignored and retained; Git cleanliness is evaluated independently of those caches.
