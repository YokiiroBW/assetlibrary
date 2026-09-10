# Minimal manual-step RegOpen capture

Active files are `capture.ps1`, `assert-live.ps1`, and read-only `StageClock.cs`. The earlier `*.draft.txt` state-machine files are inactive and must not be executed. The original notify scripts remain in `../explorer-notification-trigger-plan/`. No guard or old capture template was modified.

The fixed new output directory is `.runtime/explorer-live/20260910-clsid-key-open`. Preparation has not created registration, called native notifications, attached a debugger, or run these new scripts. PowerShell AST checks and static review only are complete; the tiny clock P/Invoke has not been called in this preparation.

After candidate review and the normal controlled registration prerequisites, establish a fresh exclusive SDK17 window and identity. Then start `capture.ps1 -PlanId <new id>` in its own hidden process/tool session. It starts only the frozen RegOpen observer, continuously drains JSONL, writes exact ready identity and native capture deadline, and waits for cleanup. It never dispatches notify, GUI or Browse. Ready absence after 45 seconds or a wrapper exception requests cooperative cancellation; no observer/Explorer hard kill exists.

The unique GUI owner executes these steps explicitly while that collector remains running:

1. `assert-live.ps1 -PlanId <id> -Step notify`, saving its output; then original `../explorer-notification-trigger-plan/run-notify.ps1 -RunDirectory <new cycle> -PlanId <id>`. Require sender exit0, no timeout/stderr and original Notification.Success. Failure means cooperative cancel and no navigation.
2. Before each ThisPC GUI input and its native baseline, run `assert-live.ps1 -PlanId <id> -Step thispc`. Perform first ThisPC navigation via CUA and invoke the new `../explorer-official-observer/run-clsid-key-open.ps1 -Mode thispc -Label thispc-baseline`. Save actual UI timing separately.
3. `assert-live.ps1 -PlanId <id> -Step browse`, saving its output, then invoke that same view wrapper with `-Mode browse -Label debug-browse-call` once. The original wrapper retains its own exact target, ThisPC and guard checks and one-shot Browse marker.

The read-only assertion requires observer/target PID and creation identity still alive, no capture failure/completion or registry stop/cleanup, native capture time remaining (30/25/15 seconds for notify/ThisPC/Browse), and at least 30 seconds left on the original registration. Notify is one-shot; later steps require its successful result, and Browse additionally requires the successful ThisPC baseline after notification. A successful assertion is a point-in-time check, not a guarantee against a subsequent process exit.

On any external-step error, send the frozen observer `--cancel-plan <id>`, keep the collector draining, and inspect confirmed detach/zero breakpoints/target liveness before owned UI and original registration cleanup. Preserve no-hit and incomplete results; do not retry notifications/Browse or extend the native 60-second/guard600-second deadlines.
