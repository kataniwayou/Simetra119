---
phase: quick
plan: 054
subsystem: tenant-vector
tags: [configuration, metric-slot, time-series]
completed: 2026-03-12
duration: ~3m
tech-stack:
  added: []
  patterns: [per-metric-configuration]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Configuration/MetricSlotOptions.cs
    - src/SnmpCollector/Configuration/TenantOptions.cs
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - src/SnmpCollector/config/tenantvector.json
    - .planning/STATE.md
decisions:
  - id: Q054-D1
    description: "TimeSeriesSize default remains 1 on MetricSlotOptions (backward compatible)"
---

# Quick Task 054: Move TimeSeriesSize to MetricSlotOptions Summary

**One-liner:** Per-metric TimeSeriesSize on MetricSlotOptions replaces per-tenant setting on TenantOptions

## What Changed

Moved the `TimeSeriesSize` configuration property from `TenantOptions` (per-tenant) to `MetricSlotOptions` (per-metric). Each metric slot now independently controls its own time series depth. Default remains 1 (single latest value) for backward compatibility.

## Tasks Completed

| Task | Name | Commit | Key Changes |
|------|------|--------|-------------|
| 1 | Move TimeSeriesSize property and update all references | 1d7386e | Added to MetricSlotOptions, removed from TenantOptions, updated registry and config |
| 2 | Update tests and STATE.md | 4e16182 | STATE.md architectural fact updated, quick task table entry added |

## Verification Results

- Build: passed (0 errors, 0 warnings)
- Tests: 210/210 passed
- `tenantOpts.TimeSeriesSize` grep: 0 matches (removed)
- `metric.TimeSeriesSize` grep in TenantVectorRegistry.cs: 1 match (correct)
- tenantvector.json: TimeSeriesSize at metric level (tenant 2, metric npb_cpu_util = 5)

## Deviations from Plan

None - plan executed exactly as written.

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| Q054-D1 | Default TimeSeriesSize = 1 on MetricSlotOptions | Backward compatible; metrics without explicit setting retain single-value behavior |
