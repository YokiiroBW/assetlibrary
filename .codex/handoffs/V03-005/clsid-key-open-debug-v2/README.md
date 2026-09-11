# RegOpen observer V2: bounded exception accounting

Only exception accounting, owned-reference C++ controls and fixed V2 paths
change. API matching, IAT identity checks, 60-second capture limit, 4096 API
entry budget, return pairing, event delivery and cleanup remain unchanged.

All observed target exception events use trigger threshold 256; the already-armed
tool cleanup interrupt is excluded as before. This is not an immediate hard
delivery cap: the next event latches protection, and the existing WaitForEvent
can deliver additional events before the outer loop starts cleanup. First-chance E06D7363 is counted separately.
Other first-chance events retain a 16 limit. Any second chance fails. Priority
is second chance, then other-first count, then total count. The first stop
reason and triggering code/chance/total remain fixed. Only 16 detailed event
lines are written; complete aggregates are recorded on protection and again
after cleanup, including failure paths. Exceptions still pass to the target
with GO_NOT_HANDLED. No filter or target exception behavior is changed.

The old budget was exhausted before the real GUI trigger. The new fixed trigger threshold
is bounded diagnostic calibration; no stable event rate, safety margin,
benign classification, or guarantee of reaching 60 seconds is claimed.

Build and 50 pure predicate/plan checks passed. Normal and cancel owned
references each produced 32 real noinline C++ throw/catch events after the
original controlled SEH event: debugger C++ count32, target catch32, other1,
total33. Both still matched the fixed RegOpen key with LSTATUS2 and no failed
PHKEY read, then removed all breakpoints/detached/verified healthy/cooperatively
exited. The 257-C++ negative reference triggered protection at total257
(C++256 plus original SEH1). The current debugger wait also delivered the last
C++ event and fixed API call before returning: final total258/C++257, all257
caught by the target. Original failure reason stayed total_exception_limit.
The observer remained passed=false and exit1; protective cleanup separately
validated. All three original child identities are now absent.

```powershell
.\build.ps1
.\bin\ClsidKeyOpenObserverV2.exe --self-test
.\bin\ClsidKeyOpenObserverV2.exe --normal
.\bin\ClsidKeyOpenObserverV2.exe --cancel
.\bin\ClsidKeyOpenObserverV2.exe --exception-limit # expected exit 1
```

The last three modes only create the fixed sibling reference. No arbitrary
exception count is accepted. --exception-limit cannot take a real plan.
The existing --run-plan/--cancel-plan interface remains official-only and
uses the same 11-line plan in this V2 directory. No real Explorer or GUI was
operated during this preparation. Original source/artifacts remain frozen.
Native Attach/Detach are not hard-deadline operations; do not kill a debugger
or target and claim successful cleanup. See validation.json and raw records.
