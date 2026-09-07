# ALIGN-004: Windows Service Spike guardrail reconciliation

Status: ready for integration review. Implementation commit: `e6074f265f7de023512b79ffcf15ee1d31fec5ac`. Branch: `codex/align-004-windows-service-guard-reconciliation`.

The NAS guardrail branch and current local implementation are reconciled without changing the production host or release gates. Native command arguments use `$Arguments`, preserving PowerShell forwarding. CIM failures propagate instead of being treated as absence. Service ownership checks cover the installation ID, registry marker, exact binary command, LocalService account and description. Normal uninstall stops and reads back the service, requests removal, confirms both service and registration absence, then removes owned application staging. Rollback retains staging when deletion or readback fails; nonempty data is preserved.

The existing `--build-info` and uppercase `--SPIKE_*` options remain supported, and the NAS lowercase/hyphenated aliases receive the same missing-value and duplicate validation. The health probe checks the existing `m0-004/v1` contract with a two-second whole-response deadline, a 4,096-byte body limit and bounded JSON depth. Host setup, configuration, diagnostics and the probe are concrete static classes; existing ASP.NET Core, WindowsServices and generated `LoggerMessage` functionality are reused without a new interface, package or framework.

The Spike project now consumes the existing central package version. Its generated restore lock stays under the per-runtime intermediate directory used by cold publishes. This resolves the current central-package and analyzer build failures without changing root dependencies, generated SDKs or public contracts.

Verification passed: 20 unique tests (9 adapter regressions and 11 Windows native/process/provenance tests), including three independent cold publishes with identical complete dual-runtime manifests. Build information identifies the implementation commit. Read-only preflight and verify-absent confirmed that no service, registry or system-temp staging residue exists. Details and artifact hashes are in `tests.md`.

File operations in executable tests use synthetic temporary data. No real SCM/HKLM mutation, Shell registration, Docker action or user asset operation was performed. Probe memory is bounded independently of response size; this test-only host does not enumerate or index assets and adds no 500,000-asset runtime path. Dependency direction, production permission policy, database ownership and shared contracts are unchanged.

The Windows Service lifecycle and Docker release gates remain open. Linux output was cross-published here but not executed on Linux, and cross-host bit-for-bit identity is not claimed. Integrate with the coordinator's NAS history merge before final repository acceptance; retain the original M0 evidence as historical evidence rather than relabeling it as a result of this commit.
