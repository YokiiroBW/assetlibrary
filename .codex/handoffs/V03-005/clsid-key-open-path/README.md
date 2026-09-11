# SHCORE ordinal 120: fixed-file read path

Only the on-disk `System32\SHCORE.dll` was read. No target code, registry API,
GUI, debugger attach, or ordinal call was executed. The PE export has no name
and is not a forwarder. Ordinal 120 resolves to RVA `0x35850`, with exact
`.pdata` range `[0x35850,0x3599E)` (334 bytes). CodeView and file fingerprints
are in `index.json`; parsed imports include both normal and delay imports.

The known windows.storage call at `0x11B949` supplies R8D=0, R9D=0, access
mask=1, and the `ShellFolder` string. In this SHCORE build that selects an
empty prefix and the predefined HKEY_CLASSES_ROOT value. With the GUID
converted by imported StringFromGUID2, the static string construction is
`CLSID\{GUID}\ShellFolder`. RegOpenKeyExW is called at `0x35908` (IAT
`0xAAED8`); its return is at `0x3590F`. Positive Win32 errors become 8007xxxx
at `0x35914..0x3591B`. No registry key creation is selected by this caller.
The nonzero third-argument branch instead uses HKCU with an Explorer prefix;
the nonzero fourth-argument branch selects RegCreateKeyExW. Neither is the
known caller's branch. No public contract for this ordinal is claimed.

Three local runtime-function ranges were disassembled: 35850/334 bytes,
359A4/108 bytes, 62A44/98 bytes. Total 540 bytes, maximum two call layers.
The raw output and code hashes are in `local-analysis-index.json`. Unexpanded
callees, including the two direct calls in the last function, are only listed
by RVA. No meaning is invented for anonymous functions.

This establishes static branch and argument evidence. It does not establish
which HKCR view or cached handles the real Explorer uses, whether an actual
open failed, or why its observed cache DWORDs were zero. `findings.json`
separates those limits from the local observations. No symbol file was
fetched or interpreted by this preparation.

Reproduction uses installed Python stdlib for pe_index.py/local_index.py and
installed dumpbin /DISASM:BYTES /RANGE for only the three recorded ranges.
The existing attributes observers and prior directories remain unchanged.
