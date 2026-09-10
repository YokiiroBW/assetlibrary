# Restricted Explorer observer v2 — query-handle correction

This is the minimal successor to frozen observer `749cd50985ba51895c410ea1e8fb1d3335c903767dbf87e88b60f846645837e0`. The old directory is unchanged. V2 has not attached to Explorer or any existing process during preparation.

The retained query/liveness process handle now requests `PROCESS_QUERY_INFORMATION | SYNCHRONIZE` (`0x00100400`). Root's separate, read-only test on the original owned Explorer and both new synthetic controls showed that the previous `PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE` (`0x00101000`) opens successfully but `CheckRemoteDebuggerPresent` fails with Win32 error 5. The API contract requires checking its success return before interpreting the output BOOL; failure details come from GetLastError. This is a local access-mask finding, not evidence that the original target had a debugger. See [Microsoft's API contract](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-checkremotedebuggerpresent).

Query failure now produces `querySucceeded=false`, the captured Win32 error and `HRESULT_FROM_WIN32`, and `debuggerPresent=null`. A successful query with `debuggerPresent=true` has a separate rejection stage. Post-detach checks use the same explicit query result. This change does not request ALL_ACCESS, VM_WRITE, privilege adjustment, or elevation for the retained handle; DbgEng's existing invasive-attach and software-breakpoint requirements are unchanged.

## Commands and plan location

```powershell
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug-v2\build.ps1'
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug-v2\bin\ExplorerTargetObserver.exe' --normal
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug-v2\bin\ExplorerTargetObserver.exe' --cancel

# Real run only after coordinator review and with the unique GUI owner:
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug-v2\bin\ExplorerTargetObserver.exe' --run-plan <plan-id>
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-target-debug-v2\bin\ExplorerTargetObserver.exe' --cancel-plan <plan-id>
```

Publish a fresh `<plan-id>.plan` in this V2 directory. It is exactly eleven ASCII/UTF-8-without-BOM lines in this order; placeholders must be replaced with the GUI owner's actual identity. The ID is 32 lowercase hexadecimal characters. A plan is single-use.

```text
AssetLibrary.Explorer.DebugPlan.v1
id=<plan-id>
profile=official
pid=<decimal-PID>
creationFiletime=<decimal-creation-FILETIME>
hwndHex=<hex-HWND-without-0x>
ownershipStartFiletime=<epoch-recorded-before-owner-created-process>
issuedFiletime=<current-UTC-FILETIME>
expiresFiletime=<issued-plus-1200000000>
durationSeconds=60
purpose=owned-explorer-namespace-diagnostic
```

`proof` is the only other profile. Profile CLSIDs, pre-attach and first-event admission, unique frame/desktop exclusion, exception handling, three API entry/return pairing, CLSCTX retention, limits, cancellation event naming, and cleanup behavior are unchanged from [the frozen v1 protocol](../explorer-target-debug/README.md). Wait for `capture_ready` (PID/creation/profile/tick) before the one approved Browse. Cleanup does not depend on frame visibility or unexpired plan state. Capture duration is at most 60 seconds; native Attach/Detach still lack a proven hard timeout, and the pre-first-event/crash windows are not guaranteed safe. No matching API event does not prove no attempted activation.

## V2 verification

Build passed `/W4 /WX /MT /O2` using installed MSVC 14.44.35207 / SDK 26100, without downloads. `normal-final.jsonl` and `cancel-final.jsonl` both passed. Each run reopens its newly created synthetic child with the old mask and reproduces error 5, then **closes the CreateProcess full-access process handle**, reopens using the exact V2 mask, checks the same PID/creation, and uses this restricted handle throughout observation and post-detach verification. Thus the regression is not hidden by CreateProcess's original handle rights.

Both runs observed the three fixed APIs with CLSCTX=1 and actual returns `0x80040154`, preserved the target's own first-chance handler, removed all six breakpoints, verified zero remaining breakpoints, detached successfully, confirmed continued heartbeats and internal/external no-debugger state, and cooperatively exited the child with code 0. Cancel exercised 14 finite wait timeouts; EXIT was not observed as E_PENDING, while ACTIVE pause and cleanup passed. Unchanged pure plan/parser checks were not rerun.

Only newly spawned synthetic processes were used. No real plan, GUI operation, registry/policy mutation, UAC, ETW, debugger kill, or target termination was performed. The real handle's successful pre-attach query and post-detach query remain to be confirmed by the coordinator-controlled real run. Final source/binary/log hashes and independent synthetic cleanup checks are recorded alongside this file.
