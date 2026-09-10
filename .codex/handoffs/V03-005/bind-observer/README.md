# Fixed MyComputer binding observer — reference controls complete

This temporary observer replaces the three general COM breakpoints with **one fixed candidate IShellFolder::BindToObject entry**. It reuses the frozen v2 plan admission, query-handle mask, ready signal, cooperative cancellation and detach checks. Earlier directories are unchanged. This preparation attached only to new owned reference children; real Explorer and the BA16 PIDL branch have not run.

## Fixed identity and scope

The sole candidate is System32 `windows.storage.dll`, RVA `0xABC70`. Before attachment the observer opens the exact system file read-only, rejects reparse/path aliases, retains its lock, and verifies SHA256 `22aff66094747a65ea1b5390e60dd423cf3bfd897781a6347ccf7f6234d0388b`. No module/RVA/hash/command override is accepted.

At the target's matching module event it requires the known system image path, AMD64 PE headers, image size 8798208, timestamp 2800646882, the candidate and its 32-byte prefix inside an executable code section, and prefix SHA256 `d191a9c6322a212351ae2247519f3cbaa2444746feb1b18f4441b5e9c728a162`. In reference mode, the remote candidate address must also equal the actual vtable method pointer reported by that child. Addresses and image paths are not printed. There are no symbol downloads or general COM entry breakpoints.

The expected PIDL is a complete opaque byte sequence limited to 2 KiB. The remote matcher follows only the documented SHITEMID.cb chain, at most 64 items / 2 KiB, to locate the terminating zero length. Empty, shorter and longer sequences are rejected before attempting an expected-length payload read. Only equal complete lengths are compared byte-for-byte. It never scans for a GUID or interprets private payload fields. Other PIDLs are discarded with a count.

Only an exact match retains IID category (`IShellFolder`, `IShellFolder2`, `IUnknown`, or `other`), whether `pbc` is present, actual return HRESULT, output-parameter presence, returned-interface nullness, and monotonic tick. Every matching entry gets a monotonically increasing `callId`, saved in the pending call and emitted unchanged on return. Return pairing uses the actual thread, return site and expected stack pointer; thread/addresses are not output. Limits remain 4096 total entries, 128 matches, 32 pending calls and 64 return sites. Pending returns at completion make the observation incomplete.

## Commands

Build uses installed MSVC 14.44.35207 / SDK 26100, x64 `/W4 /WX /MT /O2`, Windows SDK/OS libraries only. Compiler outputs remain here.

```powershell
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug\build.ps1'
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug\bin\BindObserver.exe' --self-test
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug\bin\BindObserver.exe' --normal
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug\bin\BindObserver.exe' --cancel

# Coordinator/unique GUI owner only, after review; not executed in preparation:
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug\bin\BindObserver.exe' --run-plan <plan-id>
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-debug\bin\BindObserver.exe' --cancel-plan <plan-id>
```

The real mode supports **official only**. A fresh `<plan-id>.plan` goes directly in this directory, as the same eleven ASCII/UTF-8-without-BOM lines. Fields below in angle brackets require the GUI owner's actual values.

```text
AssetLibrary.Explorer.DebugPlan.v1
id=<32-lowercase-hex-id>
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

Plan life is at most 120 seconds after issue, ownership epoch at most ten minutes old, and a `.used` marker prevents reuse. Frame/user/session/SystemRoot-explorer/desktop exclusion and before-attach/first-event checks are unchanged. First-event safety options are set before UI/expiry revalidation. Cleanup depends only on held process identity, not modal windows, visibility or plan expiration. The retained process handle is `QUERY_INFORMATION|SYNCHRONIZE`; query failure and actual debugger presence remain distinct.

For real PIDL construction the observer uses precisely `SHParseDisplayName("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}\\::{BA16CE0E-728C-4FC9-98E5-D0B35B384597}")`, matching the approved Browse route. It clones/removes the last item to verify the parent equals `FOLDERID_ComputerFolder`, and uses `ILFindLastID`/`ILClone` for the relative leaf. It does not explicitly bind to BA16, decode its PIDL layout or enumerate anything. This branch requires the owner-maintained registration and is **pending real validation**. The local reference parent is not treated as the actual Explorer parent.

Owner should remain on This PC until `capture_ready` appears, then issue the single approved Browse. Real capture lasts at most 60 seconds of the capture loop. Cancellation signals the fixed task event for this plan ID. Native Attach/Detach still have no proven hard deadline; the pre-first-event safety-option window and crash/deadlock breakpoint recovery remain limitations. No force-kill or automatic elevation exists.

## Executed verification

`offline-01.jsonl` passed 19 pure plan/ID/frame predicate checks, including rejection of the previously supported proof profile. These are not real GUI tests.

Final `normal-correlated.jsonl` and `cancel-correlated.jsonl` each started a new `BindReferenceTarget.exe`. It builds a fixed MyComputer reference and system-volume root/longer Windows-directory relative PIDLs using public APIs, without enumeration or file-content reads. It keeps the reference alive and calls the same vtable pointer: longer Windows PIDL is the nonmatching control; system root is the matching S_OK/non-null control.

Both final runs verified exact module fingerprint and remote vtable address, captured one matching entry and actual S_OK/non-null return, discarded one longer PIDL, and preserved the target's own first-chance handler. Two breakpoints were removed, remaining count was zero, detach succeeded, heartbeats and internal/external no-debugger checks passed, and the child cooperatively exited 0. The actual query mask was reopened on the child rather than retaining CreateProcess's full process handle. Cancel exercised 14 finite wait timeouts; EXIT did not manifest as E_PENDING, while ACTIVE stopped-event cleanup passed.

The final logs also verify matching entry/return `callId=1`. Failure HRESULTs, null returns, and interleaved multiple matching calls were not exercised; those paths have source review only.

`normal-01.jsonl` / `cancel-01.jsonl` are earlier successful reference runs before the unexecuted real-PIDL builder was aligned with the exact Browse route. `normal-final.jsonl` / `cancel-final.jsonl` retain the later pre-callId run from observer SHA256 `929db242db4cd9e32c46522dcf24b44c10c35a9123cc315676ad903cf8a20a0a`; they were not renamed or presented as new runs. `manifest-pre-callid.json` describes that earlier artifact state, not the current paths' bytes. Current source/binary hashes and correlated logs are in `manifest.json`. All reference children are checked as ended in `cleanup-check.json`.

These are genuine debugger hits **inside the reference child**, not inside Explorer. The host might use another parent implementation or a different PIDL representation. No match does not prove no binding or activation attempt. A successful observation/cleanup result does not mean the product namespace works. No actual BA16 parsing, registration, GUI operation, existing-process attachment, policy change, UAC or ETW was performed in this milestone.
