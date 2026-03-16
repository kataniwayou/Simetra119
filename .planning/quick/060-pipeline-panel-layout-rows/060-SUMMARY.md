---
phase: quick
plan: 060
result: complete
---

## Summary

Reorganized Pipeline Counters panel group in simetra-operations.json from a mixed layout into 4 semantic rows.

## Changes

Updated gridPos for 8 panels in `deploy/grafana/dashboards/simetra-operations.json`:

| Row | y | Panels (left → right) | Width |
|-----|---|----------------------|-------|
| 1 Events | 7 | Published, Handled, Errors, Rejected | 6×4 (unchanged) |
| 2 Polls | 15 | Poll Recovered, Poll Unreachable, Polls Executed | 8×3 |
| 3 Traps | 23 | Traps Dropped, Trap Auth Failed, Traps Received | 8×3 |
| 4 Routing | 31 | Tenant Vector Routed, Aggregated Computed | 12×2 |

## Verification

- JSON validates with `jq empty` — valid
- All 12 pipeline panels positioned without overlap
- .NET Runtime row (y=39) unaffected
