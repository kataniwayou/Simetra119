# Phase 87: K8sLeaseElection — Gate 1 (Backoff Before Acquire) - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Modify `K8sLeaseElection` to add an outer loop with `_innerCts` wrapping `RunAndTryToHoldLeadershipForeverAsync`. Before each election attempt, non-preferred pods check `IsPreferredStampFresh` and back off if the preferred pod is alive. Preferred pods and feature-off scenarios skip backoff. Phase 88 (voluntary yield) will use the `_innerCts` cancel mechanism added here.

</domain>

<decisions>
## Implementation Decisions

### Outer loop structure
- Outer loop always present, even when PreferredNode is empty (feature off). NullPreferredStampReader returns false → backoff never triggers. One code path, no conditional branching.
- `_innerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)` — inner cancel restarts election, outer stoppingToken shuts down entirely
- When inner is cancelled, outer loop immediately restarts (no delay between cancel and restart)
- `OnStoppedLeading` treats all cases the same — sets `_isLeader = false`, no distinction between yield vs natural loss. Outer loop always restarts and re-evaluates.
- The election loop stays in BackgroundService (not Quartz) — `RunAndTryToHoldLeadershipForeverAsync` is long-running, not periodic

### Backoff duration
- Non-preferred pods delay `DurationSeconds` (15s) when `IsPreferredStampFresh` is true
- Wait full duration, then check once. If still fresh → delay again. If stale → compete immediately.
- Simple `Task.Delay(DurationSeconds, stoppingToken)` — no periodic re-checks during backoff
- Preferred pod is never subject to backoff — it competes immediately through normal LeaderElector flow

### Feature-off behavior
- Outer loop always present regardless of PreferredNode config
- When feature off: `IPreferredStampReader` is `NullPreferredStampReader` (always false) → backoff condition never true → election proceeds immediately every iteration
- Zero overhead in the hot path — single bool check before delay

### OnStoppedLeading idempotency
- Must only set `_isLeader = false` — no destructive teardown, no lease deletion (lease deletion is in StopAsync only)
- Already idempotent today — verify this holds after outer loop changes

### Claude's Discretion
- Whether to expose `_innerCts` for Phase 88 yield via a method or direct field access
- Exact placement of backoff check in the outer loop (before creating inner CTS, or after)
- Log messages for backoff decisions (if any — Phase 89 covers structured logs, but basic Debug logs are acceptable here)

</decisions>

<specifics>
## Specific Ideas

- The outer loop pseudocode:
  ```
  while (!stoppingToken.IsCancellationRequested)
  {
      if (!_isPreferredPod && _stampReader.IsPreferredStampFresh)
          await Task.Delay(DurationSeconds, stoppingToken);

      _innerCts = CreateLinkedTokenSource(stoppingToken);
      await elector.RunAndTryToHoldLeadershipForeverAsync(_innerCts.Token);
      // inner cancelled or lost → loop restarts
  }
  ```
- `_isPreferredPod` comes from `PreferredLeaderService.IsPreferredPod` (injected)
- `_stampReader` is `IPreferredStampReader` (injected)
- Phase 88 will cancel `_innerCts` from outside the loop when preferred stamp becomes fresh while leading

</specifics>

<deferred>
## Deferred Ideas

- Voluntary yield (cancel _innerCts when preferred recovers while leading) — Phase 88
- Structured INFO log at each election decision point — Phase 89

</deferred>

---

*Phase: 87-election-gate-1-backoff*
*Context gathered: 2026-03-26*
