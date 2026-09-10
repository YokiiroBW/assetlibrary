# Fixed cache consumption candidate (preparation only)

Based on frozen B41 (`explorer-attributes-debug`), with one additional fixed
`windows.storage.dll` breakpoint at RVA `0x1194C3`. The public attributes
entry and consumer point share the original 4096-hit limit. No registry hook
or second internal point is installed.

`PendingCall` retains its original input mask. Only a live same-thread outer
match with all fixed stack, R14, private-return and official GUID checks can
emit four cache DWORDs. Exception and ExitThread callbacks invalidate cache
scope without removing the existing return bookkeeping. Output uses
`outerCallId` to join the public `callId`. Internal layout is specific to the
locked binary. Zero values or no matching point are not registry conclusions.

Compile and pure controls completed; see `validation.json` and raw files.
The non-debugging official PIDL preflight failed with 0x80070057 while the
fixed CLSID was absent in all four checked per-user/per-machine registry
views. No debugger was started. Synthetic normal/cancel are pending the
coordinator's fixed official registration period; this helper never registers.

Commands from this directory (no elevation):

```powershell
.\build.ps1
.\bin\CacheAttributesObserver.exe --self-test
.\bin\CacheAttributesReferenceTarget.exe --check-official-pidl
```

After the owner supplies the approved registration period, the fixed
`--normal` and `--cancel` modes create only the sibling reference target:

```powershell
.\bin\CacheAttributesObserver.exe --normal
.\bin\CacheAttributesObserver.exe --cancel
```

The existing real `--run-plan <id>` / `--cancel-plan <id>` interface is retained
with the same 11-line official-only plan in this new directory. Real use is
not validated by this preparation and requires coordinator approval. Native
Attach/Detach are not promised to have a hard timeout. Cancellation must be
cooperative; killing the debugger is not an accepted cleanup method.
