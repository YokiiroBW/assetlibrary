# Cache attributes reference completion fix

V2 only changes the reference completion predicate from exactly one cache
snapshot to one snapshot per matched outer call (`cacheSnapshots == matched`).
The existing positive-return, empty-pending and valid-output requirements stay.
Sampling, frame matching, caps, exception behavior and cleanup are unchanged.
V1 and its failed registered-reference run remain preserved.

Compilation and 38 offline predicate/plan checks passed. New normal/cancel
runs remain for the GUI owner during the fixed registration period. Run:

```powershell
.\bin\CacheAttributesObserverV2.exe --normal
.\bin\CacheAttributesObserverV2.exe --cancel
```

These modes create only the fixed sibling reference child. This preparation
did not launch or attach a target. Real Explorer use remains separately gated.
See `candidate.diff`, `validation.json`, and `manifest.json` for exact evidence.
