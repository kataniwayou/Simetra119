---
phase: 86-preferredheartbeatservice-writer-path
plan: 02
subsystem: testing
tags: [xunit, nsubstitute, k8s, lease, heartbeat, writer-path, preferred-leader]

# Dependency graph
requires:
  - phase: 86-01
    provides: PreferredHeartbeatJob with new constructor (IOptions<PodIdentityOptions>, IHostApplicationLifetime) and WriteHeartbeatLeaseAsync writer path
provides:
  - 17 unit tests covering all writer-path and reader-path scenarios for PreferredHeartbeatJob
  - Constructor fixed for IOptions<PodIdentityOptions> and IHostApplicationLifetime params
  - Writer-path tests verifying HB-01 (lease stamped with identity/renewTime), HB-02 (readiness gate), create/replace/conflict patterns
affects: [phase-87, phase-88, phase-89]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "MakeLifetime(alreadyStarted) helper: pre-cancelled CancellationTokenSource simulates ApplicationStarted"
    - "MakePreferredJob helper: sets PHYSICAL_HOSTNAME env var + PreferredNode to enable IsPreferredPod, caller restores env var in finally"
    - "WithHttpMessagesAsync mocking for Create/Replace K8s extension methods"
    - "Arg.Do<V1Lease> capture pattern for verifying lease body fields (AcquireTime present/absent)"

key-files:
  created: []
  modified:
    - tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs

key-decisions:
  - "Use pre-cancelled CancellationTokenSource for MakeLifetime(alreadyStarted: true) so Register callback fires synchronously in constructor"
  - "Restore PHYSICAL_HOSTNAME in finally blocks on each writer test to prevent test pollution"
  - "Mock CreateNamespacedLeaseWithHttpMessagesAsync and ReplaceNamespacedLeaseWithHttpMessagesAsync (underlying interface methods that extension methods delegate to)"
  - "Test cache invalidation by running 3 ticks: create (cache set), conflict (cache cleared), create again (verified via ClearReceivedCalls)"

patterns-established:
  - "Pattern: Test _isSchedulerReady via lifetime.ApplicationStarted pre-cancelled token, not direct field mutation"
  - "Pattern: Test IsPreferredPod via PHYSICAL_HOSTNAME env var + LeaseOptions.PreferredNode, clean up in finally"

# Metrics
duration: 15min
completed: 2026-03-26
---

# Phase 86 Plan 02: PreferredHeartbeatJob Writer-Path Tests Summary

**17 unit tests covering PreferredHeartbeatJob writer path: create/replace lease, readiness gate, non-preferred skip, 409/404 conflict cache invalidation, transient error handling, and AcquireTime-only-on-create invariant**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-26T13:06:48Z
- **Completed:** 2026-03-26T13:21:00Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Updated constructor call to pass `IOptions<PodIdentityOptions>` and `IHostApplicationLifetime` — all 8 existing reader tests pass unchanged
- Added 9 new writer-path tests covering every scenario from the plan (create, renew, readiness gate, non-preferred, 409 create conflict, 409 replace, 404 replace, transient error, AcquireTime invariant)
- Full test suite green: 509 tests, 0 failures

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix existing test constructor and add writer-path unit tests** - `c617322` (test)

## Files Created/Modified
- `tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs` - Updated constructor, added 9 writer-path tests, added MakeLifetime/MakePreferredJob helpers, added SetupCreate/SetupReplace mock helpers

## Decisions Made
- Used a pre-cancelled `CancellationTokenSource` in `MakeLifetime(alreadyStarted: true)` so the `Register` callback in the constructor fires synchronously — simulates `ApplicationStarted` without relying on timing
- PHYSICAL_HOSTNAME env var set/cleared in finally blocks per test to prevent cross-test pollution
- Mocked the `WithHttpMessagesAsync` interface methods (not the extension methods) — same pattern as existing reader tests
- Cache invalidation tests use 3 ticks with `ClearReceivedCalls()` between tick 2 and tick 3 to isolate the assertion

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 86 writer-path fully tested (HB-01, HB-02, HB-03 verified at unit level)
- Phase 87 (integration or E2E validation) can proceed
- Phase 88 (voluntary yield) can proceed — LeaderElector mid-renewal behavior still unconfirmed per STATE.md blocker

---
*Phase: 86-preferredheartbeatservice-writer-path*
*Completed: 2026-03-26*
