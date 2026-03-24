# Phase 82: Fixture & OID Mapping - Research

**Researched:** 2026-03-24
**Domain:** K8s ConfigMap fixture authoring, E2E simulator OID allocation, tenant OID mapping file
**Confidence:** HIGH

## Summary

This phase delivers three artifacts: (1) a new 4-tenant ConfigMap fixture applying tenants T1_P1, T2_P1, T1_P2, T2_P2 with collision-free OID suffixes, (2) extensions to the simetra-oid-metric-map ConfigMap so all new OIDs resolve to metric names, and (3) a hardcoded Bash mapping file that lists per-tenant per-role OID suffixes with healthy and violated values for use by the command interpreter in Phase 83.

The E2E simulator already handles 24 OIDs across subtrees 1-7 of `1.3.6.1.4.1.47477.999`. Subtrees 1, 2, 4 are test/type fixtures; subtrees 5, 6, 7 carry the existing T2/T3/T4 PSS tenant data (3 OIDs each). The new 4-tenant fixture needs four completely independent OID subtrees — none of which overlap with the existing 7. The natural choice is subtrees 8, 9, 10, 11 (one per tenant), each carrying the maximum required metric count.

The mapping file must be a sourced Bash file (fits the existing `lib/` pattern) that the Phase 83 interpreter sources. It must be flat and key-value structured so adding a tenant or metric means adding one line per new OID entry.

**Primary recommendation:** Assign subtrees 8–11 to T1_P1/T2_P1/T1_P2/T2_P2, allocate OID suffixes within each subtree based on role slot index, add all new OIDs to the K8s oid-metric-map ConfigMap, add new OID registrations to the simulator, and write a Bash mapping file at `tests/e2e/lib/oid_map.sh` using associative arrays.

## Standard Stack

No external libraries needed. All artifacts are:

### Core
| Artifact | Format | Purpose | Why |
|----------|--------|---------|-----|
| Tenant ConfigMap fixture | YAML (K8s ConfigMap) | Applies 4 tenants to cluster | Same pattern as existing tenant-cfg0x files |
| OID metric map extension | YAML (K8s ConfigMap) | Maps new OID strings to metric names | Same format as simetra-oid-metric-map.yaml |
| Simulator OID additions | Python (e2e_simulator.py) | Registers new OIDs in SNMP engine | Same TENANT_OIDS list pattern |
| Mapping file | Bash (sourced, oid_map.sh) | Per-tenant per-role OID lookup | Sourced by Phase 83 interpreter; consistent with lib/ pattern |

### No Installation Required

All files extend existing patterns. No new dependencies.

## Architecture Patterns

### Current OID Subtree Allocation

The E2E simulator uses `E2E_PREFIX = "1.3.6.1.4.1.47477.999"` and allocates subtrees:

```
.999.1.x  -- 7 mapped OIDs (type tests: gauge, integer, counter32, counter64, timeticks, octetstring, ipaddress)
.999.2.x  -- 2 unmapped OIDs (negative proof tests)
.999.3.x  -- Trap OID (not polled)
.999.4.x  -- 6 test-purpose OIDs (e2e_port_utilization, e2e_channel_state, e2e_bypass_status,
             e2e_command_response, e2e_agg_source_a, e2e_agg_source_b)
.999.5.x  -- 3 OIDs for PSS tenant T2 (e2e_eval_T2, e2e_res1_T2, e2e_res2_T2)
.999.6.x  -- 3 OIDs for PSS tenant T3 (e2e_eval_T3, e2e_res1_T3, e2e_res2_T3)
.999.7.x  -- 3 OIDs for PSS tenant T4 (e2e_eval_T4, e2e_res1_T4, e2e_res2_T4)
```

Subtrees 8+ are unused. Assign one subtree per new v2.6 tenant.

### OID Allocation for New 4-Tenant Fixture

The v2.6 tenants have variable metric counts:
- T1_P1: P1, 2E/2R/1C — 4 polled OIDs + 1 command OID
- T2_P1: P1, 4E/4R/1C — 8 polled OIDs + 1 command OID
- T1_P2: P2, 2E/2R/1C — 4 polled OIDs + 1 command OID
- T2_P2: P2, 4E/4R/1C — 8 polled OIDs + 1 command OID

