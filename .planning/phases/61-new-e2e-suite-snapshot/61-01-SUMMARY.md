---
phase: 61-new-e2e-suite-snapshot
plan: 01
subsystem: testing
tags: [e2e, snmp-simulator, aiohttp, bash, k8s-configmap, tenant-fixture, oid-map]

# Dependency graph
requires:
  - phase: 60-pre-tier-readiness
    provides: AreAllReady/ReadinessGrace — snapshot state suite depends on readiness gate semantics
  - phase: 59-advance-gate-fix-and-simulation
    provides: advance gate blocks on Unresolved — multi-group test correctness depends on this
provides:
  - Simulator extended to 24 OIDs with per-OID HTTP override endpoints
  - 4-tenant 2-group fixture for snapshot state scenarios (G1-T1/T2, G2-T3/T4)
  - OID metric map entries for 9 new T2-T4 OIDs (.999.5/6/7 subtrees)
  - SnapshotJob.IntervalSeconds=1 in K8s config for fast E2E cycling
  - sim_set_oid/sim_set_oid_stale/reset_oid_overrides bash helpers in sim.sh
  - Snapshot State Suite report category (indices 40-51, 12 scenarios)
affects: [61-02, 61-03]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Per-OID HTTP override dict checked before scenario fallback in DynamicInstance.getValue
    - aiohttp route registration order: literal path segment (stale) before wildcard ({value})
    - sim.sh helpers follow curl + http_code pattern consistent with sim_set_scenario
    - reset_scenario always clears OID overrides first (prevents leakage between scenarios)

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg05-four-tenant-snapshot.yaml
  modified:
    - simulators/e2e-sim/e2e_simulator.py
    - deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml
    - deploy/k8s/snmp-collector/snmp-collector-config.yaml
    - tests/e2e/lib/sim.sh
    - tests/e2e/lib/report.sh

key-decisions:
  - "Per-OID override dict (_oid_overrides) checked before scenario in DynamicInstance.getValue — enables test to set individual OID values without predefined scenarios"
  - "/oid/{oid}/stale route registered before /oid/{oid}/{value} — aiohttp matches in registration order; literal 'stale' must win over wildcard {value}"
  - "All 4 tenants use Min:10 for evaluate threshold (not Max:80) — value >= 10 = healthy, value < 10 = violated; symmetric with resolved Min:1"
  - "reset_scenario calls reset_oid_overrides before sim_set_scenario default — prevents per-OID state from previous scenarios leaking"
  - "T1 reuses .999.4.x OIDs (e2e_port_utilization, e2e_channel_state, e2e_bypass_status) — no new OID map entries needed for T1"
  - "Snapshot State Suite report category end adjusted from 40 to 51; Snapshot Evaluation end adjusted from 40 to 39 for clean boundaries"

patterns-established:
  - "TENANT_OIDS list separate from TEST_OIDS — allows independent extension of per-tenant vs test-purpose OID sets"
  - "Baseline scenario includes defaults for all registered OIDs — new OIDs get 0 unless overridden"

# Metrics
duration: 4min
completed: 2026-03-19
---

# Phase 61 Plan 01: E2E Snapshot Suite Infrastructure Summary

**Simulator extended to 24 OIDs with per-OID HTTP control (POST /oid/{oid}/{value}, POST /oid/{oid}/stale, DELETE /oid/overrides); 4-tenant 2-group K8s fixture + 9 OID map entries + 1s SnapshotJob interval + sim.sh helpers ready for plans 02 and 03.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-19T20:07:08Z
- **Completed:** 2026-03-19T20:10:35Z
- **Tasks:** 2
- **Files modified:** 6 (1 created)

## Accomplishments

- Simulator now serves 24 OIDs: 9 new tenant OIDs (.999.5/6/7 subtrees) registered as DynamicInstance via TENANT_OIDS list; all 9 included in scenario baseline at value 0
- Per-OID override layer: `_oid_overrides` dict and `_stale_oids` set checked in `DynamicInstance.getValue` before scenario fallback; three HTTP endpoints control overrides at runtime
- Full fixture stack for Phase 61 scenarios: OID map (9 entries), K8s config (1s interval), 4-tenant ConfigMap, bash helpers, and report category

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend simulator with 9 new OIDs and per-OID HTTP endpoints** - `28399b7` (feat)
2. **Task 2: OID map, SnapshotJob config, 4-tenant fixture, sim.sh helpers, report.sh category** - `ef4ed55` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `simulators/e2e-sim/e2e_simulator.py` - TENANT_OIDS list, _oid_overrides/_stale_oids state, DynamicInstance.getValue override checks, post_oid_value/post_oid_stale/delete_oid_overrides handlers, updated registration loop and docstring
- `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` - 9 new entries for .999.5.1-3, .999.6.1-3, .999.7.1-3 mapped to e2e_eval_T2..e2e_res2_T4
- `deploy/k8s/snmp-collector/snmp-collector-config.yaml` - SnapshotJob.IntervalSeconds=1 added
- `tests/e2e/fixtures/tenant-cfg05-four-tenant-snapshot.yaml` - New file: G1-T1 (Priority=1, .999.4.x OIDs), G1-T2 (Priority=1, .999.5.x OIDs), G2-T3 (Priority=2, .999.6.x OIDs), G2-T4 (Priority=2, .999.7.x OIDs); all Min:10 evaluate, Min:1 resolved, e2e_command_response command
- `tests/e2e/lib/sim.sh` - sim_set_oid, sim_set_oid_stale, reset_oid_overrides helpers added; reset_scenario now calls reset_oid_overrides first
- `tests/e2e/lib/report.sh` - Snapshot State Suite|40|51 added; Snapshot Evaluation end corrected from 40 to 39

## Decisions Made

- Per-OID override dict checked before scenario in DynamicInstance.getValue — enables arbitrary test control without predefined scenario combinations
- `/oid/{oid}/stale` registered before `/oid/{oid}/{value}` — aiohttp route registration order determines which handler wins for overlapping patterns
- All 4 tenants use Min:10 for evaluate threshold (not Max:80 from other fixtures) — symmetry with resolved Min:1; value=0 always violated, value>=10 always healthy
- reset_scenario calls reset_oid_overrides before sim_set_scenario default — prevents OID state leakage across scenarios
- T1 reuses existing .999.4.x OIDs, no new OID map entries needed for T1

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All 6 infrastructure files in place; plans 02 and 03 can reference tenant-cfg05-four-tenant-snapshot.yaml and use sim_set_oid for per-tenant OID control
- SnapshotJob 1s interval means test cycle assertions can use short timeouts (2-5s polling)
- T1 evaluate threshold is Min:10 (not Max:80): value < 10 = violated/Unresolved, value >= 10 = healthy

---
*Phase: 61-new-e2e-suite-snapshot*
*Completed: 2026-03-19*
