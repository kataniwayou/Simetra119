---
phase: 82-fixture-and-oid-mapping
verified: 2026-03-24T15:00:00Z
status: human_needed
score: 3/4 must-haves verified (1 requires live cluster)
human_verification:
  - test: "Apply tenant-cfg12-v26-four-tenant.yaml to cluster alongside OID map and devices ConfigMaps, wait ~30s, query simetra_tenant_state in Grafana"
    expected: "All 4 tenants show Healthy state after grace window passes with no violations set"
    why_human: "Requires live K8s cluster; simulator baseline=0 is below Evaluate Min=10 -- runtime behavior must be confirmed"
---
# Phase 82: Fixture and OID Mapping -- Verification Report

**Phase Goal:** A 4-tenant environment with collision-free OIDs is live in the cluster, all tenants start Healthy, and a hardcoded mapping file defines every OID suffix and value needed to violate or restore any tenant role.
**Verified:** 2026-03-24T15:00:00Z
**Status:** human_needed -- automated structural checks pass; one truth (Healthy state after grace window) requires live cluster observation
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | Applying the fixture ConfigMap produces 4 tenants (T1_P1, T2_P1, T1_P2, T2_P2) with distinct OID subtrees and no collision | VERIFIED | Fixture contains all 4 tenants; subtrees 8/9/10/11 non-overlapping with existing 1/4/5/6/7 in OID map |
| 2 | After the grace window passes with no violations set, all 4 tenants show Healthy state in Grafana | NEEDS HUMAN | Infrastructure correct but baseline=0 < Evaluate Min=10; live cluster required to confirm |
| 3 | The OID metric map contains entries for every OID suffix used by all 4 tenants | VERIFIED | 24 entries in K8s and local map, all with .0 suffix, metric names consistent across all 6 files |
| 4 | The hardcoded mapping file lists per-tenant per-role OID suffixes with healthy and violated values, adding a new tenant or metric requires one line | VERIFIED | oid_map.sh: 72 OID_MAP entries (24 OIDs x 3 fields), one line per OID, header documents extension procedure |

**Score:** 3/4 truths verified automatically; 1 requires human cluster observation

### Note on Truth 2 (Healthy initial state)

The fixture configures Evaluate metrics with Threshold Min=10.0. The simulator baseline for all v2.6 OIDs is 0. A value of 0 is below the Min threshold, so unset OIDs are in a violated reading state. Healthy state after the grace window requires either the grace window absorbs the violated readings, or the test workflow pre-sets OIDs to healthy values before the grace window expires.

TimeSeriesSize=3 and GraceMultiplier=2.0 at IntervalSeconds=1 gives approximately 6 seconds of grace. Whether the collector restarts and begins polling within this window before committing state is a runtime question requiring cluster observation.

### Required Artifacts

| Artifact | Status | Details |
|----------|--------|---------|
| simulators/e2e-sim/e2e_simulator.py | VERIFIED | TENANT_OIDS_V26 list (lines 229-258, 24 entries); baseline dict (lines 117-144, 24 entries at value 0); registration loop line 353 includes TENANT_OIDS_V26 |
| deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml | VERIFIED | 24 entries for subtrees 999.8-999.11, all with .0 suffix |
| deploy/k8s/snmp-collector/simetra-devices.yaml | VERIFIED | v2.6 poll group lines 240-268: IntervalSeconds=1, GraceMultiplier=2.0, all 24 metrics listed |
| src/SnmpCollector/config/oid_metric_map.json | VERIFIED | 24 entries for subtrees 8-11 with .0 suffix; matches K8s map |
| src/SnmpCollector/config/devices.json | VERIFIED | E2E-SIM device entry with v2.6 poll group at IntervalSeconds=1 and all 24 metrics |
| tests/e2e/fixtures/tenant-cfg12-v26-four-tenant.yaml | VERIFIED | Valid YAML; 4 tenants with correct priorities (T1_P1/T2_P1=P1, T1_P2/T2_P2=P2); 24 total metrics (4+8+4+8); Evaluate metrics have TimeSeriesSize=3/GraceMultiplier=2.0 |
| tests/e2e/lib/oid_map.sh | VERIFIED | 24 assignment lines (72 field assignments); OID_MAP, TENANT_EVAL_COUNT, TENANT_RES_COUNT, VALID_TENANTS all populated |

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| e2e_simulator.py TENANT_OIDS_V26 | SNMP registration loop | Line 353: TEST_OIDS + TENANT_OIDS + TENANT_OIDS_V26 | WIRED |
| e2e_simulator.py TENANT_OIDS_V26 | _make_scenario() baseline | 24 keys subtrees 8-11 set to 0 at lines 117-144 | WIRED |
| simetra-oid-metric-map.yaml | e2e_simulator.py | OID strings 1.3.6.1.4.1.47477.999.8.1.0 through 999.11.8.0 match simulator E2E_PREFIX subtrees with .0 suffix | WIRED |
| tenant-cfg12-v26-four-tenant.yaml | simetra-oid-metric-map.yaml | All 24 MetricName fields in fixture confirmed present in OID map | WIRED |
| oid_map.sh | tenant-cfg12-v26-four-tenant.yaml | OID suffix ordering aligns with metric ordering; subtrees 8/9/10/11 map to T1_P1/T2_P1/T1_P2/T2_P2 respectively | WIRED |

