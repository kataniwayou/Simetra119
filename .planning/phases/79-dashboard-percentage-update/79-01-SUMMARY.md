---
phase: 79-dashboard-percentage-update
plan: 01
subsystem: infra
tags: [grafana, prometheus, promql, dashboard]

# Dependency graph
requires:
  - phase: 76-tenant-metric-gauge-instrumentation
    provides: tenant_metric_stale_percent, tenant_metric_resolved_percent, tenant_metric_evaluate_percent, tenant_command_dispatched_percent, tenant_command_failed_percent, tenant_command_suppressed_percent, tenant_evaluation_state gauges
  - phase: 77-snapshot-job-percentage-computation
    provides: EvaluateTenant single-exit recording all 6 percentage gauges
affects:
  - grafana-import
  - e2e-dashboard-verification

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Direct gauge queries in Grafana: max by(...) (gauge_name{...}) — no increase()/rate() wrapping"
    - "Column header naming convention: DisplayName with (%) suffix, decimals=0, no unit property"

key-files:
  created: []
  modified:
    - deploy/grafana/dashboards/simetra-operations.json

key-decisions:
  - "Removed zero-fallback or-on clause from D-I queries — gauges always have value after first recording"
  - "Column headers show (%) in displayName override only; no unit property added (raw number display is intentional)"
  - "Trend column (RefId B) uses delta(tenant_command_dispatched_percent[30s]) — delta on gauge that changes each cycle"

patterns-established:
  - "v2.5 gauge queries: max by (tenant_id, priority, service_instance_id, k8s_pod_name) (gauge{...})"

# Metrics
duration: 5min
completed: 2026-03-23
---

# Phase 79 Plan 01: Dashboard Percentage Update Summary

**Operations dashboard Tenant Status table (panel id=28) updated from increase()-counter queries to direct v2.5 percentage gauge queries, with (%) column headers and tenant_evaluation_state rename**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-23T18:03:13Z
- **Completed:** 2026-03-23T18:08:44Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments

- All 6 counter-based `increase()` queries in panel id=28 replaced with direct gauge reads (`max by(...)`)
- State column updated from `tenant_state` to `tenant_evaluation_state` (completing Phase 76 rename)
- Trend column (RefId B) updated from `delta(tenant_command_dispatched_total)` to `delta(tenant_command_dispatched_percent)`
- All 6 percentage column headers updated with `(%)` suffix (Stale(%), Resolved(%), Evaluate(%), Dispatched(%), Suppressed(%), Failed(%))
- Zero-fallback `or on(...)` clauses removed — gauges do not need them

## Task Commits

Each task was committed atomically:

1. **Task 1: Update PromQL queries in panel id=28 targets** - `7a156f5` (feat)
2. **Task 2: Update column display names to include (%) suffix** - `9fadded` (feat)

**Plan metadata:** (pending docs commit)

## Files Created/Modified

- `deploy/grafana/dashboards/simetra-operations.json` - Panel id=28 targets (RefIds A-I) and overrides displayNames updated

## Decisions Made

- Removed zero-fallback `or on(tenant_id, priority, service_instance_id, k8s_pod_name) (...) * 0` from all gauge queries — gauges always return a value after first recording, unlike counters which may be absent before first increment
- No `unit` property added to percentage columns — raw number display (e.g., "75" not "75%") is intentional per context decisions; `(%)` suffix is in the column header only
- `delta()` wrapper kept for Trend column (RefId B) since it shows change direction, appropriate for a gauge that changes each evaluation cycle

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

Dashboard import via Grafana API (already documented in context):

```bash
kubectl port-forward -n simetra svc/grafana 3001:3000
curl -X POST http://localhost:3001/api/dashboards/db \
  -H "Content-Type: application/json" \
  -u admin:admin \
  -d @payload.json
```

Where `payload.json` wraps the dashboard JSON in `{"dashboard": ..., "overwrite": true, "folderId": 0}`.

## Next Phase Readiness

- Dashboard JSON ready for import to Grafana
- DSH-01 and DSH-02 requirements complete
- Phase 79 complete — v2.5 dashboard reflects gauge-based metric approach end-to-end

---
*Phase: 79-dashboard-percentage-update*
*Completed: 2026-03-23*