Proposed subtree-to-tenant mapping (collision-free by construction):

```
.999.8.x   -- T1_P1 (P1): 2E + 2R = 4 polled OIDs
               .999.8.1  e2e_T1P1_eval1    Evaluate #1
               .999.8.2  e2e_T1P1_eval2    Evaluate #2
               .999.8.3  e2e_T1P1_res1     Resolved #1
               .999.8.4  e2e_T1P1_res2     Resolved #2

.999.9.x   -- T2_P1 (P1): 4E + 4R = 8 polled OIDs
               .999.9.1  e2e_T2P1_eval1    Evaluate #1
               .999.9.2  e2e_T2P1_eval2    Evaluate #2
               .999.9.3  e2e_T2P1_eval3    Evaluate #3
               .999.9.4  e2e_T2P1_eval4    Evaluate #4
               .999.9.5  e2e_T2P1_res1     Resolved #1
               .999.9.6  e2e_T2P1_res2     Resolved #2
               .999.9.7  e2e_T2P1_res3     Resolved #3
               .999.9.8  e2e_T2P1_res4     Resolved #4

.999.10.x  -- T1_P2 (P2): 2E + 2R = 4 polled OIDs
               .999.10.1 e2e_T1P2_eval1    Evaluate #1
               .999.10.2 e2e_T1P2_eval2    Evaluate #2
               .999.10.3 e2e_T1P2_res1     Resolved #1
               .999.10.4 e2e_T1P2_res2     Resolved #2

.999.11.x  -- T2_P2 (P2): 4E + 4R = 8 polled OIDs
               .999.11.1 e2e_T2P2_eval1    Evaluate #1
               .999.11.2 e2e_T2P2_eval2    Evaluate #2
               .999.11.3 e2e_T2P2_eval3    Evaluate #3
               .999.11.4 e2e_T2P2_eval4    Evaluate #4
               .999.11.5 e2e_T2P2_res1     Resolved #1
               .999.11.6 e2e_T2P2_res2     Resolved #2
               .999.11.7 e2e_T2P2_res3     Resolved #3
               .999.11.8 e2e_T2P2_res4     Resolved #4
```

Commands: All 4 tenants reuse the existing `e2e_set_bypass` command (OID `.999.4.4.0`, already mapped). No new command OIDs needed — command reuse matches existing PSS fixtures.

### Recommended File Locations

```
tests/e2e/fixtures/
└── tenant-cfg12-v26-four-tenant.yaml   # New 4-tenant v2.6 fixture

tests/e2e/lib/
└── oid_map.sh                          # New hardcoded mapping file

simulators/e2e-sim/
└── e2e_simulator.py                    # Add TENANT_OIDS_V26 block

deploy/k8s/snmp-collector/
└── simetra-oid-metric-map.yaml         # Extend with 24 new OID entries
```

### Pattern 1: ConfigMap Fixture File

All existing fixtures follow a consistent YAML template. Use the same.

**Critical fields for Evaluate metrics:**
- `TimeSeriesSize: 3` (enables readiness window)
- `GraceMultiplier: 2.0`
- `Threshold: { "Min": 10.0 }` (threshold crossed when value = 0)

**Critical fields for Resolved metrics:**
- No `TimeSeriesSize` (uses device poll group default)
- `Threshold: { "Min": 1.0 }` (threshold crossed when value = 0)

**Tenant Name format:** Use the v2.6 convention directly: `T1_P1`, `T2_P1`, `T1_P2`, `T2_P2`. These become the `tenant_id` label in Prometheus queries.

**Priority groups:** T1_P1 and T2_P1 share Priority=1; T1_P2 and T2_P2 share Priority=2. This creates a 2-group advance gate structure.

