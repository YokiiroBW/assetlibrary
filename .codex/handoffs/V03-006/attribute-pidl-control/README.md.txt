# Same-condition PIDL attribute control — prepared, not executed

This is a temporary independent Shell query tool. It does not open a GUI, attach a debugger, explicitly bind the BA16 sample, change association settings, decode private PIDL layouts or patch attribute values. The same immutable official DLL and four-root STA guard are reused.

`enum-first` creates one stock MyComputer parent, completes ordinary FOLDERS|NONFOLDERS enumeration before parsing the target, then clones the leaf of the same absolute SHParseDisplayName route used by Browse. Exactly one canonical matching enumerated child is required. Five masks (28180000, 20000000, 40418000, 40000000, 2044007F) each receive enum→parsed and parsed→enum queries: 20 total, resetting the input every time.

`parse-first` starts a separate process and parent. Before enumeration it parses the target and queries 20000000, 40418000, 40000000, 2044007F, 28180000, 20000000. Only then does it enumerate, match, and perform the same 20 paired queries. These are fixed orders, not a comprehensive cold-cache experiment: process creation does not prove that shared system caches are cold.

Only the target pair's complete opaque lengths and SHA256 are persisted, with whole-byte equality before/after the paired queries. Each source's fingerprints must remain unchanged. In parse-first, the parsed source is additionally fingerprinted before/after the first six queries and compared across enumeration. No other item names or payloads are emitted. Enumeration stores at most 128 single-child PIDLs of at most 16 KiB; at most one extra Next establishes end-of-list at that limit, otherwise the process rejects the incomplete enumeration. All Shell calls are contained in an owned standalone process with an external 10-second timeout.

`run.ps1` defaults to `validate`: it checks frozen guard/DLL/probe hashes and requires the single-use output directory to be absent. It does not invoke the probe or guard. Explicit `run` creates `.runtime/explorer-live/20260910-attribute-pidl-control`, copies the unchanged guard, starts it hidden, waits at most 45 seconds for complete registration JSON and successful notification, checks four CU owners plus four absent LM roots, and runs A then B in separate processes. At least 30 seconds of the 600-second lease must remain before each process. It verifies the sequence and fingerprint records without assuming any specific output attribute bits. Failure records remain in place; a failed A stops B. The standalone probe alone may be terminated on timeout. Finally it requests guard stop, waits up to 10 seconds, and verifies guard exit, eight absent roots, dual notification, and probe exit. It never kills the guard or Explorer. A guard cleanup timeout is reported pending for inspection.

## Preparation checks actually completed

- Release x64 `/MT /W4 /WX /permissive- /analyze` build passed. Initial deleter pointer qualifier warnings were corrected by reusing the existing PIDLIST_ABSOLUTE custom pointer declaration; the original failed build log is preserved.
- PowerShell AST parsing passed.
- Default `validate` passed with `ProbeExecuted=false`, `RegistrationStarted=false`, and no run directory.
- No actual Shell query results or cleanup results exist for this proposed experiment yet.

## Exact commands

From this worktree:

```powershell
& 'C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' -S .runtime/explorer-attribute-pidl-control -B .runtime/explorer-attribute-pidl-control/build -G 'Visual Studio 17 2022' -A x64
& 'C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' --build .runtime/explorer-attribute-pidl-control/build --config Release --parallel 2
pwsh -NoProfile -File .runtime/explorer-attribute-pidl-control/run.ps1 -Action validate
```

Only after coordinator review, the single controlled run is:

```powershell
pwsh -NoProfile -File .runtime/explorer-attribute-pidl-control/run.ps1 -Action run
```

Frozen executable SHA256: `DB0700310571C6540E3DFE50CB2DCB718C3AE1E63D1CA25B365A04A5DB411E4C`. Guard: `323CD843CE402405C07102B7950A201617B372AE7A3E7DA0A62ED602BACD3A76`. DLL: `F298A352439C6209D274B567081191819BD55A45BC2F63A025EE28FC69C42E96`.
