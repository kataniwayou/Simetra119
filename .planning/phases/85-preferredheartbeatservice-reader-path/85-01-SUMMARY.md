---
phase: 85-preferredheartbeatservice-reader-path
plan: 01
subsystem: infra
tags: [kubernetes, quartz, lease-election, preferred-leader, volatile-bool, freshness]

# Dependency graph
requires:
  - phase: 84-preferredleaderservice
    provides: PreferredLeaderService singleton with IsPreferredPod, IPreferredStampReader interface
  - phase: 83-lease-options-preferred-node
    provides: LeaseOptions.PreferredNode, LeaseOptions.DurationSeconds
provides:
  - PreferredHeartbeatJobOptions configuration class (SectionName, IntervalSeconds)
  - PreferredHeartbeatJob Quartz job — reads heartbeat lease, computes freshness, updates volatile bool
  - PreferredLeaderService.UpdateStampFreshness(bool) method with transition logging
  - volatile bool _isPreferredStampFresh wired to IsPreferredStampFresh property
  - DI registration of PreferredHeartbeatJob inside IsInCluster guard
affects:
  - phase-86-writer-path (will add writer path to same PreferredHeartbeatJob)
  - phase-87-k8s-lease-election-gate (reads IPreferredStampReader.IsPreferredStampFresh)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Quartz job injects concrete PreferredLeaderService (not interface) to call write method UpdateStampFreshness"
    - "Reader job registered only inside IsInCluster guard (IKubernetes only available in cluster)"
    - "Keep-last-value semantics on transient K8s API errors — only 404 and null stamp yield stale"
    - "Freshness threshold = DurationSeconds + 5s (hardcoded clock-skew tolerance)"
    - "UTC normalization: DateTime.SpecifyKind(stampTime.Value, DateTimeKind.Utc) before comparison"

key-files:
  created:
    - src/SnmpCollector/Configuration/PreferredHeartbeatJobOptions.cs
    - src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs
  modified:
    - src/SnmpCollector/Telemetry/PreferredLeaderService.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/appsettings.json

key-decisions:
  - "Inject concrete PreferredLeaderService into job, not IPreferredStampReader — job needs to WRITE via UpdateStampFreshness"
  - "404 from K8s lease read = stale (not an error, just absent lease)"
  - "Transient errors keep last known value — avoids flapping on temporary API unavailability"
  - "DurationSeconds + 5s threshold (not configurable) — clock-skew tolerance baked in"

patterns-established:
  - "concrete-first injection: job gets PreferredLeaderService directly for write access; downstream readers get IPreferredStampReader via DI"

# Metrics
duration: 2min
completed: 2026-03-26
---

# Phase 85 Plan 01: PreferredHeartbeatService Reader Path Summary

**Quartz job reads Kubernetes heartbeat lease and updates volatile bool in PreferredLeaderService with DurationSeconds+5s freshness threshold and keep-last-value semantics on transient errors**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-25T22:23:20Z
- **Completed:** 2026-03-25T22:25:30Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- PreferredHeartbeatJob reads heartbeat lease via `CoordinationV1.ReadNamespacedLeaseAsync`, computes age against `DurationSeconds + 5s` threshold, and calls `UpdateStampFreshness`
- 404 response handled as stale without exception; transient errors keep last known value with Warning log
- PreferredLeaderService updated: `volatile bool _isPreferredStampFresh` wired to `IsPreferredStampFresh`, `UpdateStampFreshness(bool)` logs transitions at Info level
- Job registered exclusively inside `IsInCluster()` guard; `initialJobCount` conditionally incremented for thread pool sizing

## Task Commits

Each task was committed atomically:

1. **Task 1: Options class, PreferredLeaderService volatile bool, and appsettings** - `1af7e54` (feat)
2. **Task 2: PreferredHeartbeatJob and DI registration** - `5ec02b4` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/SnmpCollector/Configuration/PreferredHeartbeatJobOptions.cs` — `SectionName = "PreferredHeartbeatJob"`, `IntervalSeconds` with `[Range(1, int.MaxValue)]`, default 15
- `src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs` — Quartz job with `[DisallowConcurrentExecution]`, lease read, 404 handling, freshness threshold, keep-last-value semantics, liveness stamp
- `src/SnmpCollector/Telemetry/PreferredLeaderService.cs` — added `volatile bool _isPreferredStampFresh`, wired property, added `UpdateStampFreshness(bool)` with transition logging, stored `_logger` field
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — added `PreferredHeartbeatJobOptions` registration in `AddSnmpConfiguration`; options bind + IsInCluster job registration block + conditional `initialJobCount++` in `AddSnmpScheduling`
- `src/SnmpCollector/appsettings.json` — added `"PreferredHeartbeatJob": { "IntervalSeconds": 15 }`

## Decisions Made

- Inject concrete `PreferredLeaderService` into `PreferredHeartbeatJob` (not `IPreferredStampReader`) — job needs write access via `UpdateStampFreshness`, which is not on the reader interface
- Use `renewTime ?? acquireTime` for stamp time — handles newly created lease where only `acquireTime` is set
- UTC normalization via `DateTime.SpecifyKind(..., DateTimeKind.Utc)` on the `MicroTime` value before comparison

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Reader path complete: all pods (preferred and non-preferred) will poll the heartbeat lease and maintain `IsPreferredStampFresh` once deployed in K8s
- Phase 86 (writer path): `PreferredHeartbeatJob` already exists — Phase 86 adds the `if (IsPreferredPod)` write branch using `CoordinationV1.CreateOrReplaceNamespacedLeaseAsync`. Readiness gate mechanism still unresolved (three options: ApplicationStarted, IHealthCheckService poll, TaskCompletionSource<bool>).
- Phase 87 (K8sLeaseElection gate): can now read `IPreferredStampReader.IsPreferredStampFresh` to gate the advance decision

---
*Phase: 85-preferredheartbeatservice-reader-path*
*Completed: 2026-03-26*
