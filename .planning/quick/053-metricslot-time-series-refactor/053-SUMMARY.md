---
phase: quick
plan: "053"
subsystem: pipeline
tags: [metric-slot, time-series, immutable-array, tenant-vector]
completed: 2026-03-12
duration: ~8 min
dependency-graph:
  requires: [Q052]
  provides: [MetricSlotHolder cyclic time series, CopyFrom carry-over, TimeSeriesSize config]
  affects: [future trend analysis, delta calculations]
tech-stack:
  added: []
  patterns: [ImmutableArray cyclic series with SeriesBox reference wrapper for Volatile semantics]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/MetricSlot.cs
    - src/SnmpCollector/Pipeline/MetricSlotHolder.cs
    - src/SnmpCollector/Configuration/TenantOptions.cs
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
decisions:
  - id: Q053-D1
    title: SeriesBox reference wrapper for Volatile semantics
    choice: "Private SeriesBox class wrapping ImmutableArray<MetricSlot> to enable Volatile.Read/Write (ImmutableArray is a struct, Volatile requires reference types)"
    rationale: "Maintains lock-free volatile swap pattern established in Q052 while supporting struct-based ImmutableArray"
---

# Quick Task 053: MetricSlot Time Series Refactor Summary

Refactored MetricSlotHolder from single-slot volatile swap to ImmutableArray cyclic time series with TypeCode/Source promoted to holder-level properties and TimeSeriesSize config on TenantOptions.

## What Changed

### MetricSlot.cs
- Slimmed from 5-field record to 3-field sample record: `MetricSlot(double Value, string? StringValue, DateTimeOffset Timestamp)`
- Removed `SnmpType TypeCode`, `SnmpSource Source` parameters
- Renamed `UpdatedAt` to `Timestamp`
- Removed `using Lextm.SharpSnmpLib;` (no longer needed)

### MetricSlotHolder.cs
- Replaced `private MetricSlot? _slot` with `private SeriesBox _box` (ImmutableArray wrapped in reference type for Volatile semantics)
- Added `public int TimeSeriesSize { get; }` (constructor parameter, default 1)
- Added `public SnmpType TypeCode { get; private set; }` and `public SnmpSource Source { get; private set; }`
- WriteValue: sets TypeCode/Source on holder, creates MetricSlot sample, appends to cyclic ImmutableArray (RemoveAt(0).Add when at capacity)
- ReadSlot: returns last sample from series (or null if empty)
- Added `ReadSeries()`: returns full ImmutableArray snapshot
- Added `CopyFrom(MetricSlotHolder old)`: bulk-loads series + metadata during registry reload, truncates to new TimeSeriesSize

### TenantOptions.cs
- Added `public int TimeSeriesSize { get; set; } = 1;` (backward compatible default)

### TenantVectorRegistry.cs
- Holder construction passes `tenantOpts.TimeSeriesSize` as 5th argument
- Carry-over logic replaced ReadSlot+WriteValue pattern with `newHolder.CopyFrom(oldHolder)` for both heartbeat and tenant holders

### Tests
- All `slot.TypeCode` assertions changed to `holder.TypeCode`
- All `slot.UpdatedAt` references changed to `slot.Timestamp`
- 6 new tests: ReadSeries empty, time series accumulation, cyclic eviction, promoted properties, CopyFrom, CopyFrom truncation
- Total test count: 210 (all passing)

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| Q053-D1 | SeriesBox reference wrapper for Volatile semantics | ImmutableArray is a struct; Volatile.Read/Write requires reference types. Private SeriesBox class wraps the array to maintain lock-free pattern. |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] ImmutableArray incompatible with Volatile.Read/Write**

- **Found during:** Task 1
- **Issue:** `ImmutableArray<T>` is a value type (struct); `Volatile.Read<T>/Write<T>` requires T to be a reference type (CS0452)
- **Fix:** Introduced private `SeriesBox` class that wraps `ImmutableArray<MetricSlot>` as a reference type, enabling Volatile semantics
- **Files modified:** `src/SnmpCollector/Pipeline/MetricSlotHolder.cs`
- **Commit:** 1ab48c4

## Commits

| Hash | Message |
|------|---------|
| 1ab48c4 | feat(Q053): refactor MetricSlot to slim record, MetricSlotHolder to ImmutableArray cyclic series |
| 78c3c19 | feat(Q053): update carry-over to CopyFrom and adapt all tests for new MetricSlot shape |
