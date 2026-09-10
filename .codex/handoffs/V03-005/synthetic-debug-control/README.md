# Synthetic-only DbgEng control verification

This temporary experiment proves a fixed API entry/return observation and orderly detach against a newly created, owned synthetic process. It has no CLI accepting a PID, executable path, debugger command, or script. **It cannot attach to Explorer or an existing process.** Production CLSID registration, GUI, policy, UAC, and ETW are untouched.

## Build and run

Run `build.ps1` in this directory. It uses the installed x64 MSVC 14.44.35207 compiler and Windows SDK 10.0.26100.0, `/W4 /WX /MT /O2`, with no downloaded dependencies. Compiler temporary files stay in this directory. WRL is a Windows SDK header dependency. The observer loads `dbgeng.dll` only through `LOAD_LIBRARY_SEARCH_SYSTEM32`; debugger output, network symbol paths, and managed debugging support are disabled.

```powershell
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-debug-control\build.ps1'
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-debug-control\bin\SynthObserver.exe' --normal
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-debug-control\bin\SynthObserver.exe' --cancel
```

Each run creates only the sibling `SynthTarget.exe`, using an explicit inherited-handle list for anonymous task-owned IPC. It verifies the returned process handle/PID/creation time, exact image path, target readiness, and initial lack of a debugger. Debug events verify the same process identity. The target never registers a COM class.

The fixed matching synthetic CLSID is `{89100E21-15D2-4F41-87D6-CB36F67D86CC}`. The target makes a nonmatching call and a matching `CoGetClassObject(CLSCTX_INPROC_SERVER)` call. The observer reads only the entry's 16-byte CLSID, a return address from that owned call's stack, and the matching return HRESULT. A thread-scoped return breakpoint records the actual target result `REGDB_E_CLASSNOTREG` (`0x80040154`). Nonmatching identities/payloads are discarded; only their count remains. The target independently reports both call results through the owned shared mapping.

## Verified lifecycle

The first target event sets and reads back `DEBUG_PROCESS_ONLY_THIS_PROCESS | DEBUG_PROCESS_DETACH_ON_EXIT` before diagnostic breakpoints are enabled. A second readback confirms the options and a single debuggee. Normal mode stops at the matching return. Cancel mode requests cancellation from a separate timer thread after 1.5 seconds while the target continues its heartbeat loop.

`WaitForEvent(0, 100)` uses a finite wait. After `S_FALSE`, the loop restores `DEBUG_STATUS_GO` before waiting again. This was necessary with the installed System32 engine: otherwise it reported BREAK while its target context remained inaccessible. Cancellation requests `DEBUG_INTERRUPT_EXIT`, then cleanup obtains an actual stopped event with `DEBUG_INTERRUPT_ACTIVE`, verifies the paused PID/creation time, removes both breakpoints, verifies zero remaining breakpoints, and calls `DetachProcesses`.

After detach, success requires the same target still being alive, at least three more heartbeats, the target's own `IsDebuggerPresent` reporting false, and external `CheckRemoteDebuggerPresent` reporting false. Only after those checks does the observer signal the synthetic target to exit normally and verify exit code zero. No TerminateProcess/TerminateProcesses or debugger-kill cleanup path exists.

Final `normal-final.jsonl` and `cancel-final.jsonl` both pass and correspond to the binaries in `manifest.json`. Both have one matching entry and return; cancellation exercised 14 finite wait timeouts. The EXIT request succeeded but did **not** return `E_PENDING` in this run (`exitInterruptObserved=false`); the subsequent ACTIVE pause and cleanup were verified. This is not a claim that EXIT alone made the target inspectable.

## Preserved exploratory results

- `normal-01.jsonl`: pre-attach failure. Setting process options before the first target event returned `0x8000FFFF`; no attachment occurred. Its old `cleanupVerified=true` field described no-op cleanup and is not acceptance evidence. Target exited normally.
- `normal-02.jsonl`: earlier normal-path success, before the final cancellation correction.
- `cancel-01.jsonl` through `cancel-04.jsonl`: correctly failed because breakpoint removal remained unavailable. Detach and post-detach target survival still succeeded, but were not misreported as complete cleanup.
- `cancel-05.jsonl`: cancellation success after restoring GO between finite waits, before the final explicit paused-identity assertion.

Only the final two logs are acceptance results for the current source/binary hashes. Earlier source/binary revisions are not represented as identical to this frozen build.

## Limits

This experiment does not establish Explorer compatibility, a target's activation-filter decisions, class substitution, namespace bind contexts, or full COM/loader coverage. No real Explorer process was attached. The observer remains synthetic-only; real target support requires a separate coordinator decision and scoped implementation.

`AttachProcess` and `DetachProcesses` have no hard timeout parameter. Finite event waits and a bounded capture loop are not a hard bound on every native engine call. The interval between attachment initiation and the first event's verified DETACH_ON_EXIT option cannot be claimed safe against arbitrary debugger crashes. Abrupt debugger termination, engine deadlock, OS failure, and automatic cleanup of software breakpoints after such failures were **not** tested or guaranteed. No watchdog kills the debugger and calls that successful cleanup. The target has a cooperative 45-second deadline, which cannot run while suspended.

The ONLY_THIS_PROCESS and DETACH_ON_EXIT flags were verified by readback; no extra child-process or debugger-crash experiment was added. The success contract is explicit removal plus detach and continued target execution in the normal and requested-cancel cases. Compilation and these two real synthetic controls passed without elevation.

Primary API references: [execution model](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/debugging-session-and-execution-model), [process options](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/debug-process-xxx), [WaitForEvent](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugcontrol-waitforevent), [SetInterrupt](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugcontrol-setinterrupt), [DetachProcesses](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugclient-detachprocesses).
