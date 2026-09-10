# Restricted Explorer COM observer — prepared, real attachment not executed

This temporary native observer accepts only the fixed modes below. Real admission selects exactly one short-lived plan and one fixed profile: `official` = `{BA16CE0E-728C-4FC9-98E5-D0B35B384597}`, or `proof` = `{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}`. There is no arbitrary command, executable, module path, CLSID, PID-only attach, or automatic elevation option. The earlier `explorer-debug-control` directory remains unchanged.

## Commands

The installed MSVC 14.44.35207 / SDK 26100 build is `build.ps1`. It compiles x64 `/W4 /WX /MT /O2`, uses SDK WRL headers, and loads only System32 `dbgeng.dll`. No downloads or debugger command strings are used.

```powershell
# Synthetic-only modes used during preparation:
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug\bin\ExplorerTargetObserver.exe' --self-test
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug\bin\ExplorerTargetObserver.exe' --normal
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug\bin\ExplorerTargetObserver.exe' --cancel

# Real modes: coordinator decision and GUI-owner coordination required first.
# The argument is a 32-character lowercase hexadecimal plan ID, never a path/PID.
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug\bin\ExplorerTargetObserver.exe' --run-plan <plan-id>
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug\bin\ExplorerTargetObserver.exe' --cancel-plan <plan-id>
```

Real capture is limited to 60 seconds after its GO request. Named-event cancellation and the deadline use the validated finite-wait / GO-restoration / ACTIVE-interrupt cleanup path. `--cancel-plan` only signals `Local\AssetLibrary.TargetDebug.<plan-id>`; it never terminates a process. The real target is left running after detach. Console closure/crash is not the tested cancellation path.

## Exact plan format

Publish `<plan-id>.plan` directly in this directory as ASCII or UTF-8 without BOM, at most 2 KiB. These eleven lines and this order are required. LF and CRLF are accepted; an optional final newline is accepted. Angle-bracket values below are placeholders, not runnable values.

```text
AssetLibrary.Explorer.DebugPlan.v1
id=<32-lowercase-hex-plan-id>
profile=official
pid=<decimal-PID>
creationFiletime=<decimal-process-creation-FILETIME>
hwndHex=<hex-HWND-without-0x>
ownershipStartFiletime=<decimal-FILETIME-recorded-BEFORE-owner-created-process>
issuedFiletime=<decimal-current-UTC-FILETIME>
expiresFiletime=<decimal-issued-FILETIME-plus-1200000000>
durationSeconds=60
purpose=owned-explorer-namespace-diagnostic
```

`ownershipStartFiletime <= creationFiletime <= issuedFiletime <= now < expiresFiletime`; the ownership epoch must be within ten minutes and expiration no more than 120 seconds after issue. Unknown/reordered/extra fields, alternate profiles, malformed numbers, zero HWND, and other durations are rejected. The filename ID must match the ID inside the plan. Input handles deny writes/deletes, final paths are checked, and plan/root reparse points are rejected. A persistent `<id>.used` marker makes a plan single-use once admission succeeds. A new attempt needs a new plan ID.

The GUI owner supplies and authorizes the owned-window identity and records the epoch before creating the new process. The tool verifies those facts mechanically; it cannot infer human ownership from a same-user PID or identify who authored a tab. Do not use a plan for a restored user window or shared process. The owner should leave its unique frame on This PC, launch the observer, wait for `capture_ready`, and then perform the one approved Browse action. `capture_ready` is emitted after all three entry breakpoints are installed and GO is requested, with PID, creation FILETIME, profile, and monotonic `tick`. API entries/returns and cleanup stages also carry ticks.

## Admission and cleanup boundaries

Immediately before attach and in the first create-process debug event, admission requires the held process handle's PID/creation time and liveness, exact `%SystemRoot%\explorer.exe`, same user SID/session, a non-null desktop Shell window with a different PID, exactly one Cabinet/Explore frame in the target process, the planned HWND being visible/uncloaked/root, and no other visible top-level target window. Hidden extra frames also reject admission. First-event process safety options are set/read back after basic PID identity and before UI/expiry validation.

