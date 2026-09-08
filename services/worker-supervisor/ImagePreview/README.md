# Bounded built-in image worker (V03-007, partial)

This independent .NET 10 executable references only SkiaSharp and the BCL. It does not load Core, Host, database configuration, source roots, provider manifests or URLs. Production remains unavailable until the relevant platform enforcement and real integration checks pass.

Normal execution takes no arguments. The 24-byte little-endian header is six 32-bit values: magic `0x31495041`, status, profile, payload length, width, height. The worker establishes confinement and emits status 1 (ready), then reads one status-2 request (profile 0 thumbnail or 1 preview), its bounded encoded bytes and EOF. A successful response has status 3 and PNG bytes; statuses 4/5/6/7 are invalid/unsupported/limit/unavailable. No input is decoded before confinement. Source limit 32MiB, 40MP and 16384 per edge; outputs 512/2MiB or 1600/12MiB, no upscaling, orientation applied, 8bit PNG with alpha. Animation and 16bit PNG are unsupported in this first scope.

## Builds and locks

Normal build uses `packages.lock.json` and no implicit AOT tool dependencies:

```text
dotnet restore services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj --locked-mode
dotnet build services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj -c Release --no-restore
```

Actual AOT publishing enables strict AOT/trimming diagnostics. Use the existing `AssetLibraryReleaseLockRoot` property with a task-owned absolute directory, distinct from the ordinary lock. The first RID restore generates the release lock; subsequent verification must use `--locked-mode`. Linux must build on Linux with the pinned SDK, clang and zlib development headers. Windows uses the installed MSVC toolchain. Native symbols remain diagnostic build artifacts; final package symbol separation still needs verification.

```text
dotnet restore services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj -p:RuntimeIdentifier=linux-x64 -p:PublishAot=true -p:AssetLibraryReleaseLockRoot=<absolute-release-lock-directory> --locked-mode
dotnet publish services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj -c Release -p:RuntimeIdentifier=linux-x64 --self-contained true -p:PublishAot=true -p:AssetLibraryReleaseLockRoot=<absolute-release-lock-directory> -p:DebugType=None -p:DebugSymbols=false --no-restore -o <owned-output>
```

## Linux same-code probes

These operator-only modes use the same warmup and `LinuxImageIsolation.Enter` as image requests. They never accept images, provider code, credentials or arbitrary target paths. Run as a non-root user with no permitted/effective/inheritable capabilities. For `--probe-isolation`, create the fixed file `../preview-denied.marker` next to the publish directory containing exactly `assetlibrary-image-canary-v1` with **no newline**. It must be a synthetic marker in the operator's owned test directory. The probe reads this bounded known marker before installing its filter and attempts it again afterward.

- `--probe-isolation`: emits bounded JSON covering file open, socket creation, connect through a pre-existing test socket, cross-process signal/memory, fork/non-thread clone, exec and io_uring rejection, plus pre-filter seccomp/io_uring state. Compare the controls: an outer Docker denial is not evidence of a new worker denial.
- `--probe-memory`: attempts a bounded 768MiB native allocation against the worker's 512MiB address-space cap; it must fail. This is not a GC-only check.
- `--probe-threads`: makes at most 257 attempts with small-stack background threads; RLIMIT_NPROC 256 and the memory cap must stop creation, then all started threads are released and joined under a shared one-second cleanup budget. The UID-wide limit can conservatively deny sooner. No CAP_SYS_ADMIN/SYS_RESOURCE or root bypass is accepted.
- `--probe-cpu`: intentionally spins. The kernel CPU limit must terminate it; use an independent parent wall deadline and verify exit/reap. It does not emit a fabricated success record.

The worker has a 64MiB workstation, nonconcurrent GC heap budget in addition to native/OS limits, embedded once in its project; launchers clear inherited environment and do not override that budget. Linux checks current address-space reservations before admission: setting an AS ceiling alone cannot retract existing reservations. It requires NativeAOT, limits AS/CPU/FSIZE/CORE/NPROC, sets parent-death SIGKILL and no_new_privs, then installs a TSYNC x86-64 allowlist. Only standard pipe descriptors can be read/written/queried, memory mappings must be anonymous, signals target self, prlimit may only query self, and clone is restricted to actual threads. Installation failure rejects execution. These claims need actual same-code probe evidence; this README does not close M0-008.

Windows requires LPAC, the exact ADR-0019-approved `lpacCom` capability and matching Job limits. Actual NativeAOT PNG output and Core/PostgreSQL/HTTPS image requests now pass. The launcher retains its original task and resources through cancellation, and private version-2 ownership journals support confirmed cleanup and recovery; malformed or uncertain records fail closed. These results do not complete the separate native fault, filesystem/network/handle, CPU/memory or COM boundary matrix owned by V03-006.
