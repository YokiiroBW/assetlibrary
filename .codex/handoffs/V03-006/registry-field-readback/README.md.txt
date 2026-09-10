# Fixed official field readback

This independent x64 reader uses the reviewed 11-field contract unchanged (SHA256 `63CC68C84E2E49B4AE9078E7A6EE474B795461BE68E464AA765E55E1E2A41A13`). It never loads, dot-sources or executes the registration guard. Registry operations are only `RegOpenKeyExW` with `KEY_QUERY_VALUE|KEY_WOW64_64KEY`, `RegQueryValueExW`, and `RegCloseKey`.

Each field reports key/value presence, actual native status, type, byte count and actual value where valid. A metadata query precedes a bounded data read (maximum 4096 bytes). No retry hides changing type/size or MORE_DATA. DWORDs are normalized from the four actual bytes to unsigned `0xXXXXXXXX`; REG_SZ requires valid UTF-16 with a final terminator and no embedded null. An existing empty default string is distinct from a missing value. Unsupported types retain their type/status without reading arbitrary payloads. Expected-value comparison never substitutes a missing actual value.

There are 20 records: 11 official HKCU fields, four separate owner fields, and five BA16 fields in the current reader's HKCR merged view. HKCR is explicitly an external standalone-process observation, not proof of Explorer's view. Reads are sequential rather than an atomic snapshot. Raw DLL paths may identify the current user profile; keep original output in runtime and clearly label any redacted repository copy with both hashes.

`run-readback.ps1` starts only a new hidden readback process, bounds it to 10 seconds, and preserves original stdout/stderr/exit/timeout metadata under a new label. It can terminate only that owned reader on timeout. It does not register anything or inspect/control Explorer.

```powershell
pwsh -NoProfile -File .runtime/explorer-registry-readback/run-readback.ps1 -Mode expected-missing -Label unregistered-baseline
# In the next coordinator-defined valid registration window only:
pwsh -NoProfile -File .runtime/explorer-registry-readback/run-readback.ps1 -Mode expected-registered -Label registered-before
pwsh -NoProfile -File .runtime/explorer-registry-readback/run-readback.ps1 -Mode expected-registered -Label registered-after
```

Preparation completed: both PowerShell ASTs and C# Add-Type compile passed. One expected-missing run passed: 20 records all `missing-key`, native open status2, exit0, no timeout/stderr, no registry writes. This validates the unregistered/missing-key path only. Registered values, missing-value-with-existing-key, wrong types, string/size errors, concurrent changes and timeout cleanup have source review only; no fixtures or registrations were created to manufacture positive evidence.
