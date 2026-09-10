# Fixed CLSID RegOpenKeyExW observer

This diagnostic observes only the standard HKEY_CLASSES_ROOT and exact
`CLSID\{BA16CE0E-728C-4FC9-98E5-D0B35B384597}\ShellFolder` (ASCII case
insensitive, complete terminator required). Root is filtered before reading
the subkey; mismatches stop character reads immediately. There is one API
entry breakpoint, 4096 entry-hit budget, 128 matched calls, 32 pending calls,
and 64 thread-scoped return sites. No arbitrary key, API, module or profile
is accepted. Only `official` plans are valid.

Initial advapi32 GetProcAddress returned a local IAT jump stub. The second
lookup verified that its fixed IAT target equals the public
api-ms-win-core-registry-l1-1-0 RegOpenKeyExW export: KernelBase RVA 2BBF0.
The current-process entry bytes equal the on-disk file. Location-01 and the
original LocatorAdvapi source/binary retain the first observation as history.

The observer records matched callId, ulOptions, samDesired, low-32 LSTATUS
and successful-output presence. Only LSTATUS==0 permits reading PHKEY;
failed outputs remain uninspected and are logged as null/unknown, not zero.
The observer never closes a target registry handle. The reference child
closes only handles returned successfully by its own public RegOpenKeyExW.

In real mode, fixed KernelBase and SHCORE files are locked and hashed before
attach. Initial module events verify their paths and PE identities. SHCORE's
fixed IAT RVA AAED8 must equal the selected entry before ready; missing or
mismatched mappings fail. The reference only checks its APIset/advapi paths;
it does not prove the real Explorer importer mapping. No SHCORE ordinal is
called and no private/cache/PIDL breakpoint is retained.

Build and 39 pure predicate/plan checks passed. Newly created reference
normal/cancel runs each observed status 2 and discarded a non-target call.
Both removed 2 breakpoints, read back zero, detached, confirmed same-child
heartbeats/no-debugger, and exited cooperatively. The final child identities
are absent. See validation.json and original JSONL. Registered success and
real SHCORE mapping remain for coordinator/owner validation.

```powershell
.\build.ps1
.\bin\ClsidKeyOpenObserver.exe --self-test
.\bin\ClsidKeyOpenObserver.exe --normal
.\bin\ClsidKeyOpenObserver.exe --cancel
```

These normal/cancel modes create only the sibling reference target. They do
not register keys. The real command is `--run-plan <32-lowercase-hex-id>`;
cooperative cancellation is `--cancel-plan <id>`. The same 11-line plan must
be inside this exact directory, with duration60, official profile, current
owned Explorer PID/creation/HWND, and an ownership epoch recorded before its
creation. Existing user/desktop Explorer processes fail admission. The
normal identity, same-session, owned-frame, expiry and process-access gates
from B41 remain. Cleanup does not depend on a frame remaining visible.

Cached attributes may avoid a new registry open; no hits are not evidence
of no historical attempt. Attach/Detach are native calls without a hard
completion guarantee. Use cooperative cancellation and verify cleanup;
never kill a debugger or target and claim successful recovery.
