---
phase: 86-preferredheartbeatservice-writer-path
plan: 01
subsystem: infra
tags: [kubernetes, lease, heartbeat, preferred-leader, quartz, IHostApplicationLifetime]

# Dependency graph
requires:
  - phase: 85-preferredheartbeatservice-reader-path
    provides: PreferredHeartbeatJob reader path and PreferredLeaderService.UpdateStampFreshness
  - phase: 84-preferredleaderservice
    provides: PreferredLeaderService.IsPreferredPod, PodIdentityOptions, LeaseOptions.PreferredNode
provides:
  - PreferredHeartbeatJob writer path — preferred pod creates/renews snmp-collector-preferred lease
  - Readiness gate via IHostApplicationLifetime.ApplicationStarted.Register callback
  - Write-before-read Execute ordering (preferred pod stamps, then reads its own stamp)
affects:
  - 86-02 (test updates for new constructor params)
  - 87 (K8sLeaseElection IPreferredStampReader gate integration)
  - 89 (E2E: preferred pod heartbeat visible in cluster)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Create-or-replace with cached resourceVersion: try create, 409 -> read for resourceVersion, replace"
    - "IHostApplicationLifetime.ApplicationStarted.Register for scheduler readiness gate (volatile bool)"
    - "AcquireTime set only on first create, not on subsequent replacements (renewals)"
    - "Non-preferred pod silent skip: no log output when IsPreferredPod is false"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs

key-decisions:
  - "Readiness gate on job via volatile bool (not on PreferredLeaderService) — job-lifecycle state, not pod-identity state"
  - "Cache resourceVersion on job instance — avoid extra read per tick; null cache handled gracefully (409 -> read)"
  - "AcquireTime cleared to null in the 409-conflict fallthrough to replace path — preserves original acquisition timestamp"
  - "Transient write errors caught inside WriteHeartbeatLeaseAsync as Warning; OperationCanceledException propagates"
  - "TTL expiry handles shutdown; no explicit lease delete added to GracefulShutdownService"

patterns-established:
  - "WriteHeartbeatLeaseAsync: private method parallel to ReadAndUpdateStampFreshnessAsync"
  - "Execute write-before-read: writer conditional on IsPreferredPod && _isSchedulerReady, then unconditional reader"

# Metrics
duration: 2min
completed: 2026-03-26
---

# Phase 86 Plan 01: PreferredHeartbeatService Writer Path Summary

**Preferred pod now creates and renews the snmp-collector-preferred K8s lease on every tick, gated by ApplicationStarted readiness and IsPreferredPod, using cached resourceVersion for optimistic-concurrency-safe renewals.**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-25T23:03:04Z
- **Completed:** 2026-03-25T23:05:15Z
- **Tasks:** 1 of 1
- **Files modified:** 1

## Accomplishments

- Added WriteHeartbeatLeaseAsync with create-or-replace pattern and cached resourceVersion
- Added IHostApplicationLifetime.ApplicationStarted.Register callback setting _isSchedulerReady volatile bool
- Restructured Execute to write-before-read: preferred pod stamps lease then reads its own stamp
- Non-preferred pods silently skip the write path (no log)
- Build succeeds with 0 errors and 0 warnings; all 8 existing reader tests still pass

## Task Commits

1. **Task 1: Add writer path, readiness gate, and restructure Execute** - `bed3dda` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs` - Added writer path, readiness gate, IHostApplicationLifetime + IOptions<PodIdentityOptions> injection, write-before-read Execute, WriteHeartbeatLeaseAsync private method

## Decisions Made

- Readiness gate placed on the job as `volatile bool _isSchedulerReady` (not on PreferredLeaderService) — readiness is scheduler-lifecycle state, not pod-identity state.
- ResourceVersion cached as `string? _cachedResourceVersion` on the job instance. Null cache is safe: 409 on create triggers a read-then-replace recovery path. Per-tick cost on cache hit: zero extra reads.
- AcquireTime set to `now` on the create branch, then cleared to `null` in the 409-fallthrough path so the replace branch does not overwrite the original acquisition timestamp.
- All transient write errors (not 409/404/OperationCanceled) caught inside WriteHeartbeatLeaseAsync as Warning — consistent with reader path error handling.

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 86-02: Constructor signature changed (added IHostApplicationLifetime and IOptions<PodIdentityOptions>). Existing reader tests still pass (8/8) but test setup may need updating for new params — addressed in Plan 02.
- Plan 87 (K8sLeaseElection gate): PreferredLeaderService.IsPreferredStampFresh is populated by the reader path; preferred pod confirms its own stamp freshness via write-before-read. Gate integration is ready.

---
*Phase: 86-preferredheartbeatservice-writer-path*
*Completed: 2026-03-26*
