---
phase: 87-election-gate-1-backoff
plan: 01
subsystem: infra
tags: [k8s, leader-election, cancellation-token, backoff, preferred-leader]

requires:
  - phase: 86-preferred-heartbeat-writer
    provides: PreferredLeaderService.IsPreferredPod and IPreferredStampReader.IsPreferredStampFresh — both registered in K8s DI branch

provides:
  - K8sLeaseElection outer while loop with _innerCts lifecycle
  - Gate 1 backoff: non-preferred pod delays DurationSeconds when stamp is fresh
  - CancelInnerElection() method for Phase 88 voluntary yield
  - Two new constructor parameters (PreferredLeaderService, IPreferredStampReader)

affects:
  - 87-02 (Gate 1 tests)
  - 88-voluntary-yield (calls CancelInnerElection)

tech-stack:
  added: []
  patterns:
    - "Outer loop with linked inner CTS: while(!stoppingToken) { using var innerCts = CreateLinkedTokenSource(stoppingToken); _innerCts = innerCts; try { await op(innerCts.Token); } catch(OCE) when stoppingToken { break; } catch(OCE) { /* inner cancel, continue */ } finally { _innerCts = null; } }"
    - "CancelInnerElection(): try { _innerCts?.Cancel(); } catch(ObjectDisposedException) {} — null-conditional + ODE guard"
    - "Gate 1 backoff: if (!IsPreferredPod && IsPreferredStampFresh) { await Task.Delay(duration, stoppingToken); continue; }"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Telemetry/K8sLeaseElection.cs

key-decisions:
  - "PreferredLeaderService injected as concrete type (not interface) — IsPreferredPod is on the concrete, not IPreferredStampReader"
  - "elector created once outside loop — LeaderElector is reused across inner restarts, not recreated each iteration"
  - "Task.Delay for backoff uses stoppingToken, not innerCts.Token — innerCts does not exist yet at backoff point"
  - "OnStoppedLeading remains _isLeader = false only — idempotency preserved, no lease deletion side effects"
  - "_innerCts is volatile — sufficient for null-conditional cancel from separate thread; stale null read is a safe no-op"
  - "StopAsync unchanged — stoppingToken cancellation auto-propagates to innerCts via linked source; using block handles disposal"

patterns-established:
  - "Outer loop with _innerCts: standard pattern for restartable BackgroundService operations without full service stop"
  - "Gate backoff via Task.Delay + continue: cancellable, zero-allocation, stoppingToken exits cleanly"

duration: 2min
completed: 2026-03-26
---

# Phase 87 Plan 01: Election Gate 1 — Backoff Before Acquire Summary

**K8sLeaseElection refactored with outer while loop, Gate 1 backoff (non-preferred pod delays DurationSeconds when preferred stamp is fresh), and CancelInnerElection() method enabling Phase 88 voluntary yield**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-25T23:41:25Z
- **Completed:** 2026-03-25T23:42:46Z
- **Tasks:** 2 (implemented atomically in single file write)
- **Files modified:** 1

## Accomplishments

- K8sLeaseElection.ExecuteAsync now has outer while loop with fresh `_innerCts` each iteration — required scaffolding for Phase 88 voluntary yield
- Gate 1 backoff: when `!IsPreferredPod && IsPreferredStampFresh`, non-preferred pod delays `DurationSeconds` before election attempt — gives preferred pod a head start
- Preferred pod never enters backoff path (ELEC-04 preserved)
- Feature-off path (NullPreferredStampReader always returns false for IsPreferredStampFresh) — zero overhead, backoff block never executes
- OnStoppedLeading unchanged — remains `_isLeader = false` only, idempotent across both outer shutdown and inner voluntary yield paths
- Build: 0 errors, 0 warnings. All 509 existing tests pass

## Task Commits

Each task was committed atomically:

1. **Task 1 + Task 2: Constructor params, _innerCts field, CancelInnerElection, outer loop, Gate 1 backoff** - `7188591` (feat)

**Plan metadata:** (docs commit to follow)

## Files Created/Modified

- `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — Added 2 constructor params (7 total), volatile `_innerCts` field, `CancelInnerElection()` method, outer while loop, Gate 1 backoff block, updated XML docs

## Decisions Made

- Concrete `PreferredLeaderService` injected (not interface) because `IsPreferredPod` lives on the concrete type, not `IPreferredStampReader`
- `elector` created once outside loop — no need to recreate; event handler registrations persist across inner restarts
- Backoff `Task.Delay` uses `stoppingToken` directly (not `innerCts.Token`) because `innerCts` is created after the backoff decision
- `_innerCts` declared `volatile` — sufficient for Phase 88's null-conditional cancel from a separate thread; stale null read is a safe no-op
- `CancelInnerElection()` wraps `?.Cancel()` in `try/catch(ObjectDisposedException)` to handle the race window between `_innerCts = null` in finally and `Dispose()` via using

## Deviations from Plan

None — plan executed exactly as written. Both tasks implemented in a single atomic write since they modify the same file and the pseudocode was fully verified in RESEARCH.md.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Phase 87-02: Ready to add unit tests for Gate 1 backoff behavior (stub IPreferredStampReader, mock IKubernetes)
- Phase 88 (voluntary yield): `CancelInnerElection()` method is in place; Phase 88 can call it directly from whatever service triggers voluntary yield
- LeaderElector state after mid-renewal cancellation (open question from STATE.md) still needs Phase 88 investigation — not a blocker for 87-02 tests

---
*Phase: 87-election-gate-1-backoff*
*Completed: 2026-03-26*
