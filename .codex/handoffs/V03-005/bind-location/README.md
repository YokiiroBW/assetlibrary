# MyComputer BindToObject reference locator

This fixed native helper takes no arguments. It creates a local MyComputer `IShellFolder` reference through public Shell APIs, retains it throughout location and control work, and reads its public SDK C-interface `BindToObject` function pointer. A compile-time `offsetof` check fixes the slot at 5 / 40 bytes on x64. It never loads DbgEng, attaches a debugger, operates a GUI, registers anything, enumerates a folder, or reads asset files.

```powershell
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-location\build.ps1'
& 'C:\YOKI\Codex\AssetLibrary-worktrees\V03-005\.runtime\explorer-bind-location\bin\BindLocator.exe'
```

Build uses installed MSVC 14.44.35207 / Windows SDK 26100, x64 `/W4 /WX /MT /O2`; no downloads or new runtime dependencies. All preparation outputs are confined to this directory.

The locator rejects an unexpected MyComputer class, successful `IClientSecurity` proxy query, non-image/non-executable/writable method memory, and modules outside the exact System32 shell32.dll/windows.storage.dll allowlist. Thus combase, RPC/proxy modules and arbitrary paths are not accepted. The accepted module remains loaded; its file is held against writes/deletes while SHA256 and version metadata are read. PE machine, method RVA and executable section bounds are checked. Hashing streams the bounded system module; only relative offsets, approved basename, hash/version and system class IDs are retained. No absolute method addresses or user paths are printed.

Actual `location.jsonl` result:

- MyComputer class: `{20D04FE0-3AEA-1069-A2D8-08002B30309D}`; `IClientSecurity` query: `E_NOINTERFACE`.
- Module: `windows.storage.dll`, version `6.2.26100.8036`, AMD64 (`0x8664`), `MEM_IMAGE`.
- BindToObject RVA: `0x000ABC70` (703600).
- Code section: RVA 4096, length 6624332; image size 8798208.
- File SHA256: `22aff66094747a65ea1b5390e60dd423cf3bfd897781a6347ccf7f6234d0388b`.
- First 32 method bytes SHA256: `d191a9c6322a212351ae2247519f3cbaa2444746feb1b18f4441b5e9c728a162`.
- Using the same obtained function pointer, a legal relative PIDL for the system-volume root bound successfully to `IShellFolder`; child class `{F3364BA0-65B9-11CE-A9BA-00AA004AE837}`. Only root metadata was requested. Interface release and COM uninitialization are recorded; exit code 0.

This is a **reference-process code-location clue**, not an Explorer debugger hit. The control demonstrates a successful call through the obtained pointer; it does not prove that the real Explorer's parent implementation uses this RVA. COM allows different implementations of one IID. `IClientSecurity` failure excludes a standard security-aware proxy but does not alone exclude custom marshaling; the strict system-module/code checks add evidence, not a universal remoting proof. MEM_IMAGE does not by itself prove a page was never modified; the memory prefix hash is supplied for a later identity comparison. No real-target mapping or code-byte comparison has been performed here.

Primary references: [COM interface function tables](https://learn.microsoft.com/en-us/windows/win32/com/interface-pointers-and-interfaces), [BindToObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-bindtoobject), [IClientSecurity limitations](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nn-objidl-iclientsecurity), [VirtualQuery and image-page limitations](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualquery).
