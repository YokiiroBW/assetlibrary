# MyComputer GetAttributesOf reference locator

This fixed native helper takes no arguments and reuses the reviewed BindLocator's system-image checks. It creates a local MyComputer `IShellFolder`, verifies class `{20D04FE0-3AEA-1069-A2D8-08002B30309D}`, and keeps that reference alive through location and control work. The public SDK CINTERFACE layout is checked at compile time: `GetAttributesOf` is slot 9 / offset 72 bytes on x64.

```powershell
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-attributes-location\build.ps1'
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-attributes-location\bin\AttributesLocator.exe'
```

Build passed with installed MSVC 14.44.35207 / SDK 26100, x64 `/W4 /WX /MT /O2`, no added dependency. The helper rejects a standard security-aware proxy, non-image/non-code/writable method memory and any module outside the exact System32 shell32.dll/windows.storage.dll allowlist. It holds the module/file, checks PE machine and executable section bounds, streams the system-file SHA256 and hashes the method's first 32 bytes. No absolute method address or user path is retained.

Actual `location.jsonl` result:

- Module `windows.storage.dll`, version `6.2.26100.8036`, AMD64, MEM_IMAGE.
- GetAttributesOf RVA `0x001188E0` (1149152).
- Executable section RVA 4096, length 6624332; image size 8798208, timestamp 2800646882.
- File SHA256 `22aff66094747a65ea1b5390e60dd423cf3bfd897781a6347ccf7f6234d0388b`.
- First 32 code bytes SHA256 `991cf0c259bf70e99b9b2d34fe48ea5e4675e48cfd1c7bfb55eac5f3e612ca64`.
- One legal system-volume-root relative child PIDL is produced through ParseDisplayName and checked with public IL APIs. Calling the same obtained method pointer with `cidl=1` requests `FOLDER|FILESYSTEM|BROWSABLE|HIDDEN|NONENUMERATED` (`0x68180000`). Actual HRESULT is S_OK and output mask is `0x60000000` (`FOLDER|FILESYSTEM`). The tool does not require or pre-assume particular returned bits for success.
- Interface release and COM uninitialization are recorded; process exit code 0.

This is a reference-process call and code-location clue, **not an Explorer debugger hit or BA16 attribute result**. It cannot establish whether the real host's parent implementation returns FOLDER. As in the prior locator, IClientSecurity E_NOINTERFACE is not a universal exclusion of custom marshaling, and MEM_IMAGE alone does not prove memory was never modified. No host mapping/byte comparison was performed.

No debugger, GUI operation, registration change, directory enumeration or asset-content read was performed. Only system-root metadata and the approved system module were read. All other frozen directories remain untouched. `changes.patch`, `validation.json`, and `manifest.json` provide the source review and prepared artifact record.
