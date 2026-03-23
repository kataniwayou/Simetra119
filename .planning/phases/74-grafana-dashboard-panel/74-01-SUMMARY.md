---
phase: 74-grafana-dashboard-panel
plan: 01
subsystem: infra
tags: [grafana, prometheus, promql, dashboard, tenant-metrics]

# Dependency graph
requires:
  - phase: 72-tenant-metric-service
    provides: TenantMetricService emitting tenant_state, tenant_command_*, tenant_tier*, tenant_evaluation_duration_milliseconds metrics
  - phase: 73-snapshotjob-instrumentation
    provides: SnapshotJob instrumented with EvaluateTenant per-tenant metrics
provides:
  - Simetra Operations dashboard with Tenant Status table panel (row id=27, table id=28)
  - 13-column per-tenant per-pod status table with state color coding, trend arrows, P99 histogram
affects: [grafana, operations-dashboard, monitoring]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "merge + organize transformation pipeline for multi-query Grafana tables"
    - "color-background cell type with value mappings for enum gauge fields"
    - "threshold-based trend arrows with delta() PromQL query (matching business dashboard pattern)"

key-files:
  created: []
  modified:
    - deploy/grafana/dashboards/simetra-operations.json

key-decisions:
  - "Insert between Commands and .NET Runtime sections (shift .NET Runtime down 13 units) rather than appending at end — satisfies TDSH-01 requirement for placement after commands panels"
  - "13 overrides covering all 9 Value columns plus 4 label columns plus 6 hidden system labels"
  - "organize indexByName maps: State=4, Dispatched=5, Failed=6, Suppressed=7, Stale=8, Resolved=9, Evaluate=10, P99=11, Trend=12"

patterns-established:
  - "Tenant table column order: Host, Pod, Tenant, Priority, State, Dispatched, Failed, Suppressed, Stale, Resolved, Evaluate, P99 (ms), Trend"

# Metrics
duration: ~15min
completed: 2026-03-23
---

# Phase 74 Plan 01: Grafana Dashboard Panel Summary

**Per-tenant per-pod status table added to operations dashboard: 13 columns driven by 9 PromQL queries (tenant_state gauge, 6 counter rates, P99 histogram, delta trend), with merge+organize transforms, state color mappings, and trend arrows**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-23T13:00:00Z
- **Completed:** 2026-03-23T13:16:28Z
- **Tasks:** 1/2 (Task 2 at checkpoint awaiting human verification)
- **Files modified:** 1

## Accomplishments
- Added "Tenant Status" row panel (id=27) at y=47 between Command panels and .NET Runtime section
- Added table panel (id=28) at y=48, h=10 with 9 queries (refIds A-I) and 20 field overrides
- Shifted all 7 .NET Runtime panels (ids 15-21) down by 13 grid units to make room
- State column color-codes: NotReady (grey), Healthy (green), Resolved (yellow), Unresolved (red)
- Trend column shows ▼/—/▲ based on delta(tenant_command_dispatched_total[30s])

## Task Commits

1. **Task 1: Add Tenant Status row and table panel** - `74019f8` (feat)

## Files Created/Modified
- `deploy/grafana/dashboards/simetra-operations.json` - Added 2 panels, shifted 7 existing panels

## Decisions Made
- Inserted between Commands and .NET Runtime (not appended after .NET Runtime) to match TDSH-01 operator intent — "Tenant Status" logically belongs adjacent to the command panels
- Used 13 units shift (row h=1 + table h=10 + 2 units buffer) to ensure no y overlap
- Trend column uses Value #B (delta dispatched) as the activity signal — matches business dashboard pattern

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required. Dashboard JSON is deployed via existing Grafana provisioning.

## Next Phase Readiness

- Task 2 (human-verify checkpoint) awaiting operator confirmation in Grafana
- On approval: Phase 74 complete, ready for Phase 75

---
*Phase: 74-grafana-dashboard-panel*
*Completed: 2026-03-23 (pending Task 2 verification)*
