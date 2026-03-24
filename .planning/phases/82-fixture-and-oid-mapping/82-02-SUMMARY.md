---
phase: 82-fixture-and-oid-mapping
plan: 02
subsystem: testing
tags: [e2e, k8s, configmap, bash, snmp, oid, fixture]

# Dependency graph
requires:
  - phase: 82-fixture-and-oid-mapping/82-01
    provides: OID metric map ConfigMap and simulator OID registrations for subtrees 8-11
provides:
  - K8s ConfigMap fixture with 4 v2.6 tenants (T1_P1, T2_P1, T1_P2, T2_P2)
  - Bash OID mapping source file (72 entries, 24 OIDs x 3 fields) for Phase 83 interpreter
affects: [83-command-interpreter]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "OID_MAP[TENANT.ROLE.N.FIELD] associative array key convention for per-tenant per-role lookups"
    - "Flat sourced Bash mapping file in tests/e2e/lib/ — sourced, never executed directly"

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg12-v26-four-tenant.yaml
    - tests/e2e/lib/oid_map.sh
  modified: []

key-decisions:
  - "Tenant names use v2.6 short form directly (T1_P1, not e2e-v26-g1-t1) — becomes Prometheus tenant_id label"
  - "Each tenant gets its own OID subtree (8-11) — collision-free by construction, no overlap with existing subtrees 1-7"
  - "All 4 tenants reuse existing e2e_set_bypass command — no new command OIDs needed"

patterns-established:
  - "OID_MAP key format: TENANT.ROLE.N.FIELD (e.g. T1_P1.E.1.oid) — adding a metric = adding one line"
  - "Evaluate healthy=10/violated=0, Resolved healthy=1/violated=0"

# Metrics
duration: 2min
completed: 2026-03-24
---

# Phase 82 Plan 02: Fixture & OID Mapping Summary

**4-tenant v2.6 K8s ConfigMap fixture (24 metrics, 2 priority groups) and 72-entry Bash OID mapping file ready for Phase 83 command interpreter**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-24T14:17:54Z
- **Completed:** 2026-03-24T14:19:23Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Created `tenant-cfg12-v26-four-tenant.yaml` with 4 tenants, 24 metrics, correct priority groupings, and all Evaluate metrics with TimeSeriesSize=3/GraceMultiplier=2.0
- Created `oid_map.sh` with 72 OID_MAP entries across subtrees 8-11 (no OID collisions with existing subtrees 1-7)
- All tenant names, metric names, and OID suffixes are internally consistent between both files

## Task Commits

Each task was committed atomically:

1. **Task 1: Create 4-tenant v2.6 fixture ConfigMap** - `7dcc4af` (feat)
2. **Task 2: Create OID mapping file for Phase 83 interpreter** - `b801310` (feat)

**Plan metadata:** (see final commit below)

## Files Created/Modified
- `tests/e2e/fixtures/tenant-cfg12-v26-four-tenant.yaml` - K8s ConfigMap with T1_P1/T2_P1/T1_P2/T2_P2 tenants
- `tests/e2e/lib/oid_map.sh` - Sourced Bash file with OID_MAP[72], TENANT_EVAL_COUNT, TENANT_RES_COUNT, VALID_TENANTS

## Decisions Made
- Tenant names use the v2.6 short form directly (T1_P1, T2_P1, T1_P2, T2_P2) — these become the `tenant_id` label in Prometheus metrics, making them immediately readable in Grafana
- All 4 tenants reuse the pre-existing `e2e_set_bypass` command (OID `.999.4.4.0`) — no new command OID registrations needed
- OID subtrees 8-11 assigned one per tenant (T1_P1=8, T2_P1=9, T1_P2=10, T2_P2=11) — guaranteed collision-free with existing subtrees 1-7

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 83 command interpreter can source `tests/e2e/lib/oid_map.sh` and immediately look up any tenant/role/slot OID suffix and values
- `tenant-cfg12-v26-four-tenant.yaml` ready to apply to cluster alongside the OID map and simulator extensions from Plan 01
- All 4 tenant names are consistent between fixture and mapping file — no renaming needed

---
*Phase: 82-fixture-and-oid-mapping*
*Completed: 2026-03-24*