```yaml
# Source: tests/e2e/fixtures/tenant-cfg08-pss-four-tenant.yaml pattern
{
  "Name": "T1_P1",
  "Priority": 1,
  "SuppressionWindowSeconds": 10,
  "Metrics": [
    {
      "Ip": "e2e-simulator.simetra.svc.cluster.local",
      "Port": 161,
      "MetricName": "e2e_T1P1_eval1",
      "TimeSeriesSize": 3,
      "GraceMultiplier": 2.0,
      "Role": "Evaluate",
      "Threshold": { "Min": 10.0 }
    },
    ...
  ],
  "Commands": [
    {
      "Ip": "e2e-simulator.simetra.svc.cluster.local",
      "Port": 161,
      "CommandName": "e2e_set_bypass",
      "Value": "0",
      "ValueType": "Integer32"
    }
  ]
}
```

### Pattern 2: Simulator OID Registration

The simulator uses a `TENANT_OIDS` list with tuples `(oid_str, label, syntax_cls, writable)`. Add a `TENANT_OIDS_V26` block and include it in the registration loop.

```python
# Source: simulators/e2e-sim/e2e_simulator.py TENANT_OIDS pattern
TENANT_OIDS_V26 = [
    (f"{E2E_PREFIX}.8.1",  "e2e_T1P1_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.2",  "e2e_T1P1_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.3",  "e2e_T1P1_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.4",  "e2e_T1P1_res2",  v2c.Gauge32, False),
    # ... T2_P1, T1_P2, T2_P2 entries
]
```

Also add all new OID keys to `_make_scenario()`'s `baseline` dict with default value `0`. This ensures the default scenario returns a valid (non-stale) value for all new OIDs immediately.

**Update the docstring OID count** from 24 to 48 (24 new OIDs added).

### Pattern 3: OID Metric Map Extension

Add 24 new entries to `simetra-oid-metric-map.yaml`. Follow the existing E2E section at the bottom of the ConfigMap.

```yaml
{ "Oid": "1.3.6.1.4.1.47477.999.8.1.0",  "MetricName": "e2e_T1P1_eval1" },
{ "Oid": "1.3.6.1.4.1.47477.999.8.2.0",  "MetricName": "e2e_T1P1_eval2" },
...
```

Note the `.0` suffix on OID strings (SNMP scalar instance convention) — required by existing code.

### Pattern 4: Hardcoded Mapping File

The mapping file is sourced by Phase 83's interpreter. It must expose per-tenant per-role OID suffix lookup in a form that `bash` can index. The cleanest form matching "add one line per new tenant/metric" is flat variable naming or associative arrays.

**Recommended: associative array approach**

```bash
# tests/e2e/lib/oid_map.sh
# Source: project convention (lib/common.sh, lib/sim.sh patterns)
#
# OID_MAP -- per-tenant per-role OID suffixes and values.
# Format: OID_MAP[Tenant.Role.N.oid]   = "suffix"  (e.g. "8.1")
#         OID_MAP[Tenant.Role.N.healthy] = value
#         OID_MAP[Tenant.Role.N.violated] = value
#
# Tenant names: T1_P1 T2_P1 T1_P2 T2_P2
# Roles: E (Evaluate), R (Resolved)
# N: 1-based index within that role for the tenant

declare -A OID_MAP

# T1_P1: Priority 1, 2 Evaluate, 2 Resolved
OID_MAP[T1_P1.E.1.oid]="8.1";  OID_MAP[T1_P1.E.1.healthy]="10"; OID_MAP[T1_P1.E.1.violated]="0"
OID_MAP[T1_P1.E.2.oid]="8.2";  OID_MAP[T1_P1.E.2.healthy]="10"; OID_MAP[T1_P1.E.2.violated]="0"
OID_MAP[T1_P1.R.1.oid]="8.3";  OID_MAP[T1_P1.R.1.healthy]="1";  OID_MAP[T1_P1.R.1.violated]="0"
OID_MAP[T1_P1.R.2.oid]="8.4";  OID_MAP[T1_P1.R.2.healthy]="1";  OID_MAP[T1_P1.R.2.violated]="0"
```

This layout satisfies MAP-02: adding a new tenant = adding one block of lines (one line per OID), adding a new metric = adding one line.

**Tenant metadata** (metric counts per role, needed by Phase 83 for validation):

