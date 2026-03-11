---
phase: quick
plan: 047
subsystem: pipeline
tags: [tenant-vector, heartbeat, routing]
completed: 2026-03-11
duration: ~3 minutes
dependency-graph:
  requires: [Q041-heartbeat-counter32, Q042-remove-intervalseconds, 27-tenant-vector-registry]
  provides: [heartbeat-tenant-vector-slot]
  affects: [e2e-heartbeat-verification]
tech-stack:
  added: []
  patterns: [hardcoded-tenant-injection, device-bypass-routing]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
---

# Quick Task 047: Hardcode Heartbeat as Highest Priority Tenant Summary

Heartbeat tenant hardcoded at int.MinValue priority in TenantVectorRegistry, with DeviceRegistry bypass in fan-out behavior, ensuring heartbeat metrics always route to a dedicated slot.

## What Was Done

### Task 1: Inject hardcoded heartbeat tenant in TenantVectorRegistry.Reload
- Added `DefaultIntervalSeconds` const (15) to HeartbeatJobOptions
- Heartbeat tenant injected at int.MinValue priority before ConfigMap tenant loop
- Heartbeat slot values carry over across Reload() cycles
- int.MinValue priority guarded: ConfigMap tenants bumped to int.MinValue + 1 with warning log
- TenantCount includes heartbeat (+1 over ConfigMap count)
- Commit: `2892581`

### Task 2: Bypass DeviceRegistry lookup for heartbeat in TenantVectorFanOutBehavior
- Added HeartbeatDeviceName check before DeviceRegistry lookup
- Heartbeat routes directly via (127.0.0.1, 0, metricName) without DeviceRegistry
- Existing DeviceRegistry lookup became else-if branch, unchanged
- Commit: `bf4dc5c`

### Task 3: Add heartbeat tenant tests and fix existing assertions
- 5 new tests: empty config heartbeat exists, first group ordering, TryRoute routing, value carry-over, priority bump
- Fixed 5 existing tests for heartbeat presence (Groups count +1, index shift)
- All 199 tests pass
- Commit: `76b51d0`

## Decisions Made

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | int.MinValue priority (not 0) for heartbeat | Guarantees heartbeat is always first in SortedDictionary; only int.MinValue is reserved |
| 2 | DefaultIntervalSeconds as const, not from IOptions | Avoids DI injection in registry; matches instance property default |

## Deviations from Plan

None -- plan executed as written with int.MinValue correction applied per user instruction.

## Verification

- `dotnet build` -- clean, 0 warnings, 0 errors
- `dotnet test` -- 199/199 passed (5 new + 194 existing)
