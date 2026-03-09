---
phase: quick
plan: "034"
subsystem: dashboard
tags: [grafana, prometheus, operations-dashboard]

requires:
  - phase: 18-operations-dashboard
    provides: "Base operations dashboard with Pod Identity table"
provides:
  - "Host column in Pod Identity table using service_instance_id"
  - "Consistent Host/Pod/Role column naming across dashboards"
affects: []

tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - deploy/grafana/dashboards/simetra-operations.json

key-decisions:
  - "Updated PromQL queries to group by service_instance_id in addition to k8s_pod_name to populate Host column"

patterns-established: []

duration: 3min
completed: 2026-03-09
---

# Quick 034: Ops Dashboard Host Column & Pod Rename Summary

**Added Host column (service_instance_id) to Pod Identity table with column order Host, Pod, Role matching business dashboard pattern**

## Performance

- **Duration:** 3 min
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Added service_instance_id override with displayName "Host" to Pod Identity panel
- Renamed k8s_pod_name displayName from "Pod Name" to "Pod"
- Added organize transformation enforcing column order: Host(0), Pod(1), Role(2)
- Updated both PromQL queries to group by service_instance_id for Host data

## Task Commits

1. **Task 1: Add Host column, rename Pod Name, enforce column order** - `acb8e71` (feat)

## Files Created/Modified
- `deploy/grafana/dashboards/simetra-operations.json` - Updated Pod Identity table panel with Host column, Pod rename, organize transform, and query changes

## Decisions Made
- Updated PromQL queries to include `service_instance_id` in group by clause -- without this, the Host column would have no data since the original queries only grouped by `k8s_pod_name`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added service_instance_id to PromQL group by**
- **Found during:** Task 1
- **Issue:** Queries grouped only by k8s_pod_name; Host column would be empty without service_instance_id in results
- **Fix:** Changed `count by (k8s_pod_name)` to `count by (service_instance_id, k8s_pod_name)` in both queries
- **Files modified:** deploy/grafana/dashboards/simetra-operations.json
- **Verification:** JSON parses correctly, all assertions pass
- **Committed in:** acb8e71

---

**Total deviations:** 1 auto-fixed (1 missing critical)
**Impact on plan:** Essential for Host column to display data. No scope creep.

## Issues Encountered
None

## Next Phase Readiness
- Operations dashboard Pod Identity table now matches business dashboard column pattern
- User needs to re-import the updated JSON into Grafana

---
*Quick: 034-ops-dashboard-host-column-pod-rename*
*Completed: 2026-03-09*