### Requirements Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| FIX-01: 4-tenant ConfigMap fixture with correct compositions | SATISFIED | T1_P1 (P1, 2E/2R/1C), T2_P1 (P1, 4E/4R/1C), T1_P2 (P2, 2E/2R/1C), T2_P2 (P2, 4E/4R/1C) |
| FIX-02: Each tenant uses unique OID suffixes, no collision | SATISFIED | Subtrees 8, 9, 10, 11 -- no overlap with existing 1/4/5/6/7 |
| FIX-03: OID metric map extended with entries for all 4 tenants | SATISFIED | 24 entries in K8s and local map |
| FIX-04: All tenants start Healthy after fixture applied and grace window passes | NEEDS HUMAN | Infrastructure supports it; runtime behavior requires cluster verification |
| MAP-01: Hardcoded mapping file with per-tenant per-role OID suffixes and values | SATISFIED | oid_map.sh: 72 OID_MAP entries, Evaluate healthy=10/violated=0, Resolved healthy=1/violated=0 |
| MAP-02: Mapping is easy to maintain -- add tenant or metric by adding a line | SATISFIED | One line per OID in oid_map.sh; header documents exact extension procedure |

### Anti-Patterns Found

None. No TODO/FIXME/placeholder patterns in any of the 7 modified or created files.

### Human Verification Required

#### 1. All 4 Tenants Reach Healthy State After Grace Window

**Test:** Apply the OID map ConfigMap, devices ConfigMap, and tenant fixture to the cluster, then wait approximately 30 seconds for the collector to restart and grace windows to expire:

    kubectl apply -f deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml
    kubectl apply -f deploy/k8s/snmp-collector/simetra-devices.yaml
    kubectl apply -f tests/e2e/fixtures/tenant-cfg12-v26-four-tenant.yaml

Query in Grafana or Prometheus:

    simetra_tenant_state{tenant_id=~"T1_P1|T2_P1|T1_P2|T2_P2"}

**Expected:** All 4 tenants return state value 1 (Healthy).
**Why human:** Requires a live cluster. The simulator baseline for v2.6 OIDs is 0, which is below the Evaluate Min=10 threshold. Whether tenants reach Healthy depends on whether the grace window absorbs the initial violated readings before state is committed.

**If tenants do NOT reach Healthy:** The simulator HTTP API must be called to set eval OIDs >= 10 and res OIDs >= 1 before the grace window expires. This is a workflow orchestration concern for Phase 83 (the command interpreter), not a structural defect in Phase 82 artifacts.

### Gaps Summary

No structural gaps. All 7 artifacts exist, are substantive, and are correctly wired. The single unverified truth (FIX-04) is a runtime/cluster concern requiring human observation.

---

_Verified: 2026-03-24T15:00:00Z_
_Verifier: Claude (gsd-verifier)_
