# V03-021 client/Requests review checkpoint

This is an independently buildable checkpoint, not completion of the custom Explorer view.

The owner-approved LocalPipePeer extraction retains the original SnapshotPipe 150ms/64KiB/cancellation implementation and shares actual TokenUser/session/held server identity checks. ThumbnailClient implements frozen thumbnail-v1 with strict header/correlation/epoch/node/PBGRA/1MiB validation and at most two retained I/O operations, including canceled operations until actual completion. Background image waits are at most20s; no EOF wait, original-file access, network stack or media decoder is added.

Requests retains plain State/Ticket/cancellation/module pins only, never Folder/View/Surface COM references. It limits page work to4, image worker threads to2, outstanding image tickets to64/module and current visible candidates to16/view. Page Loading has10s/20-attempt limits; Ready is not polled. Per-viewport ticket identity is checked again by Current after UI call-outs so already-taken batches cannot restore canceled images. Final Surface/View cancellation, navigation and visible-page integration are in the following checkpoint.

Validation already performed with final client/Requests sources: strict Release/W4/WX/analyze builds; explorer_gallery_thumbnail passed20.14s including13 coordinator wire vectors, actual same-user native pipes, malformed/partial/cancel/reclaim/two-slots and20s deadline; explorer_gallery_requests passed0.73s including late generation/ticket discard, UI query zero, queue bounds and reclamation. Existing original9 CTests passed48.08s with the peer extraction in place. No network/NAS or production registration was used.

Dependencies already coordinator-owned: Surface.h582b907, thumbnail-vectors7fae7a0. The subsequent full View checkpoint also uses Surface31f26c2/fd17678 and newer UI visibility fixes. Do not treat the invalid unregistered private-PIDL notification fixture as a real notification positive control; the full View tests use a native PIDL for an owned temporary directory.

CLI after this checkpoint:
cmake -S tests/windows-shell -B .runtime/explorer-gallery -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-gallery --config Release --parallel 2
ctest --test-dir .runtime/explorer-gallery -C Release --output-on-failure -R '^explorer_gallery_(thumbnail|requests)$'