```bash
# Tenant metadata for interpreter validation
TENANT_EVAL_COUNT[T1_P1]=2
TENANT_RES_COUNT[T1_P1]=2
TENANT_EVAL_COUNT[T2_P1]=4
TENANT_RES_COUNT[T2_P1]=4
TENANT_EVAL_COUNT[T1_P2]=2
TENANT_RES_COUNT[T1_P2]=2
TENANT_EVAL_COUNT[T2_P2]=4
TENANT_RES_COUNT[T2_P2]=4
VALID_TENANTS="T1_P1 T2_P1 T1_P2 T2_P2"
```

### Pattern 5: Devices ConfigMap Extension

The new metric names must be polled. Add a poll group for the 24 new OIDs to the E2E-SIM device entry in `simetra-devices.yaml`. Use `IntervalSeconds: 1` to match the existing `e2e_eval_T2/T3/T4` poll group that uses 1s for fast readiness in tests.

```json
{
  "IntervalSeconds": 1,
  "GraceMultiplier": 2.0,
  "Metrics": [
    {"MetricName": "e2e_T1P1_eval1"},
    {"MetricName": "e2e_T1P1_eval2"},
    ...all 24 new metric names...
  ]
}
```

Also add entries to the local dev fallback at `src/SnmpCollector/config/devices.json`.

### Anti-Patterns to Avoid

- **Reusing existing OID subtrees**: subtrees 5/6/7 are wired to PSS fixture scenarios by OID suffix; using them for v2.6 tenants would make existing scenarios (107-113) produce wrong results.
- **OID strings without `.0` suffix**: The SNMP collector resolves `{oid}.0` for scalar instances. OID map entries must include the `.0` suffix.
- **Not updating `_make_scenario` baseline**: New OIDs not in the baseline default to `STALE` sentinel, causing `NoSuchInstance` responses and immediate stale-tier violations at startup — tenants will not reach Healthy.
- **Not updating local dev fallback**: `src/SnmpCollector/config/oid_metric_map.json` and `devices.json` must also be updated or local dev will diverge.
- **Using `Name` field without underscores**: The ConfigMap tenant `Name` field becomes `tenant_id` in Prometheus. Use `T1_P1` not `T1-P1` to keep consistency with the command pattern format.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OID collision detection | Manual comparison | Assign one subtree per tenant | Subtree isolation is structural — impossible to collide by construction |
| Mapping file lookup logic | Complex bash function | Associative array key convention `Tenant.Role.N.field` | O(1) lookup, no loops needed |
| Command OID for v2.6 tenants | New simulator OID | Reuse existing `e2e_set_bypass` (`.999.4.4.0`) | Already registered, already in command map, no simulator changes needed for commands |

**Key insight:** OID collision is entirely a structural problem solved by subtree assignment. There is no need for runtime collision checks.

## Common Pitfalls

### Pitfall 1: Missing OIDs from Simulator Baseline

**What goes wrong:** New OIDs are registered in SNMP but not added to `_make_scenario()` baseline dict. The `DynamicInstance.getValue()` falls through to `SCENARIOS[_active_scenario].get(self._oid_str, STALE)` and returns `STALE`, producing `NoSuchInstance`. All 4 tenants enter stale state immediately.

**Why it happens:** The simulator has two levels: (1) OID registration (MIB) and (2) OID values (scenario dict). Registration alone is insufficient.

**How to avoid:** After adding OIDs to `TENANT_OIDS_V26`, also add all their keys with value `0` to the `baseline` dict inside `_make_scenario()`.

**Warning signs:** Tenants show Stale state in Grafana immediately after fixture applied.

### Pitfall 2: OID Map Missing Entries

**What goes wrong:** New metric names aren't in `simetra-oid-metric-map.yaml`. The SNMP collector resolves these as "Unknown" and exports metrics with `resolved_name="Unknown"`. The tenant vector can't match them to configured MetricNames, causing the holders to never receive values — tenants stay NotReady.

**Why it happens:** The OID map and the tenant fixture are separate ConfigMaps. Updating one without the other is easy to miss.

**How to avoid:** Plan the fixture and OID map extension together in a single plan. Verify after applying by checking Grafana for `resolved_name != "Unknown"` on new OIDs.

**Warning signs:** Tenants stuck in NotReady state after grace window passes.

### Pitfall 3: Missing Devices ConfigMap Entries

