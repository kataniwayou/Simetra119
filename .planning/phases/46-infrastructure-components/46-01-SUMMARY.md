---
phase: 46-infrastructure-components
plan: 01
subsystem: pipeline
tags: [suppression-cache, tenant-config, validation, di]
dependency_graph:
  requires: [44-pipeline-liveness]
  provides: [ISuppressionCache, SuppressionWindowSeconds, ValueType-parse-validation]
  affects: [47-snapshot-job-options, 48-snapshot-job]
tech_stack:
  added: []
  patterns: [ConcurrentDictionary-based-cache, lazy-TTL-expiry, config-time-parse-validation]
key_files:
  created:
    - src/SnmpCollector/Pipeline/ISuppressionCache.cs
    - src/SnmpCollector/Pipeline/SuppressionCache.cs
    - tests/SnmpCollector.Tests/Pipeline/SuppressionCacheTests.cs
  modified:
    - src/SnmpCollector/Configuration/TenantOptions.cs
    - src/SnmpCollector/Pipeline/Tenant.cs
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - src/SnmpCollector/Services/TenantVectorWatcherService.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs
decisions:
  - id: SUP-01
    decision: "SuppressionCache uses ConcurrentDictionary<string, DateTimeOffset> with lazy TTL (no background sweep)"
    reason: "Matches LivenessVectorService pattern; dead entries expire naturally"
  - id: SUP-02
    decision: "Window passed at check time, not stored in entry"
    reason: "Allows tenant config changes to take effect immediately on next check"
  - id: SUP-03
    decision: "Suppressed calls do NOT re-stamp timestamp"
    reason: "Window does not extend on repeated suppressed calls — prevents indefinite suppression"
metrics:
  duration: ~10 min
  completed: 2026-03-16
---

# Phase 46 Plan 01: ISuppressionCache + SuppressionWindowSeconds + Value Parse Validation Summary

ConcurrentDictionary-based suppression cache with lazy TTL expiry, TenantOptions.SuppressionWindowSeconds (default 60) propagated to immutable Tenant property, and Integer32/IpAddress Value parse validation at config load time.

## What Was Done

### Task 1: ISuppressionCache + SuppressionCache + Config + Validation
- Created `ISuppressionCache` interface with `TrySuppress(key, windowSeconds)` and `Count` properties
- Created `SuppressionCache` sealed class using `ConcurrentDictionary<string, DateTimeOffset>`
- Added `TenantOptions.SuppressionWindowSeconds` with default 60
- Added `Tenant.SuppressionWindowSeconds` as immutable property (4th constructor parameter)
- Updated `TenantVectorRegistry.Reload` to pass `tenantOpts.SuppressionWindowSeconds` to `new Tenant()`
- Added Value+ValueType parse validation in `TenantVectorWatcherService.ValidateAndBuildTenants`: Integer32 via `int.TryParse`, IpAddress via `IPAddress.TryParse`, OctetString passes any non-empty value
- Registered `ISuppressionCache` as singleton in `ServiceCollectionExtensions.AddSnmpPipeline`
- Commit: `417e2af`

### Task 2: Tests
- 7 SuppressionCache behavioral tests: first-call returns false, second-within-window returns true, after-expiry returns false, different-keys independent, window-at-check-time not stored, suppressed-call does not update timestamp, count reflects entries
- 4 Value+ValueType validation tests: invalid Integer32 skipped, invalid IpAddress skipped, OctetString any value accepted, valid Integer32 accepted
- All 349 tests pass (11 new + 338 existing)
- Commit: `5b5df4f`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Copied HealthCheckJsonWriter.cs from main worktree**
- **Found during:** Task 1 build verification
- **Issue:** `HealthCheckJsonWriter.cs` was untracked in the main repo and missing from this worktree, causing CS0103 build errors
- **Fix:** Copied the file from the main repo to the worktree and included in Task 1 commit
- **Files modified:** `src/SnmpCollector/HealthChecks/HealthCheckJsonWriter.cs`

## Decisions Made

| ID | Decision | Reason |
|----|----------|--------|
| SUP-01 | ConcurrentDictionary + lazy TTL (no sweep) | Matches LivenessVectorService pattern |
| SUP-02 | Window passed at check time, not stored | Config changes take effect immediately |
| SUP-03 | Suppressed calls do NOT re-stamp | Prevents indefinite suppression |

## Next Phase Readiness

- ISuppressionCache is ready for injection into SnapshotJob (Phase 48)
- Tenant.SuppressionWindowSeconds is ready for use by SnapshotJob to pass per-tenant windows
- Value+ValueType parse validation ensures CommandWorker receives pre-validated data
