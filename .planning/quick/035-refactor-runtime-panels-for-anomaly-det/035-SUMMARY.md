# Quick 035: Refactor Runtime Panels for Anomaly Detection

**One-liner:** Replaced GC Pause Time and GC Heap Size panels with CPU Time (percentunit) and Exceptions (ops) for faster anomaly signals.

## What Changed

Replaced two .NET Runtime panels in the operations dashboard that showed GC internals with more actionable anomaly detection metrics:

| Before | After | Metric | Unit |
|--------|-------|--------|------|
| GC Pause Time Rate | CPU Time | `dotnet_process_cpu_time_seconds_total` | percentunit |
| GC Heap Size | Exceptions | `dotnet_exceptions_total` | ops |

Both new panels retain the same `sum by (k8s_pod_name)` pattern with `$pod` filter, matching all other runtime panels.

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| CPU Time over GC Pause Time | CPU spikes are a faster, more universal anomaly signal than GC pause duration |
| Exceptions over GC Heap Size | Exception rate spikes are the fastest indicator that something broke |

## Deviations from Plan

None - plan executed exactly as written.

## Commits

| Hash | Description |
|------|-------------|
| 31a9921 | feat(quick-035): replace GC panels with CPU Time and Exceptions for anomaly detection |

## Files Modified

- `deploy/grafana/dashboards/simetra-operations.json` - Updated 2 panels in .NET Runtime row

## Duration

~2 minutes
