# Fixed binding observer — requested IID metadata

This is the minimal metadata successor to frozen observer `822fee1961946116b7e6b8babc5907f81e7c569b30d674ac2506cbf823037619`. After the existing exact approved-PIDL match and existing IID read, it formats that IID using `StringFromGUID2` and emits `requestedIid` in canonical brace/hyphen form. `iidCategory` and `callId` remain present. No other target memory, PIDL, process, or address read was added.

Only `Observer.cpp`'s GUID formatting/output and the task-directory literals in `Plan.h` / `build.ps1` changed. `changes.patch` contains the exact source diff. Shared data, fingerprinting and reference-target source are byte-identical to the prior tool. PIDL matching, candidate RVA/fingerprints, plan schema, official-only scope, capture limits, return handling and cleanup are unchanged.

```powershell
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug-iid\build.ps1'
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug-iid\bin\BindObserver.exe' --normal
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug-iid\bin\BindObserver.exe' --cancel

# Coordinator / unique GUI owner only, after review; not run in preparation:
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug-iid\bin\BindObserver.exe' --run-plan <new-plan-id>
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug-iid\bin\BindObserver.exe' --cancel-plan <new-plan-id>
```

The unchanged eleven-line `official` plan must be stored as `<id>.plan` in this **new IID directory**. Exact plan/ready/cancellation/lifecycle instructions and limitations are in the [frozen base protocol](../explorer-bind-debug/README.md). No arbitrary IID input is accepted: this field describes the interface already requested by a matching target call. No arbitrary process/module/RVA/command input was introduced.

Build passed x64 `/W4 /WX /MT /O2` with installed MSVC 14.44.35207 / SDK 26100. New `normal-final.jsonl` and `cancel-final.jsonl` each captured one entry with `requestedIid={000214E6-0000-0000-C000-000000000046}` (`IShellFolder`) and its corresponding `callId=1` return, S_OK/non-null. External log checks verified the exact IID and callId association. Both removed the two breakpoints, verified zero remaining, detached successfully, confirmed continued target heartbeats/no debugger, and cooperatively exited the reference child with code 0. Cancel exercised 14 finite waits.

The previous real-host observation, supplied by the coordinator, had two matched calls with category `other`: `0x80070490/null` and `0x80004002/null`, with different bind-context presence. Those are observations from the **prior real-host run**, not new synthetic coverage. The present synthetic controls exercise IShellFolder/S_OK/non-null only; they do not identify those earlier unknown IIDs or prove the requests were optional. No real mode or GUI operation was run here.

All previous directories and real records remain untouched. This stage does not change the reference-vs-host limitation, opaque-PIDL representation limitations, or unproved native Attach/Detach hard-deadline/crash cleanup cases. The final hashes, exact diff and independently checked reference-child cleanup are stored alongside this file.
