# M0-002 Explorer soak protocol

`tests/spikes/windows-shell/scripts/soak.ps1` is only a host-cycle helper. It
does not navigate Explorer or exercise the Shell DLL, so its output is not
Explorer soak evidence. A Windows 11 x64 operator must run this protocol and
save the evidence outside the repository or in an approved test-results store.

## Preconditions

- Windows 11 x64, Visual Studio 2022 Desktop C++, Windows 11 SDK and CMake.
- UAC enabled (`EnableLUA=1`). The HKCU-only registration script refuses a host
  with UAC disabled because an administrator Explorer cannot bind per-user COM
  classes in that state. Do not substitute an HKLM registration for this Spike.
- A clean current-user registration produced by `register.ps1`.
- Explorer restart available for the recovery checkpoints.
- Use the checked-in scripts with the 3-second bounded background
  `SHChangeNotify`; do not substitute an older synchronous-notification copy.
- Disable Windows/system sleep for the duration; sleep/wake gaps are not valid
  soak evidence. Stop an existing `AssetHostStub` before starting the cycle helper.

## Manual procedure

1. Build and register with `build.ps1`, `register.ps1`, and
   `verify-registration.ps1`.
2. Start `run-host.ps1 -Mode normal` in a separate PowerShell window.
3. Launch `%SystemRoot%\Explorer.exe /e,::{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}`
   as documented by Microsoft for namespace extensions; do not type the CLSID
   into Explorer's address bar. Enter `AssetHost (M0-002)`, then record view
   activation time and the connected status.
4. Repeat at least 20 times: stop/restart the host, open or refresh the custom
   view, and confirm Explorer remains responsive and returns to connected state.
5. Repeat with `-Mode crash -Once`, `-Mode slow -Once`, and
   `-Mode invalid -Once`. Confirm each failure reaches the unavailable status
   within the 250 ms protocol attempt budget (plus worker cancellation cleanup and
   normal UI scheduling), without an Explorer hang or process crash; restart normal
   host and confirm recovery. The Explorer view activation thread must return
   without waiting for this cleanup.
6. Run `soak.ps1 -Hours 8` in parallel as the host-cycle signal, while keeping
   the manual Explorer view open and periodically recording bounded response,
   memory, and recovery observations. This combined operator run is the only
   candidate for the 8-hour Explorer gate.
7. Keep the custom view open and unregister with `unregister.ps1`. The script
   must exit instead of waiting indefinitely: a delayed cache notification may
   produce the documented 3-second warning, but the owner-checked registry state
   must already be clean. Then restart Explorer and confirm the namespace
   disappears and ordinary Explorer navigation remains functional.

## Evidence checklist

The host-cycle helper uses monotonic elapsed time, a 30-second interval and at
most 1000 iterations by default (about 960 cycles for eight hours). Scheduling
gaps, nonzero host exits, exhausting the iteration limit early, or failure to
stop a host within five seconds fail the run. Each process handle is disposed
before the next cycle. These bounds limit the helper; they do not prove Shell
DLL responsiveness, cancellation drain time or the eight-hour Explorer gate.

Record Windows build, SDK/MSVC/CMake versions, DLL/EXE hashes, start/end times,
host mode, iteration counts, Explorer responsiveness, failure-to-recovery times,
process crashes, bounded-notification outcome, and registration/unregistration
results. Do not record user
paths, credentials, asset names, or private file contents. A missing, failed, or
partial item remains an unmet M0-002 gate.