Cleanup checks only the held process's PID/creation identity. Expiration, a newly displayed modal, or changed frame visibility cannot skip removal/detach. Safety flags `ONLY_THIS_PROCESS` and `DETACH_ON_EXIT` are read back. No target or debugger termination routine exists. All owned diagnostic breakpoints are removed, their count is checked as zero, and `DetachProcesses` must succeed. Real success also requires the same process alive and `CheckRemoteDebuggerPresent=false` after detach.

Native Attach/Detach and arbitrary engine calls have no proven hard timeout. The interval before the first debug event's safety options are confirmed remains an abrupt-failure risk. Software-breakpoint recovery after an engine crash/deadlock or forcibly closing the debugger was not tested or guaranteed. No watchdog kills the debugger and calls that cleanup success. Access denial fails closed; the tool does not enable SeDebugPrivilege or request UAC.

## Actual coverage

The fixed system `combase` LoadModule event establishes a paused, resolved target-module context. Only `CoCreateInstance`, `CoCreateInstanceEx`, and `CoGetClassObject` exports are used. API addresses are resolved in the target through DbgEng; synthetic controls cross-check them against addresses reported by the owned child.

At entry the observer reads only the 16-byte CLSID. Only matching profile calls retain API, profile, PID, CLSCTX, and tick. CLSCTX is read from EDX for CoGetClassObject and R8D for the two Create APIs. Matching calls are paired with actual HRESULT returns using thread ID, return site, and expected stack pointer. Limits are 4,096 total API entries, 128 matches, 32 pending calls, and 64 return sites. Unmatched payloads and all paths/stacks/addresses are discarded. A pending return at observation end makes the observation incomplete rather than inventing a result.

Attach uses `DEBUG_ATTACH_INVASIVE_NO_INITIAL_BREAK` and the known module event, so an assumed second initial exception is unnecessary. Only the explicitly armed ACTIVE interrupt breakpoint is handled by the observer. Other first-chance exceptions use `GO_NOT_HANDLED` so target handlers run; second-chance failures remain target/OS decisions and stop successful observation.

This does not cover all internal COM activation paths or expose IActivationFilter/namespace bind contexts. No matching event does not prove that the class was never considered or activation was never attempted. DLL loading is not implemented in this milestone.

## Prepared evidence

- `offline-01.jsonl`: 19 pure plan-parser/ID/frame-predicate controls passed. These are not nineteen real GUI or process-identity scenarios; their input logic remained unchanged through final capture corrections.
- `normal-final.jsonl` and `cancel-final.jsonl`: newly created synthetic targets only; each observed three entries, three returns `0x80040154`, CLSCTX=1, and three discarded nonmatches. All six breakpoints were removed, count zero and detach succeeded; target heartbeats/internal+external no-debugger checks passed before cooperative exit 0. Cancel exercised 14 finite wait timeouts; EXIT did not manifest as E_PENDING, while the ACTIVE pause/cleanup did pass.
- Both synthetic runs include an exact creation-mismatch check against the shared `PinnedProcessIdentity` predicate, a correct-identity positive check, and rejection of the synthetic executable by real-image admission. No real frame admission was executed.
- The child raises a fixed first-chance exception before its API calls. Its own SEH handler must execute for final success, proving that observation did not swallow that exception.
- `normal-01.jsonl` and `normal-02.jsonl` are preserved tool-development failures (second-initial-break assumption, then premature export resolution), not Windows product results. `normal-03.jsonl` is an earlier three-API success before the final explicit exception control.

No `--run-plan`, real Explorer attach, GUI/registry/policy change, elevation, or ETW capture was executed. Final hashes and synthetic process cleanup evidence are in `manifest.json` and `cleanup-check.json`.
