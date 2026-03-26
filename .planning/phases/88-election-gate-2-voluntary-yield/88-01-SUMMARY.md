---
phase: 88-election-gate-2-voluntary-yield
plan: 01
subsystem: infra
tags: [kubernetes, leader-election, preferred-leader, voluntary-yield, k8s-lease, election-gate]

# Dependency graph
requires:
  - phase: 87-election-gate-2-cancel-inner-election
    provides: K8sLeaseElection.CancelInnerElection() — cancels _innerCts to restart election loop
  - phase: 86-preferred-heartbeat-writer
    provides: PreferredHeartbeatJob writer path, _kubeClient, _leaseOptions already injected
  - phase: 85-preferred-heartbeat-reader
    provides: PreferredHeartbeatJob reader path, IsPreferredStampFresh volatile bool

provides:
  - PreferredHeartbeatJob.YieldLeadershipAsync — deletes leadership lease then cancels inner election
  - Gate 2 (ELEC-02) yield condition in Execute() — fires when non-preferred pod holds leadership and preferred stamp becomes fresh
  - Nullable K8sLeaseElection constructor parameter — DI injects singleton, tests pass null safely

affects:
  - phase-89 (E2E testing): voluntary yield is now exercisable end-to-end
  - PreferredHeartbeatJob consumers: constructor gains optional leaseElection param at end

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "delete-then-cancel yield: DeleteNamespacedLeaseAsync first, CancelInnerElection second — mirrors StopAsync precedent"
    - "OperationCanceledException re-thrown in YieldLeadershipAsync matching WriteHeartbeatLeaseAsync and ReadAndUpdateStampFreshnessAsync pattern"
    - "Delete failure logs Warning, cancel fires regardless — ensure election restarts even on K8s API errors"
    - "Nullable optional param at end of constructor — existing tests unmodified, DI injects singleton when available"
    - "Cheapest-first condition ordering: IsPreferredStampFresh (most selective) then null check then IsLeader then !IsPreferredPod"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs

key-decisions:
  - "leaseElection parameter placed AFTER logger (last, optional=null) to preserve existing positional test constructors without modification"
  - "YieldLeadershipAsync is a private helper (not inline) — cleaner separation, easier to test in future"
  - "Uses _leaseOptions.Name (leadership lease) for delete, NOT the -preferred heartbeat lease name"
  - "Delete failure logs Warning level — operator needs to know delete failed but cancel must still fire"

patterns-established:
  - "Gate 2 condition ordering: stamp freshness check first (cheapest), null guard second, leadership check third, preferred-pod exclusion last"

# Metrics
duration: 2min
completed: 2026-03-26
---

# Phase 88 Plan 01: Election Gate 2 Voluntary Yield Summary

**Non-preferred leader voluntarily yields by deleting the leadership lease and cancelling the inner election when the preferred pod's heartbeat stamp becomes fresh (ELEC-02)**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-26T10:26:29Z
- **Completed:** 2026-03-26T10:28:49Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments

- PreferredHeartbeatJob accepts a nullable `K8sLeaseElection?` constructor parameter — existing 17 tests and all 518 suite tests pass unchanged
- Gate 2 (ELEC-02) yield condition inserted in `Execute()` immediately after `ReadAndUpdateStampFreshnessAsync`, checking `IsPreferredStampFresh && _leaseElection is not null && IsLeader && !IsPreferredPod`
- `YieldLeadershipAsync` deletes the leadership lease (`_leaseOptions.Name`) then calls `CancelInnerElection()`, mirroring the delete-then-cancel pattern from `K8sLeaseElection.StopAsync`

## Task Commits

1. **Task 1: Add K8sLeaseElection constructor parameter** - `9224d28` (feat)
2. **Task 2: Add yield condition check and YieldLeadershipAsync helper** - `748e4c8` (feat)

## Files Created/Modified

- `src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs` — added `_leaseElection` field, optional constructor param, Gate 2 condition in `Execute()`, and `YieldLeadershipAsync` private helper; updated class XML doc to mention Gate 2

## Decisions Made

- `leaseElection` parameter placed AFTER `logger` (as last optional param) to preserve existing positional constructor calls in tests — plan said "after liveness and before logger" but that would break 17 existing tests that pass logger positionally at arg-7; placing it last achieves the same DI injection without test modification
- Used `_leaseOptions.Name` (the leadership lease, e.g. `snmp-collector-leader`) for the delete call — NOT the heartbeat lease (`-preferred` suffix)
- `YieldLeadershipAsync` as private helper method rather than inlined in `Execute()` — keeps the gate condition block clean and separates concerns
- Delete failure logs `Warning`, `CancelInnerElection()` fires regardless — election restart must happen even if K8s API rejects the delete

## Deviations from Plan

One minor deviation from the plan's stated parameter ordering:

**Parameter placement: `leaseElection` after `logger` instead of before it**
- **Found during:** Task 1
- **Issue:** Plan specified parameter order "after `ILivenessVectorService liveness` and before `ILogger`", but existing tests pass `NullLogger<PreferredHeartbeatJob>.Instance` as the 7th positional argument. Inserting `leaseElection` at position 7 would pass NullLogger to the `K8sLeaseElection?` param, breaking all 17 tests.
- **Fix:** Placed `K8sLeaseElection? leaseElection = null` as the last constructor parameter (after logger). DI still injects the singleton; tests continue passing NullLogger at position 7 without modification.
- **Verification:** All 518 tests pass with no modifications to test files.
- **Committed in:** `9224d28` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — bug prevention: parameter ordering that would have broken existing tests)
**Impact on plan:** Deviation achieves the same functional goal (DI injection, nullable default) while preserving all existing tests. No scope creep.

## Issues Encountered

None — straightforward implementation once parameter ordering was resolved.

## Next Phase Readiness

- Gate 2 (ELEC-02) voluntary yield is fully implemented in `PreferredHeartbeatJob`
- Phase 89 (E2E testing) can now exercise the full preferred leader election flow end-to-end
- No blockers — K8sLeaseElection singleton will be injected by DI in production; test scenarios use `leaseElection = null` default

---
*Phase: 88-election-gate-2-voluntary-yield*
*Completed: 2026-03-26*
