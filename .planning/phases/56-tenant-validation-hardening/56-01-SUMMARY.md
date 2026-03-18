---
phase: 56-tenant-validation-hardening
plan: 01
subsystem: tenant-validation
tags: [validation, tenant, threshold, suppression, interval, ip-resolution]
dependency-graph:
  requires: []
  provides: [tenant-validation-hardening, suppression-window-validation, interval-zero-skip, ip-resolution-skip, threshold-min-max-skip, timeseries-cap]
  affects: [56-02]
tech-stack:
  added: []
  patterns: [per-entry-skip-semantics, interval-aware-clamping]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Services/TenantVectorWatcherService.cs
    - src/SnmpCollector/Program.cs
    - tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs
decisions:
  - threshold-min-max-skip: "Threshold Min>Max now SKIPS metric (was clear+keep) for consistency with all other Error-level checks"
  - interval-zero-skip: "IntervalSeconds=0 after resolution SKIPS metric (Error+continue) since unresolvable interval is genuinely broken"
  - passthrough-test-helpers: "Updated CreatePassthroughDeviceRegistry and CreatePassthroughOidMapService to return real devices with poll groups for interval resolution"
metrics:
  duration: ~9 min
  completed: 2026-03-18
---

# Phase 56 Plan 01: Tenant Validation Hardening Summary

**One-liner:** Point fixes and new validation checks in ValidateAndBuildTenants -- threshold skip, TimeSeriesSize cap, IP resolution skip, IntervalSeconds=0 skip, SuppressionWindowSeconds clamping with snapshotIntervalSeconds parameter.

## Changes Made

### Task 1: Point fixes + threshold skip + IP skip + suppression validation

- Added `snapshotIntervalSeconds` parameter to `ValidateAndBuildTenants` method signature
- Wired `IOptions<SnapshotJobOptions>` into `TenantVectorWatcherService` constructor
- Updated call sites in `HandleConfigMapChangedAsync` and `Program.cs` (local dev)
- Renumbered validation comment steps sequentially (1-12 for metrics, 1-9 for commands)
- Changed Threshold Min>Max from clear+keep to skip (Error + continue)
- Fixed threshold log parameter names from `{TenantName}/{MetricIndex}` to `{TenantId}/{Index}`
- Added TimeSeriesSize > 1000 cap (skip metric)
- Added IntervalSeconds=0 check after resolution (skip metric)
- Added IP resolution failure check (hostname not in AllDevices and not valid IP -> skip)
- Added SuppressionWindowSeconds validation: -1 accepted, 0 clamped to interval, <-1 clamped, 1..interval-1 clamped

### Task 2: Unit tests

- Updated all existing `ValidateAndBuildTenants` calls with `snapshotIntervalSeconds=15`
- Updated `CreatePassthroughDeviceRegistry` to return real device with poll group (IntervalSeconds=10)
- Updated `CreatePassthroughOidMapService` to return OID for poll group resolution
- Fixed 3 existing tests for behavioral changes (threshold skip, interval-zero skip, poll-group-not-found skip)
- Added 7 new tests: ThresholdMinGreaterThanMax, TimeSeriesSizeExceedsMax, IpNotResolved, IntervalSecondsZero, SuppressionWindowZero, SuppressionWindowNegativeOne, SuppressionWindowBelowInterval
- All 446 tests pass (40 validation tests: 33 existing + 7 new)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CA2017 warning in SuppressionWindowSeconds log template**
- **Found during:** Task 1
- **Issue:** Log template had duplicate `{Interval}` placeholder, causing CA2017 (parameter count mismatch)
- **Fix:** Changed second `{Interval}` to literal text "interval" in the "clamped to" suffix
- **Files modified:** TenantVectorWatcherService.cs
- **Commit:** 78743c1

**2. [Rule 2 - Missing Critical] Test passthrough helpers needed real device/OID data**
- **Found during:** Task 2
- **Issue:** IntervalSeconds=0 skip caused 10 existing tests to fail because passthrough DeviceRegistry returned no device info and OidMapService returned no OIDs, leaving interval at 0
- **Fix:** Updated CreatePassthroughDeviceRegistry to return real DeviceInfo with poll group containing PassthroughOid; updated CreatePassthroughOidMapService to resolve all metric names to PassthroughOid
- **Files modified:** TenantVectorWatcherValidationTests.cs
- **Commit:** 1a5ae7e

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Threshold Min>Max skips metric (not clear+keep) | Consistent with all other Error-level checks that skip the metric via `continue` |
| IntervalSeconds=0 skips metric | If MetricName passed TEN-05 but can't resolve interval from any poll group, the metric is genuinely broken |
| Passthrough test helpers return real devices | New IntervalSeconds=0 check requires device with poll group for OID-to-interval resolution in tests |

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 78743c1 | feat | Point fixes + threshold skip + IP skip + suppression validation |
| 1a5ae7e | test | Unit tests for tenant validation hardening (7 new, 33 updated) |

## Next Phase Readiness

Plan 56-02 (duplicate detection, command existence warning) can proceed. The renumbered comment slots 10 (duplicate metric detection) and 8/9 (duplicate command detection, CommandName existence warning) are reserved for 56-02.
