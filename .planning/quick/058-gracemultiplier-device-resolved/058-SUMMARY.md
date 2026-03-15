---
quick: "058"
title: "GraceMultiplier & Device-Resolved IntervalSeconds"
subsystem: "tenant-validation"
tags: ["grace-multiplier", "stale-detection", "poll-groups", "interval-resolution", "tenant-vector"]

dependency-graph:
  requires: ["42-02 (ThresholdOptions wired end-to-end)"]
  provides: ["GraceMultiplier on PollOptions/MetricPollInfo/MetricSlotHolder", "IntervalSeconds+GraceMultiplier resolved from device at tenant load time"]
  affects: ["future stale metric detection (will use MetricSlotHolder.GraceMultiplier)"]

tech-stack:
  added: []
  patterns: ["device-resolved config (IntervalSeconds, GraceMultiplier derived from device poll group, not operator-set)"]

key-files:
  created: []
  modified:
    - src/SnmpCollector/Configuration/PollOptions.cs
    - src/SnmpCollector/Pipeline/MetricPollInfo.cs
    - src/SnmpCollector/Pipeline/MetricSlotHolder.cs
    - src/SnmpCollector/Configuration/MetricSlotOptions.cs
    - src/SnmpCollector/Services/DeviceWatcherService.cs
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - src/SnmpCollector/Services/TenantVectorWatcherService.cs
    - src/SnmpCollector/config/devices.json
    - deploy/k8s/snmp-collector/simetra-devices.yaml
    - deploy/k8s/production/configmap.yaml
    - tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs

decisions:
  - "MetricSlotOptions.GraceMultiplier added as a resolved (not operator-set) field — plan initially said not to, but correction in plan section 2a required it to carry value through to TenantVectorRegistry.Reload"
  - "MetricSlotHolder constructor reordered: graceMultiplier (double, default 2.0) placed before threshold (ThresholdOptions?) to keep non-nullable before nullable params"
  - "IP-resolution foreach variable renamed from 'device' to 'registeredDevice' to avoid CS0136 scope conflict with out var device from TEN-07 check"

metrics:
  duration: "5 min"
  completed: "2026-03-15"
  tests-before: 332
  tests-after: 336
---

# Quick 058: GraceMultiplier & Device-Resolved IntervalSeconds Summary

**One-liner:** GraceMultiplier (double, default 2.0) added to device poll groups and resolved end-to-end into MetricSlotHolder at tenant load time via OID + aggregated-metric lookup.

## What Was Done

### Task 1 — Add GraceMultiplier to PollOptions, MetricPollInfo, MetricSlotHolder

- `PollOptions.GraceMultiplier` (double, default 2.0) — parsed from device config JSON
- `MetricPollInfo.GraceMultiplier` (double, default 2.0) — positional record parameter after TimeoutMultiplier
- `MetricSlotHolder.GraceMultiplier` (public get-only property) — constructor reordered to `(ip, port, metricName, intervalSeconds, timeSeriesSize=1, graceMultiplier=2.0, threshold=null)`
- `MetricSlotOptions.GraceMultiplier` (double, default 2.0) — resolved field, not operator-set
- `DeviceWatcherService.BuildPollGroups` passes `GraceMultiplier: poll.GraceMultiplier` to MetricPollInfo constructor
- `TenantVectorRegistry.Reload` passes `metric.GraceMultiplier` positionally to MetricSlotHolder constructor
- All 3 device config files updated: `"GraceMultiplier": 2.0` added to every poll group (9 total: 4 OBP + 4 NPB + 1 E2E-SIM)

### Task 2 — Resolve IntervalSeconds + GraceMultiplier in ValidateAndBuildTenants

- TEN-07 check changed from `out _` to `out var device` to capture DeviceInfo
- After threshold validation, resolve loop: looks up OID via `oidMapService.ResolveToOid(metric.MetricName)`, then searches poll group OIDs
- Fallback loop: if no OID match (e.g. aggregated metric), searches `pg.AggregatedMetrics` by MetricName (OrdinalIgnoreCase)
- Both `metric.IntervalSeconds` and `metric.GraceMultiplier` set from resolved values
- IP-resolution foreach variable renamed `registeredDevice` to avoid CS0136 scope conflict
- 4 new tests added (336 total, all pass)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Variable scope conflict in TenantVectorWatcherService**

- **Found during:** Task 2 build
- **Issue:** The existing IP-resolution `foreach (var device in ...)` conflicted with the new `out var device` from TEN-07 — CS0136 scope error
- **Fix:** Renamed foreach variable to `registeredDevice`
- **Files modified:** `TenantVectorWatcherService.cs`
- **Commit:** b2059b6

## Verification Checklist

- [x] `PollOptions.GraceMultiplier` exists with default 2.0
- [x] `MetricPollInfo` record has `GraceMultiplier` parameter with default 2.0
- [x] `MetricSlotHolder.GraceMultiplier` is a public get-only property, set in constructor
- [x] `MetricSlotHolder.CopyFrom` does NOT copy GraceMultiplier
- [x] `MetricSlotOptions.GraceMultiplier` exists (resolved field, default 2.0)
- [x] `ValidateAndBuildTenants` resolves IntervalSeconds + GraceMultiplier from device poll group via OID lookup
- [x] Aggregated metrics are also resolved (fallback to AggregatedMetrics search)
- [x] `TenantVectorRegistry.Reload` passes GraceMultiplier to MetricSlotHolder constructor
- [x] All 3 device config files have `"GraceMultiplier": 2.0` on every poll group
- [x] No IntervalSeconds in tenant config files (confirmed none present)
- [x] 4 new unit tests pass
- [x] All existing tests pass (332 -> 336)
- [x] `dotnet build` clean

## Commits

- `2eac645` feat(058): add GraceMultiplier to PollOptions, MetricPollInfo, MetricSlotHolder
- `b2059b6` feat(058): resolve IntervalSeconds+GraceMultiplier from device poll group in ValidateAndBuildTenants
