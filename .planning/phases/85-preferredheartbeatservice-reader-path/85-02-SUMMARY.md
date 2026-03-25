---
phase: 85-preferredheartbeatservice-reader-path
plan: 02
subsystem: testing
tags: [kubernetes, quartz, lease-election, preferred-leader, nsubstitute, unit-tests, freshness]

# Dependency graph
requires:
  - phase: 85-01
    provides: PreferredHeartbeatJob, UpdateStampFreshness, volatile bool _isPreferredStampFresh
provides:
  - Unit tests for UpdateStampFreshness (5 tests: initial false, set true, set false, transitions, idempotent)
  - Unit tests for PreferredHeartbeatJob (8 tests: fresh, stale, threshold, 404, transient error, null timestamps, acquireTime fallback, liveness)
  - SC-4 verified: IsPreferredStampFresh returns real derived value from mocked K8s lease response
affects:
  - phase-86-writer-path (test patterns established here reusable for writer path tests)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Mock ICoordinationV1Operations.ReadNamespacedLeaseWithHttpMessagesAsync (underlying interface method), not the extension ReadNamespacedLeaseAsync — NSubstitute cannot mock static extension methods"
    - "HttpOperationResponse<V1Lease> with Body set to mock lease returned from WithHttpMessages mock"
    - "HttpOperationException 404 constructed with HttpResponseMessageWrapper(new HttpResponseMessage(NotFound), '')"
    - "Threshold timing tests use threshold-1s (not exact threshold) to avoid wall-clock race between stub computation and job execution"
    - "StubJobContext pattern: minimal IJobExecutionContext implementation using JobBuilder.Create<T>().WithIdentity()"

key-files:
  created:
    - tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs
  modified:
    - tests/SnmpCollector.Tests/Telemetry/PreferredLeaderServiceTests.cs

key-decisions:
  - "Mock ReadNamespacedLeaseWithHttpMessagesAsync not ReadNamespacedLeaseAsync — extension methods cannot be intercepted by NSubstitute"
  - "Threshold test uses threshold-1s offset to prevent wall-clock timing race; documents this explicitly in test name and comment"
  - "StubLivenessVectorService from HeartbeatJobTests replaced by NSubstitute mock (ILivenessVectorService) since verification via Received() is needed"

patterns-established:
  - "K8s interface mocking: substitute ICoordinationV1Operations, wire via kubeClient.CoordinationV1.Returns(mockCoordV1.Object)"
  - "Transient error keep-last-value: test by setting known state first, then throwing generic exception, then asserting state unchanged"

# Metrics
duration: 15min
completed: 2026-03-26
---

# Phase 85 Plan 02: PreferredHeartbeatService Reader Path Tests Summary

**13 new unit tests cover UpdateStampFreshness state transitions and all PreferredHeartbeatJob lease-read scenarios (fresh, stale, 404, transient error, null timestamps, acquireTime fallback, liveness) using NSubstitute-mocked IKubernetes; full suite at 500 tests**

## Performance

- **Duration:** 15 min
- **Started:** 2026-03-26T00:00:00Z
- **Completed:** 2026-03-26T00:15:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Extended `PreferredLeaderServiceTests` with 5 `UpdateStampFreshness` tests: initial false, set true, set false, fresh-to-stale transition, and idempotent same-value call
- Created `PreferredHeartbeatJobTests` with 8 tests using NSubstitute-mocked `IKubernetes` + `ILivenessVectorService`; SC-4 explicitly satisfied (real derived value from mocked lease, not a stub)
- Full test suite passes at 500 tests (487 pre-existing + 13 new)

## Task Commits

Each task was committed atomically:

1. **Task 1: UpdateStampFreshness tests** - `d83c80e` (test)
2. **Task 2: PreferredHeartbeatJob unit tests** - `133a87a` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/SnmpCollector.Tests/Telemetry/PreferredLeaderServiceTests.cs` — 5 new tests (9-13): initial false, set true, set false, fresh-to-stale, idempotent no-op
- `tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs` — new file; 8 tests with mocked IKubernetes via NSubstitute; SetupLeaseResponse/SetupLeaseThrows helpers; Make404Exception utility

## Decisions Made

- Mock `ReadNamespacedLeaseWithHttpMessagesAsync` (not the extension `ReadNamespacedLeaseAsync`) — NSubstitute cannot intercept static extension methods; the extension delegates to the interface method automatically
- Threshold boundary test uses `DurationSeconds + 5 - 1` seconds offset (not exact threshold) to avoid wall-clock race between stub computation and job execution time
- Use `Substitute.For<ILivenessVectorService>()` rather than a hand-rolled stub to leverage `Received()` verification for liveness stamping assertions

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Threshold boundary test timing race**
- **Found during:** Task 2 (Execute_WithLeaseAtExactThreshold_SetsStampFreshTrue)
- **Issue:** Computing `UtcNow.AddSeconds(-threshold)` before job execution meant the age exceeded threshold by a few ms at execution time, causing the test to fail flakily
- **Fix:** Changed to `UtcNow.AddSeconds(-(threshold - 1))` — 1 second inside the threshold — with updated test name `Execute_WithLeaseJustInsideThreshold_SetsStampFreshTrue` and explanatory comment
- **Files modified:** tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs
- **Verification:** Test passes consistently; behavior under test (age <= threshold = true) is preserved
- **Committed in:** 133a87a (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - timing bug in test)
**Impact on plan:** Fix necessary for test correctness; semantics preserved (testing that a lease inside the threshold window is fresh).

## Issues Encountered

- `ReadNamespacedLeaseAsync` is a static extension method on `ICoordinationV1Operations` — cannot be directly mocked. Resolved by mocking the underlying `ReadNamespacedLeaseWithHttpMessagesAsync` interface method, which the extension calls internally.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- SC-4 satisfied: verified by unit test that `IsPreferredStampFresh` returns a real derived value from a mocked lease response, not a compile-time stub
- Phase 85 fully complete: both reader path implementation (85-01) and its tests (85-02) are committed
- Phase 86 (writer path): test patterns established here (K8s mock setup, StubJobContext, NSubstitute `Received()` verification) directly reusable

---
*Phase: 85-preferredheartbeatservice-reader-path*
*Completed: 2026-03-26*