**What goes wrong:** New metric names aren't in the E2E-SIM device poll group. The SNMP collector never polls the new OIDs. Tenant holders receive no values and stay NotReady.

**Why it happens:** Three places must all know about new metrics: oid_metric_map, devices, and tenants.

**How to avoid:** Add the 24 new metric names to the E2E-SIM `Polls` section in `simetra-devices.yaml` (and `devices.json` local fallback). Use the existing 1s interval poll group pattern.

**Warning signs:** No `snmp_gauge` series in Prometheus for the new metric names.

### Pitfall 4: Grace Window Not Honored in Success Criteria

**What goes wrong:** Tenant is Healthy only after the readiness window passes with no violations. The readiness window for `TimeSeriesSize=3, GraceMultiplier=2.0, IntervalSeconds=1` is `3 × 1 × 2.0 = 6 seconds`. Success criterion 2 requires waiting for this window before asserting Healthy.

**Why it happens:** Checking Grafana immediately after fixture apply shows NotReady, not Healthy. This is correct behavior, not a bug.

**How to avoid:** Wait at least 6-8 seconds after the OIDs are primed with healthy values before checking state.

### Pitfall 5: Tenant Name Mismatch

**What goes wrong:** Fixture uses `"Name": "T1_P1"` but Grafana shows `tenant_id="T1_P1"`. If names use hyphens (`T1-P1`) or different capitalization, the mapping file keys won't match.

**Why it happens:** The tenant `Name` field propagates to Prometheus `tenant_id` labels directly.

**How to avoid:** Use exactly `T1_P1`, `T2_P1`, `T1_P2`, `T2_P2` (underscore, uppercase) in both the fixture and the mapping file keys.

### Pitfall 6: Local Dev Config Not Updated

**What goes wrong:** `src/SnmpCollector/config/oid_metric_map.json` and `devices.json` are the local dev fallbacks. If not updated, running the collector locally resolves new OIDs as Unknown.

**Why it happens:** Two copies of the config: one in `src/SnmpCollector/config/` (local dev) and one in `deploy/k8s/snmp-collector/` (K8s).

**How to avoid:** Update both locations in the same plan.

## Code Examples

### Complete OID Suffix Table

Full v2.6 OID suffix assignment (OID suffix = last two segments after E2E_PREFIX):

```
Tenant   Role    Slot  OID Suffix  MetricName
------   ------  ----  ----------  ----------
T1_P1    E       1     8.1         e2e_T1P1_eval1
T1_P1    E       2     8.2         e2e_T1P1_eval2
T1_P1    R       1     8.3         e2e_T1P1_res1
T1_P1    R       2     8.4         e2e_T1P1_res2
T2_P1    E       1     9.1         e2e_T2P1_eval1
T2_P1    E       2     9.2         e2e_T2P1_eval2
T2_P1    E       3     9.3         e2e_T2P1_eval3
T2_P1    E       4     9.4         e2e_T2P1_eval4
T2_P1    R       1     9.5         e2e_T2P1_res1
T2_P1    R       2     9.6         e2e_T2P1_res2
T2_P1    R       3     9.7         e2e_T2P1_res3
T2_P1    R       4     9.8         e2e_T2P1_res4
T1_P2    E       1     10.1        e2e_T1P2_eval1
T1_P2    E       2     10.2        e2e_T1P2_eval2
T1_P2    R       1     10.3        e2e_T1P2_res1
T1_P2    R       2     10.4        e2e_T1P2_res2
T2_P2    E       1     11.1        e2e_T2P2_eval1
T2_P2    E       2     11.2        e2e_T2P2_eval2
T2_P2    E       3     11.3        e2e_T2P2_eval3
T2_P2    E       4     11.4        e2e_T2P2_eval4
T2_P2    R       1     11.5        e2e_T2P2_res1
T2_P2    R       2     11.6        e2e_T2P2_res2
T2_P2    R       3     11.7        e2e_T2P2_res3
T2_P2    R       4     11.8        e2e_T2P2_res4
```

Total new OIDs: 24 polled. No new command OIDs (reuse e2e_set_bypass).

### Simulator Addition Pattern

