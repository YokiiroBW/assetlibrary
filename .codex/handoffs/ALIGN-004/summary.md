# ALIGN-004 implementation checkpoint

Status: partial pending native process/provenance verification.

Reconciles the NAS service guardrails with the current Windows evidence: retains read-only entry points, uppercase CLI and build-info; stages the complete artifact with LocalService ACL and installation identity; validates service/registry ownership and readback; treats query failures as errors; preserves referenced staging and nonempty data during cleanup. Native arguments use $Arguments, avoiding PowerShell's automatic $Args binding bug.

The 9 adapter regressions passed, including actual PowerShell argument forwarding, query failure, identity mismatch and temporary-file rollback. No SCM/HKLM writes or Shell registration were executed. No product contract, dependency, database or release gate changed.

Native process and deterministic publishing verification follows this clean implementation commit. The final handoff will reference that commit and record the remaining evidence.
