# Same-NAS image supervisor

ADR-0023 / local-image-container-v1 only. This BCL-only NativeAOT executable does not reference Core or Skia assemblies. The sole production invocation is PID1 with no arguments. Fixed child: `/app/workers/image-preview/AssetLibrary.ImagePreview.Worker --container-decoder`; no user-supplied command, path, URL or environment.

The deployment supplies private PID/net/mount/IPC namespaces, network none, read-only image, the one private IPC volume and a real 512MiB memory cgroup. Root PID1 retains only CHOWN/SETUID/SETGID/KILL (root's revised contract); it binds/chmods/chowns the socket before accepting. It does not claim that per-thread capset removes a capability from every CLR thread. Each child clears groups and uses libc setresgid/setresuid to become 1655 permanently, with normal securebits. The warmed decoder verifies every thread's Uid/Gid including fsuid, capabilities and NNP before reading input, then requires the exact memory bound, no inherited socket/ring and MEMLOCK0 rejection of a valid ring creation.

One decoder is admitted. At most one preceding response publication is retained, so no more than two bounded exchange tasks exist. Extra clients get status7 without a worker or queue. The supervisor streams source bytes, validates at most12MiB PNG output, waits for the actual successful child exit, and releases the decoder admission before publishing the complete response. Core can close immediately after the full PNG. Work uses8s total cancellation; cleanup retains admission until wait/reap. pidfd signals the original owned child, never a guessed/reused process ID. Failure to terminate/reap or an uncertain late startup exits PID1 to retire the private namespace.

Persistent files live only under `/run/assetlibrary-image` (root:1654/0710): `supervisor-state.bin`, an8-byte bounded counter; `supervisor-lock.bin`, a root0600 exclusive lifetime file lock; and `supervisor-state.next`, a root0600 atomic replacement. No media or credentials are stored. An attempt is reserved and fsynced before launch, then cleared only after a healthy terminal result and actual cleanup. Invalid/unsupported/limit images and confirmed normal cancellation do not create restart storms. At3 unrecovered attempts, new decoders are disabled. Malformed state or permanent startup errors keep PID1 unhealthy without accepting work. State is not automatically erased on ordinary restart.

`--health` never connects or launches a decoder. It reads state, exact socket metadata and listening entry, `/proc/1/exe`, all PID1 thread UID/capability/NNP fields and the kernel memory cgroup limit. Deployment ownership/image/mount evidence remains the installer's responsibility.

Operational probes use this same PID1 and child launcher, in task-owned containers with restart disabled:

- `--probe-container-isolation`: before/after valid ring control, NPROC fork rejection, parent signal denial, private state denial and child identity.
- `--probe-container-memory`:768MiB native allocation must fail under512MiB AS.
- `--probe-container-threads`: a new thread must fail after hard NPROC1.
- `--probe-container-cpu`: intentional spin; record actual kernel termination/exit/reap (nonzero exit is expected), not a fake JSON success.

All probes use the normal warmup/confinement path. These Linux probes have not been run on Windows and must not be reported as passed from Windows compilation.

Build entry:

```text
dotnet restore services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj --locked-mode
dotnet format services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj --verify-no-changes --no-restore
dotnet build services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj -c Release --no-restore
```

Linux AOT publishing uses the pinned SDK and root-owned linux-x64 release lock, `-p:PublishAot=true --self-contained true`; root owns the solution/lock/packaging integration. No additional NuGet dependency is required.
