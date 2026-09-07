# ALIGN-004: Windows Service Spike guardrail reconciliation

- Task ID: `ALIGN-004`
- Milestone: `V0.1` coordination; implementation remains M0 test-only
- Owner: `server-packaging-owner`
- Branch: `codex/align-004-windows-service-guard-reconciliation`
- Worktree: `C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-004`
- Base: `c1df723`
- Status: implementation and bounded verification complete; ready for coordinator integration review
- Implementation commit: `e6074f265f7de023512b79ffcf15ee1d31fec5ac`

## Objective

Reconcile origin M0-004 guardrails with current Windows evidence. Preserve build provenance and existing CLI configuration, strengthen ownership/readback/rollback, and execute regressions without installing or starting a Windows service.

## Required context

Read AGENTS.md, .codex/START_HERE.md, docs/01, docs/02, docs/05, docs/16, docs/17, docs/22, .codex/policies/CODE_QUALITY.md, the M0-004 task/handoff/Spike docs, ADR-0009/0012/0013, m0-gates.json, and both main/origin versions of the assigned files.

## Ownership and allowed paths

- tests/spikes/server-packaging/scripts/windows-service.ps1
- tests/spikes/server-packaging/src/Program.cs
- tests/spikes/server-packaging/src/ServerPackagingSpike.csproj (coordinator-approved compatibility fix: reuse the root package pin and isolate generated lock state)
- tests/spikes/server-packaging/test_windows_service_contract.py
- tests/spikes/server-packaging/test_spike.py
- This task package and .codex/handoffs/ALIGN-004/**.

All other paths, old M0 handoffs, project state, contracts, dependencies, versions, production modules, migrations and release-gate state are read-only. The coordinator owns history integration and global workspace alignment.

The coordinator approved the Spike project compatibility fix after NU1008 exposed its duplicate package version under the current central package policy. Root dependency versions and production gates remain read-only.

## Architecture and reuse

Module: server-packaging-spike. Reuse its ASP.NET Core host, WindowsServices lifetime, m0-004/v1 probe, deterministic bootstrap and Python/PowerShell test tooling. No business rule, database model, language, framework, dependency or shared contract is added. Keep one source for all host packages and preserve every release blocker.

## Constraints and acceptance

- Default preflight and verify-absent remain read-only; no SCM/HKLM write or Shell registration is authorized.
- Retain --build-info, pinned informational version and uppercase --SPIKE_* options; accept the origin option spelling with the same semantics.
- Stage beneath the dedicated system-temp root. Verify ownership, account/binPath, readback, bounded waits and containment before cleanup; preserve nonempty data and service-referenced staging.
- Service-query failure must never mean absence. Native command arguments must survive PowerShell binding.
- Tests use mocks or temporary files/loopback process hosts. No real assets, credentials, external database, service installation or capacity/soak run.
- Review stable diff, run targeted behavior tests, affected native process suite and architecture/contract checks. Record platform skips truthfully.
- Commit code and complete summary/result/tests handoff. Do not mark M0-004 or any release complete.

## Verification

- python -B -m unittest discover -s tests/spikes/server-packaging -p test_windows_service_contract.py -v
- python -B -m unittest discover -s tests/spikes/server-packaging -p test_spike.py -v, with the existing SDK selected by M0_004_DOTNET_DIR.
- python -B scripts/validate_architecture_baseline.py
- python -B scripts/validate_handoff.py
- git diff --check

Bootstrap requires a clean committed worktree for provenance. Commit implementation before native verification, then commit verification metadata separately.

## Completion evidence — 2026-09-07

The implementation contains the reconciled service guardrails, both CLI spellings, bounded contract-checked probes, the central-package compatibility fix and analyzer-compliant host responsibilities. No root dependency or release gate changed.

The 9 service adapter regressions and 11 native process/provenance tests passed (20 unique tests, no failures or skips). The native suite included three independent cold publishes for both runtime identifiers. Read-only preflight and verify-absent confirmed no service, registry or system-temp staging residue. Final commands, provenance, compatibility limits and architecture review are recorded in `.codex/handoffs/ALIGN-004/`.

Only the coordinator updates the task registry and integrates this branch. The Windows Service lifecycle and Docker release gates remain open; this bounded reconciliation does not complete M0-004 or a release.