```python
# Source: simulators/e2e-sim/e2e_simulator.py TENANT_OIDS block (lines 186-197)

# Phase 82: v2.6 4-tenant OIDs (subtrees .999.8.x - .999.11.x)
TENANT_OIDS_V26 = [
    # T1_P1 (Priority 1, 2E/2R)
    (f"{E2E_PREFIX}.8.1",  "e2e_T1P1_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.2",  "e2e_T1P1_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.3",  "e2e_T1P1_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.4",  "e2e_T1P1_res2",  v2c.Gauge32, False),
    # T2_P1 (Priority 1, 4E/4R)
    (f"{E2E_PREFIX}.9.1",  "e2e_T2P1_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.2",  "e2e_T2P1_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.3",  "e2e_T2P1_eval3", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.4",  "e2e_T2P1_eval4", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.5",  "e2e_T2P1_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.6",  "e2e_T2P1_res2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.7",  "e2e_T2P1_res3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.8",  "e2e_T2P1_res4",  v2c.Gauge32, False),
    # T1_P2 (Priority 2, 2E/2R)
    (f"{E2E_PREFIX}.10.1", "e2e_T1P2_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.10.2", "e2e_T1P2_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.10.3", "e2e_T1P2_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.10.4", "e2e_T1P2_res2",  v2c.Gauge32, False),
    # T2_P2 (Priority 2, 4E/4R)
    (f"{E2E_PREFIX}.11.1", "e2e_T2P2_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.2", "e2e_T2P2_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.3", "e2e_T2P2_eval3", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.4", "e2e_T2P2_eval4", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.5", "e2e_T2P2_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.6", "e2e_T2P2_res2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.7", "e2e_T2P2_res3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.8", "e2e_T2P2_res4",  v2c.Gauge32, False),
]
```

In `_make_scenario()` baseline dict, add all 24 new OID keys:

```python
# In _make_scenario() baseline dict:
f"{E2E_PREFIX}.8.1": 0,  f"{E2E_PREFIX}.8.2": 0,
f"{E2E_PREFIX}.8.3": 0,  f"{E2E_PREFIX}.8.4": 0,
# ... etc for all 24
```

In the OID registration loop, add `TENANT_OIDS_V26` to the list:

```python
for oid_str, label, syntax_cls, writable in TEST_OIDS + TENANT_OIDS + TENANT_OIDS_V26:
```

### Mapping File Structure

