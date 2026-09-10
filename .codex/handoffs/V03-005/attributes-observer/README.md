# Fixed GetAttributesOf observer — reference tests complete

This temporary observer replaces the IID-enabled BindToObject entry with a single `IShellFolder::GetAttributesOf` candidate. It reuses the reviewed plan/admission, exact opaque-PIDL matcher, callId and thread/stack return pairing, correct query-handle mask, module identity checks, ready/cancel and cleanup lifecycle. It does not add a second binding breakpoint. No existing process was attached during preparation.

The fixed candidate is System32 `windows.storage.dll`, RVA `0x1188E0`, first-32-byte SHA256 `991cf0c259bf70e99b9b2d34fe48ea5e4675e48cfd1c7bfb55eac5f3e612ca64`. File SHA256 remains `22aff66094747a65ea1b5390e60dd423cf3bfd897781a6347ccf7f6234d0388b`; AMD64 PE size/timestamp and executable-code-range checks are unchanged. No module/RVA/hash override or symbol download exists.

## Parameter and output scope

At entry the x64 COM ABI is `this=RCX`, `cidl=EDX`, `apidl=R8`, `SFGAOF*=R9`. The filter reads cidl first. Anything except one item is discarded before reading the array or any item; it never attributes a multiple-item aggregate result to one child. For cidl=1, only apidl[0] is read and compared as complete bounded opaque PIDL bytes. Only after an exact match is R9's 32-bit input mask read and that same mask address saved for the actual return. The old IID/pbc/stack+40 output-slot reads are removed.

Records contain callId, input SFGAOF mask, actual HRESULT, output mask, `outputMaskValid=SUCCEEDED(hr)` and tick. No pointers, private payloads or extra array entries are output. If the input mask did not request `SFGAO_FOLDER`, an output without FOLDER **does not establish that the item is not a folder**. Failed HRESULTs make the returned mask invalid regardless of its numeric bits. Successful tool cleanup is not a product acceptance result.

## Commands and plans

```powershell
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-attributes-debug\build.ps1'
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-attributes-debug\bin\AttributesObserver.exe' --normal
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-attributes-debug\bin\AttributesObserver.exe' --cancel

# Coordinator and unique GUI owner only, after review; not executed here:
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-attributes-debug\bin\AttributesObserver.exe' --run-plan <new-plan-id>
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-attributes-debug\bin\AttributesObserver.exe' --cancel-plan <new-plan-id>
```

Use a fresh official-only eleven-line `<id>.plan` in this new directory. The [frozen plan/ready/cancellation protocol](../explorer-bind-debug/README.md) is unchanged. Real PIDL construction uses the same fixed absolute MyComputer/BA16 SHParseDisplayName route as Browse, validates its parent with IL APIs and clones its relative leaf. This branch was not run in this milestone. Wait for `capture_ready` before the one approved Browse. Native Attach/Detach/crash limitations, single-use plans and cleanup independent of modal/visibility/expiration are unchanged.

## Executed verification

Build passed x64 `/W4 /WX /MT /O2` with installed MSVC 14.44.35207 / SDK 26100. `normal-final.jsonl` and `cancel-final.jsonl` each start only a new `AttributesReferenceTarget.exe` and:

- Verify public CINTERFACE GetAttributesOf slot 9, fixed module/file/code fingerprints, and exact equality of remote candidate address with the child's actual vtable method pointer.
- Create one legal system-volume-root child PIDL through public APIs. The positive call is cidl=1, input `FOLDER|FILESYSTEM|BROWSABLE|HIDDEN|NONENUMERATED` (`0x68180000`), actual S_OK/output `0x60000000` (`FOLDER|FILESYSTEM`).
- Make a legitimate cidl=2 call using two references to the same single child PIDL. The target independently confirms its aggregate call completed, while the observer discards it before any array/item read. A multilevel Windows PIDL is not used as a PCUITEMID_CHILD control.
- Externally verify one matching entry and return share callId=1, exact input/output masks, successful HRESULT and outputMaskValid=true; one cidl=2 control is discarded.
- Reopen the child's process handle using the exact real mask, preserve its own first-chance exception handler, remove two breakpoints and verify zero remaining, detach, confirm advancing heartbeats plus internal/external no-debugger checks, then cooperatively exit the child 0. Cancel exercised 14 finite wait timeouts.

No real Explorer/BA16 attribute query, existing-process attachment, registration, GUI, directory enumeration, asset-content read, policy change, UAC or forced termination was performed. These results are reference-child debugger evidence, not host attribute evidence. No-FOLDER-input, failed-HRESULT, null-mask and interleaved multiple matching calls were not exercised; their handling has source review only. Unchanged pure plan checks were not rerun. A zero-hit result still does not prove the host never requested attributes or used this parent implementation.
