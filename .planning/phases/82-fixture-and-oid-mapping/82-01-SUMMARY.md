---
phase: 82
plan: "01"
subsystem: e2e-simulator
tags: [snmp, oid, simulator, k8s, config, v2.6]
one-liner: "24 new v2.6 OIDs registered in simulator (TENANT_OIDS_V26), OID metric maps, and device poll configs for 4-tenant fixture"

dependency-graph:
  requires: []
  provides:
    - TENANT_OIDS_V26 list with 24 OIDs in e2e_simulator.py
    - 24 OID-to-metric-name mappings in K8s and local OID metric map
    - v2.6 poll group at IntervalSeconds=1 in K8s and local device config
    - Full E2E-SIM device entry added to local devices.json (was missing)
  affects:
    - "82-02: tenant ConfigMap fixture (references metric names defined here)"
    - "83-xx: command interpreter (uses OID suffixes from these registrations)"

tech-stack:
  added: []
  patterns:
    - TENANT_OIDS_V26 list pattern extending TENANT_OIDS
    - Subtree-per-tenant OID allocation (subtrees 8-11)

key-files:
  created: []
  modified:
    - simulators/e2e-sim/e2e_simulator.py
    - deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml
    - deploy/k8s/snmp-collector/simetra-devices.yaml
    - src/SnmpCollector/config/oid_metric_map.json
    - src/SnmpCollector/config/devices.json

decisions:
  - id: subtree-8-to-11
    choice: "Subtrees 8-11 assigned to T1_P1/T2_P1/T1_P2/T2_P2 respectively"
    rationale: "Collision-free by construction; follows existing per-tenant subtree pattern from Phase 61"

metrics:
  duration: "~4 min"
  completed: "2026-03-24"
---

# Phase 82 Plan 01: Fixture & OID Mapping — Register v2.6 OIDs Summary

24 new v2.6 OIDs registered in simulator (TENANT_OIDS_V26), OID metric maps, and device poll configs for 4-tenant fixture.

## What Was Built

### Task 1: Add 24 v2.6 OIDs to E2E Simulator

Added `TENANT_OIDS_V26` list to `e2e_simulator.py` with 24 entries spanning subtrees 8-11:

- **Subtree 8** — T1_P1: `e2e_T1P1_eval1/2`, `e2e_T1P1_res1/2` (4 OIDs)
- **Subtree 9** — T2_P1: `e2e_T2P1_eval1-4`, `e2e_T2P1_res1-4` (8 OIDs)
- **Subtree 10** — T1_P2: `e2e_T1P2_eval1/2`, `e2e_T1P2_res1/2` (4 OIDs)
- **Subtree 11** — T2_P2: `e2e_T2P2_eval1-4`, `e2e_T2P2_res1-4` (8 OIDs)

All 24 OIDs use `v2c.Gauge32` syntax and default to `0` in the `_make_scenario()` baseline dict. The registration loop was updated from `TEST_OIDS + TENANT_OIDS` to `TEST_OIDS + TENANT_OIDS + TENANT_OIDS_V26`. Docstring updated to reflect 48 total OIDs.

### Task 2: Extend OID Metric Map and Device Poll Config (K8s + Local Dev)

**K8s simetra-oid-metric-map.yaml:** 24 new entries added at the bottom of the E2E section, all with `.0` suffix (SNMP scalar convention), grouped by tenant.

**K8s simetra-devices.yaml:** New poll group added to E2E-SIM device with `IntervalSeconds: 1` and `GraceMultiplier: 2.0`, listing all 24 v2.6 metrics.

**Local oid_metric_map.json:** Added all 24 v2.6 entries. Also added the pre-existing E2E entries (subtrees 1, 4, 5-7) which were missing from the local file — this was a gap vs K8s config, fixed as part of this task.

**Local devices.json:** Added a complete E2E-SIM device entry (was entirely missing from local dev config). The entry includes all existing poll groups plus the new v2.6 poll group at `IntervalSeconds: 1`.

## Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| OID subtree assignment | Subtrees 8-11 for T1_P1/T2_P1/T1_P2/T2_P2 | Collision-free; continues per-tenant subtree pattern from Phase 61 |
| Poll interval | 1 second for v2.6 metrics | Matches existing T2-T4 tenant poll group; fast readiness for E2E test assertions |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Local oid_metric_map.json was missing all E2E OID entries**

- **Found during:** Task 2
- **Issue:** The local `src/SnmpCollector/config/oid_metric_map.json` had no E2E entries at all (subtrees 1, 4, 5-7 absent), while the K8s ConfigMap had them. This would have caused local dev to resolve all E2E OIDs as "Unknown".
- **Fix:** Added all pre-existing E2E OID entries (subtrees 1, 4, 5-7) plus the 24 new v2.6 entries to the local file.
- **Files modified:** `src/SnmpCollector/config/oid_metric_map.json`
- **Commit:** 2a4d990

**2. [Rule 2 - Missing Critical] Local devices.json was missing the E2E-SIM device entry entirely**

- **Found during:** Task 2
- **Issue:** `src/SnmpCollector/config/devices.json` only had OBP-01 and NPB-01. E2E-SIM was completely absent, meaning local dev would never poll any E2E simulator OIDs.
- **Fix:** Added a full E2E-SIM device entry matching the K8s structure (port 10163 for local), including all existing poll groups plus the new v2.6 poll group.
- **Files modified:** `src/SnmpCollector/config/devices.json`
- **Commit:** 2a4d990

## Verification Results

| Check | Result |
|-------|--------|
| Simulator Python syntax | Pass |
| TENANT_OIDS_V26 in definition + registration loop | Pass (2 occurrences) |
| 24 baseline dict entries (subtrees 8-11) | Pass |
| 24 TENANT_OIDS_V26 list entries | Pass |
| e2e_T1P1_eval1 in all 4 config files | Pass |
| e2e_T2P2_res4 in all 4 config files | Pass |
| All 24 K8s OID map entries use .0 suffix | Pass (0 missing) |
| All 24 local OID map entries use .0 suffix | Pass (0 missing) |
| v2.6 poll group IntervalSeconds=1 | Pass |
| Existing subtrees 1-7 unaffected | Pass |
| Local devices.json valid JSON | Pass |
| Local oid_metric_map.json valid JSON (after comment strip) | Pass |

## Commits

| Hash | Message |
|------|---------|
| d627644 | feat(82-01): add TENANT_OIDS_V26 with 24 v2.6 OIDs to E2E simulator |
| 2a4d990 | feat(82-01): extend OID metric map and device poll config with 24 v2.6 metrics |

## Next Phase Readiness

Phase 82 Plan 02 (tenant ConfigMap fixture) can proceed. All metric names used in that fixture are now registered and resolvable:
- `e2e_T1P1_eval1/2`, `e2e_T1P1_res1/2`
- `e2e_T2P1_eval1-4`, `e2e_T2P1_res1-4`
- `e2e_T1P2_eval1/2`, `e2e_T1P2_res1/2`
- `e2e_T2P2_eval1-4`, `e2e_T2P2_res1-4`

No blockers. The `e2e_set_bypass` command reuses the existing `.999.4.4` OID — no new command OID needed.