```bash
#!/usr/bin/env bash
# oid_map.sh -- v2.6 tenant OID mapping for manual simulation interpreter
#
# Source this file before using the interpreter.
# Key format: TENANT.ROLE.SLOT.FIELD
#   TENANT: T1_P1 | T2_P1 | T1_P2 | T2_P2
#   ROLE:   E (Evaluate) | R (Resolved)
#   SLOT:   1-based index within that role for this tenant
#   FIELD:  oid (suffix for sim_set_oid) | healthy | violated

declare -A OID_MAP

# ---------------------------------------------------------------------------
# T1_P1  Priority=1  2E / 2R / 1C
# ---------------------------------------------------------------------------
OID_MAP[T1_P1.E.1.oid]="8.1";  OID_MAP[T1_P1.E.1.healthy]="10"; OID_MAP[T1_P1.E.1.violated]="0"
OID_MAP[T1_P1.E.2.oid]="8.2";  OID_MAP[T1_P1.E.2.healthy]="10"; OID_MAP[T1_P1.E.2.violated]="0"
OID_MAP[T1_P1.R.1.oid]="8.3";  OID_MAP[T1_P1.R.1.healthy]="1";  OID_MAP[T1_P1.R.1.violated]="0"
OID_MAP[T1_P1.R.2.oid]="8.4";  OID_MAP[T1_P1.R.2.healthy]="1";  OID_MAP[T1_P1.R.2.violated]="0"

# ---------------------------------------------------------------------------
# T2_P1  Priority=1  4E / 4R / 1C
# ---------------------------------------------------------------------------
OID_MAP[T2_P1.E.1.oid]="9.1";  OID_MAP[T2_P1.E.1.healthy]="10"; OID_MAP[T2_P1.E.1.violated]="0"
OID_MAP[T2_P1.E.2.oid]="9.2";  OID_MAP[T2_P1.E.2.healthy]="10"; OID_MAP[T2_P1.E.2.violated]="0"
OID_MAP[T2_P1.E.3.oid]="9.3";  OID_MAP[T2_P1.E.3.healthy]="10"; OID_MAP[T2_P1.E.3.violated]="0"
OID_MAP[T2_P1.E.4.oid]="9.4";  OID_MAP[T2_P1.E.4.healthy]="10"; OID_MAP[T2_P1.E.4.violated]="0"
OID_MAP[T2_P1.R.1.oid]="9.5";  OID_MAP[T2_P1.R.1.healthy]="1";  OID_MAP[T2_P1.R.1.violated]="0"
OID_MAP[T2_P1.R.2.oid]="9.6";  OID_MAP[T2_P1.R.2.healthy]="1";  OID_MAP[T2_P1.R.2.violated]="0"
OID_MAP[T2_P1.R.3.oid]="9.7";  OID_MAP[T2_P1.R.3.healthy]="1";  OID_MAP[T2_P1.R.3.violated]="0"
OID_MAP[T2_P1.R.4.oid]="9.8";  OID_MAP[T2_P1.R.4.healthy]="1";  OID_MAP[T2_P1.R.4.violated]="0"

# ---------------------------------------------------------------------------
# T1_P2  Priority=2  2E / 2R / 1C
# ---------------------------------------------------------------------------
OID_MAP[T1_P2.E.1.oid]="10.1"; OID_MAP[T1_P2.E.1.healthy]="10"; OID_MAP[T1_P2.E.1.violated]="0"
OID_MAP[T1_P2.E.2.oid]="10.2"; OID_MAP[T1_P2.E.2.healthy]="10"; OID_MAP[T1_P2.E.2.violated]="0"
OID_MAP[T1_P2.R.1.oid]="10.3"; OID_MAP[T1_P2.R.1.healthy]="1";  OID_MAP[T1_P2.R.1.violated]="0"
OID_MAP[T1_P2.R.2.oid]="10.4"; OID_MAP[T1_P2.R.2.healthy]="1";  OID_MAP[T1_P2.R.2.violated]="0"

# ---------------------------------------------------------------------------
# T2_P2  Priority=2  4E / 4R / 1C
# ---------------------------------------------------------------------------
OID_MAP[T2_P2.E.1.oid]="11.1"; OID_MAP[T2_P2.E.1.healthy]="10"; OID_MAP[T2_P2.E.1.violated]="0"
OID_MAP[T2_P2.E.2.oid]="11.2"; OID_MAP[T2_P2.E.2.healthy]="10"; OID_MAP[T2_P2.E.2.violated]="0"
OID_MAP[T2_P2.E.3.oid]="11.3"; OID_MAP[T2_P2.E.3.healthy]="10"; OID_MAP[T2_P2.E.3.violated]="0"
OID_MAP[T2_P2.E.4.oid]="11.4"; OID_MAP[T2_P2.E.4.healthy]="10"; OID_MAP[T2_P2.E.4.violated]="0"
OID_MAP[T2_P2.R.1.oid]="11.5"; OID_MAP[T2_P2.R.1.healthy]="1";  OID_MAP[T2_P2.R.1.violated]="0"
OID_MAP[T2_P2.R.2.oid]="11.6"; OID_MAP[T2_P2.R.2.healthy]="1";  OID_MAP[T2_P2.R.2.violated]="0"
OID_MAP[T2_P2.R.3.oid]="11.7"; OID_MAP[T2_P2.R.3.healthy]="1";  OID_MAP[T2_P2.R.3.violated]="0"
OID_MAP[T2_P2.R.4.oid]="11.8"; OID_MAP[T2_P2.R.4.healthy]="1";  OID_MAP[T2_P2.R.4.violated]="0"

# ---------------------------------------------------------------------------
# Tenant metadata (for Phase 83 interpreter validation)
# ---------------------------------------------------------------------------
declare -A TENANT_EVAL_COUNT TENANT_RES_COUNT
TENANT_EVAL_COUNT[T1_P1]=2; TENANT_RES_COUNT[T1_P1]=2
TENANT_EVAL_COUNT[T2_P1]=4; TENANT_RES_COUNT[T2_P1]=4
TENANT_EVAL_COUNT[T1_P2]=2; TENANT_RES_COUNT[T1_P2]=2
TENANT_EVAL_COUNT[T2_P2]=4; TENANT_RES_COUNT[T2_P2]=4
VALID_TENANTS="T1_P1 T2_P1 T1_P2 T2_P2"
```

