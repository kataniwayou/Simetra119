---
phase: 88-election-gate-2-voluntary-yield
plan: 02
subsystem: testing
tags: [kubernetes, leader-election, preferred-leader, voluntary-yield, unit-tests, nsubstitute, reflection]

# Dependency graph
requires:
  - phase: 88-election-gate-2-voluntary-yield/88-01
    provides: YieldLeadershipAsync, Gate 2 condition in Execute(), nullable K8sLeaseElection? constructor param
  - phase: 86-preferred-heartbeat-writer
    provides: MakePreferredJob helper, SetupCreateResponse, SetupReplaceResponse patterns
  - phase: 85-preferred-heartbeat-reader
    provides: SetupLeaseResponse, FreshLease, existing 17 tests, test infrastructure

provides:
  - 6 yield path unit tests for Gate 2 (ELEC-02) in PreferredHeartbeatJobTests
  - MakeNonPreferredJobWithElection helper — real K8sLeaseElection backed by shared mock IKubernetes
  - SetIsLeader helper — reflection sets _isLeader on sealed K8sLeaseElection
  - SetupDeleteSucceeds / SetupDeleteThrows — mock DeleteNamespacedLeaseWithHttpMessagesAsync (11-param)

affects:
  - phase-89 (E2E): yield behavior verified at unit level, ready for end-to-end exercise

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Reflection to set private volatile field on sealed class: BindingFlags.NonPublic | BindingFlags.Instance"
    - "MakeNonPreferredJobWithElection: PHYSICAL_HOSTNAME = other-node, PreferredLeaderService as IPreferredStampReader (dual role)"
    - "SetupDeleteSucceeds / SetupDeleteThrows: mock 11-parameter DeleteNamespacedLeaseWithHttpMessagesAsync via NSubstitute"
    - "Negative test per condition: each of the four Gate 2 conditions tested independently"

key-files:
  created: []
  modified:
    - tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs

key-decisions:
  - "Reflection to set _isLeader: preferred over [InternalsVisibleTo] — avoids production code modification for test purposes only"
  - "PreferredLeaderService passed as IPreferredStampReader to K8sLeaseElection: real type implements interface, avoids need for a second mock"
  - "Test 21 (preferred pod): rebuilds job with leaseElection rather than modifying MakePreferredJob — preserves existing helper signature"
  - "Test 22 (null leaseElection): uses default _job (no election injected) — proves null guard works without extra construction"

patterns-established:
  - "Sealed class test pattern: real instance + reflection for private state, no [InternalsVisibleTo] modification needed"
  - "Negative-condition coverage: one test per condition ensures Gate 2 four-part AND is complete and correct"

# Metrics
duration: 2min
completed: 2026-03-26
---

# Phase 88 Plan 02: Yield Path Unit Tests Summary

**6 yield path tests covering Gate 2 (ELEC-02) happy path, all four negative conditions, and delete failure resilience — using reflection to set _isLeader on sealed K8sLeaseElection**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-26T00:11:17Z
- **Completed:** 2026-03-26T00:13:06Z
- **Tasks:** 2 (helpers + tests in one logical unit)
- **Files modified:** 1

## Accomplishments

- Added `MakeNonPreferredJobWithElection` helper that builds a non-preferred job with a real `K8sLeaseElection` backed by the shared `_mockKubeClient` — PHYSICAL_HOSTNAME set to "other-node" so IsPreferredPod = false
- Added `SetIsLeader` reflection helper to set `_isLeader` on the sealed `K8sLeaseElection` class, enabling the positive yield test without modifying production code
- Added `SetupDeleteSucceeds` and `SetupDeleteThrows` helpers for the 11-parameter `DeleteNamespacedLeaseWithHttpMessagesAsync` overload
- 6 new yield tests: happy path (delete fires), stamp stale, not leader, preferred pod, null election, delete failure — total test count 518 → 524

## Task Commits

1. **Tasks 1 & 2: Add yield helpers and tests** - `6ed6821` (test)

## Files Created/Modified

- `tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs` — added `using System.Reflection`, updated XML doc summary, added 3 helpers and 6 yield tests (tests 18–23)

## Decisions Made

- Used reflection to set `_isLeader` rather than adding `[InternalsVisibleTo]` — keeps production code unmodified; reflection is acceptable for testing sealed classes where public API cannot set internal state
- `PreferredLeaderService` implements `IPreferredStampReader`, so it is passed as both parameters to `K8sLeaseElection` constructor — no second mock needed
- Test 21 (preferred pod does not yield) builds a fresh job instance with election rather than changing `MakePreferredJob` signature — preserves existing helper for existing tests
- Test 22 (null leaseElection) reuses the default `_job` field — cleanest approach, proves the null guard without extra setup

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed incorrect K8sLeaseElection constructor call in plan helper**

- **Found during:** Task 1 (reading actual K8sLeaseElection.cs constructor)
- **Issue:** Plan's `MakeNonPreferredJobWithElection` snippet used wrong constructor parameter order and incorrectly called `Substitute.For<PreferredLeaderService>()` on a sealed class
- **Fix:** Used actual constructor signature `(IOptions<LeaseOptions>, IOptions<PodIdentityOptions>, IKubernetes, IHostApplicationLifetime, ILogger, PreferredLeaderService, IPreferredStampReader)` with a real `PreferredLeaderService` serving both roles
- **Files modified:** tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs
- **Verification:** Build succeeds, all 524 tests pass
- **Committed in:** `6ed6821` (task commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — bug in plan's illustrative code snippet)
**Impact on plan:** Deviation achieves identical functional goal. The plan explicitly noted the helper was "illustrative — match the actual constructor signature precisely." No scope creep.

## Issues Encountered

None — reflection approach for `_isLeader` worked as expected. `BindingFlags.NonPublic | BindingFlags.Instance` locates the volatile bool field without issues.

## Next Phase Readiness

- All 6 Gate 2 yield tests pass — yield behavior is unit-verified at every condition boundary
- Phase 89 (E2E testing) can proceed with confidence that the yield logic is correct
- No blockers

---
*Phase: 88-election-gate-2-voluntary-yield*
*Completed: 2026-03-26*
