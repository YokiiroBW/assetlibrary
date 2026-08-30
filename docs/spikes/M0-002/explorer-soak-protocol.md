# M0-002 Explorer soak protocol

`tests/spikes/windows-shell/scripts/soak.ps1` is only a host-cycle helper. It
does not navigate Explorer or exercise the Shell DLL, so its output is not
Explorer soak evidence. A Windows 11 x64 operator must run this protocol and
save the evidence outside the repository or in an approved test-results store.

## Preconditions

- Windows 11 x64, Visual Studio 2022 Desktop C++, Windows 11 SDK and CMake.
- A clean current-user registration produced by `register.ps1`.
- Explorer restart available for the recovery checkpoints.

## Manual procedure

1. Build and register with `build.ps1`, `register.ps1`, and
   `verify-registration.ps1`.
2. Start `run-host.ps1 -Mode normal` in a separate PowerShell window.
3. Open `shell:::{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}` and enter
   `AssetHost (M0-002)`. Record view activation time and the connected status.
4. Repeat at least 20 times: stop/restart the host, open or refresh the custom
   view, and confirm Explorer remains responsive and returns to connected state.
5. Repeat with `-Mode crash -Once`, `-Mode slow -Once`, and
   `-Mode invalid -Once`. Confirm each failure reaches the unavailable status
   within the 250 ms protocol budget (plus normal UI scheduling), without an
   Explorer hang or process crash; restart normal host and confirm recovery.
6. Run `soak.ps1 -Hours 8` in parallel as the host-cycle signal, while keeping
   the manual Explorer view open and periodically recording bounded response,
   memory, and recovery observations. This combined operator run is the only
   candidate for the 8-hour Explorer gate.
7. Unregister with `unregister.ps1`, restart Explorer, and confirm the namespace
   disappears and ordinary Explorer navigation remains functional.

## Evidence checklist

Record Windows build, SDK/MSVC/CMake versions, DLL/EXE hashes, start/end times,
host mode, iteration counts, Explorer responsiveness, failure-to-recovery times,
process crashes, and registration/unregistration results. Do not record user
paths, credentials, asset names, or private file contents. A missing, failed, or
partial item remains an unmet M0-002 gate.