### Tenant ConfigMap Threshold Design

Evaluate metrics: `Threshold: { "Min": 10.0 }`. Value `10` = healthy (above min), value `0` = violated (below min).

Resolved metrics: `Threshold: { "Min": 1.0 }`. Value `1` = healthy (above min), value `0` = violated (below min).

This matches the existing PSS fixture conventions exactly (e.g. `tenant-cfg06-pss-single.yaml`) and is the canonical approach for E2E fixtures.

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| Per-subtree ad-hoc naming (T2/T3/T4) | Per-tenant structured subtree with slot-indexed names | Enables interpreter lookup |
| Fixtures share OID suffixes across tenants via scenarios | Each tenant has exclusive OID subtree | Collision-free by construction, no scenario switching needed |

**Deprecated/outdated:**
- Scenario switching (sim_set_scenario): Not used by v2.6 fixture. All v2.6 control goes through per-OID HTTP endpoints (`/oid/{suffix}/{value}`) directly. The fixture fixture uses the `default` scenario baseline (all OIDs start at 0).

## Open Questions

1. **Simulator rebuild required?**
   - What we know: The simulator is deployed as a Docker container in K8s. Changes to `e2e_simulator.py` require rebuilding and redeploying the image.
   - What's unclear: The deployment process — whether this is a `docker build` + `kubectl rollout restart` or a more involved step.
   - Recommendation: Look at `deploy/k8s/simulators/e2e-sim-deployment.yaml` to confirm the image tag and pull policy. Plan should include a simulator rebuild step.

2. **Local dev oid_metric_map.json sync**
   - What we know: Both `src/SnmpCollector/config/oid_metric_map.json` and `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` need updating.
   - What's unclear: Whether the K8s ConfigMap is applied via `kubectl apply` manually or if there's a deploy script.
   - Recommendation: Plan should update both files and include a `kubectl apply` step for the ConfigMap.

3. **Fixture naming: `tenant-cfg12` or different scheme?**
   - What we know: Existing fixtures use `tenant-cfg01` through `tenant-cfg11`.
   - What's unclear: Whether the v2.6 fixture should follow the sequential scheme or use a v2.6-specific name.
   - Recommendation: Use `tenant-cfg12-v26-four-tenant.yaml` — follows the sequence and is self-documenting.

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection: `simulators/e2e-sim/e2e_simulator.py` — OID allocation, scenario dict, HTTP endpoint behavior
- Direct codebase inspection: `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` — existing OID map format and entries
- Direct codebase inspection: `tests/e2e/fixtures/tenant-cfg08-pss-four-tenant.yaml` — 4-tenant fixture pattern
- Direct codebase inspection: `tests/e2e/lib/sim.sh` — `sim_set_oid`, `sim_set_oid_stale` function signatures
- Direct codebase inspection: `deploy/k8s/snmp-collector/simetra-devices.yaml` — poll group format for E2E-SIM device

### Secondary (MEDIUM confidence)
- `.planning/STATE.md` key facts section — v2.6 tenant specs (2E/2R/1C, 4E/4R/1C) confirmed from prior planning session
- `.planning/ROADMAP.md` phase 82 requirements — FIX-01 through MAP-02 requirements

## Metadata

**Confidence breakdown:**
- OID allocation design: HIGH — direct inspection of all existing OID assignments, no external sources needed
- Simulator extension pattern: HIGH — exact pattern verified from existing `TENANT_OIDS` block
- Mapping file structure: HIGH — Bash associative arrays are the correct tool; pattern fits `lib/` conventions
- Pitfalls: HIGH — all derived from direct code inspection of the failure modes

**Research date:** 2026-03-24
**Valid until:** 2026-04-24 (stable codebase, no external dependencies)
