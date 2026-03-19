# Phase 61: New E2E Suite Snapshot - Research

**Researched:** 2026-03-19
**Domain:** Bash E2E test authoring — simulator extension, tenant fixtures, scenario scripts
**Confidence:** HIGH

## Summary

This phase adds a comprehensive E2E test suite to prove every path through the 4-tier evaluation tree
and advance gate, using a 4-tenant setup (2 groups x 2 tenants). All decisions are locked in CONTEXT.md;
this research synthesizes what the planner needs from the existing codebase.

The E2E infrastructure (runner, lib/, simulator) is fully operational and well-patterned. New work falls
into three concrete tracks: (1) extend the simulator with new OIDs and a per-OID HTTP endpoint, (2) add
new tenant fixtures, and (3) write scenario scripts following the established playbook. The hardest
timing problem is the readiness grace window (Phase 60): the new tenants need priming before evaluation
starts, just like the existing STS-05/STS-06/MTS-03 scenarios handle today.

**Primary recommendation:** Model all new scenarios on the existing STS/MTS scripts. Use the per-OID
HTTP endpoint (new in this phase) to set values per-tenant precisely, and add a `sim_set_oid` helper
to sim.sh following the same pattern as `sim_set_scenario`.

## Standard Stack

### Core (all existing infrastructure — no new dependencies)

| Tool | Location | Purpose | Notes |
|------|----------|---------|-------|
| bash + set -euo pipefail | tests/e2e/scenarios/*.sh | Test scenario scripts | Source pattern from run-all.sh |
| lib/common.sh | tests/e2e/lib/common.sh | record_pass/record_fail, assert_delta_gt | Already sourced by run-all.sh |
| lib/sim.sh | tests/e2e/lib/sim.sh | sim_set_scenario, reset_scenario, poll_until_log | Extend with sim_set_oid |
| lib/prometheus.sh | tests/e2e/lib/prometheus.sh | snapshot_counter, poll_until, query_counter | Already sourced |
| lib/kubectl.sh | tests/e2e/lib/kubectl.sh | save_configmap, restore_configmap | Already sourced |
| lib/report.sh | tests/e2e/lib/report.sh | generate_report with category ranges | Needs new category for SNS suite |
| aiohttp (Python) | simulators/e2e-sim/e2e_simulator.py | HTTP control endpoint | Extend with /oid/{oid}/{value} route |
| pysnmp | simulators/e2e-sim/e2e_simulator.py | SNMP agent with DynamicInstance | Extend with new OIDs subtrees .999.5-7 |

### Supporting

| Tool | Location | Purpose | Notes |
|------|----------|---------|-------|
| tenant-cfg*.yaml | tests/e2e/fixtures/ | Tenant ConfigMap fixtures | New fixture: tenant-cfg05-four-tenant-snapshot.yaml |
| simetra-oid-metric-map.yaml | deploy/k8s/snmp-collector/ | Maps OIDs to MetricNames | Add 9 new OID entries for T2-T4 |
| snmp-collector-config.yaml | deploy/k8s/snmp-collector/ | App config | Needs SnapshotJob.IntervalSeconds:1 added |

## Architecture Patterns

### How the E2E runner discovers and executes scenarios

```
run-all.sh sources lib/*.sh, then:
for scenario in "$SCRIPT_DIR"/scenarios/[0-9]*.sh; do
    source "$scenario"
done
```

Every scenario is sourced (not executed as subshell). This means:
- All functions from lib/ are available inside scenarios
- Global state (SCENARIO_RESULTS, PASS_COUNT, FAIL_COUNT) accumulates across scenarios
- Scenarios must clean up their ConfigMap changes (restore_configmap) on exit

### How existing scenarios are numbered and what numbers are taken

Existing: 01-40. The new SNS (Snapshot) suite starts at **41**. There are 2 parts:
- Part 1: SNS state tests (5 results) — scripts 41-45
- Part 2: SNS advance gate tests (7 gate combinations) — scripts 46-52

### Pattern for a single-tenant state scenario

```bash
# Template derived from 29-sts-01-healthy.sh, 31-sts-03-resolved-gate.sh, etc.
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# 1. Save + apply fixture
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
kubectl apply -f "$FIXTURES_DIR/tenant-cfg05-four-tenant-snapshot.yaml" > /dev/null 2>&1 || true
poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30 || true

# 2. Prime (MANDATORY for readiness grace window)
#    For a tenant with TimeSeriesSize=3 IntervalSeconds=1 GraceMultiplier=2.0:
#    ReadinessGrace = 3 * 1 * 2.0 = 6 seconds
#    Prime by setting valid values and sleeping > ReadinessGrace
sim_set_oid ".999.4.1" "10"   # T1 evaluate in-range
sim_set_oid ".999.4.2" "1"    # T1 resolved1 in-range
sim_set_oid ".999.4.3" "1"    # T1 resolved2 in-range
sleep 8  # wait for readiness grace to pass (6s + margin)

# 3. Set desired test state via per-OID endpoint
sim_set_oid ".999.4.1" "0"    # T1 evaluate violated (< Min:10)

# 4. Assert (log poll or counter delta)
if poll_until_log 30 2 "e2e-tenant-G1-T1.*tier=..." 20; then
    record_pass "SNS-xx: ..." "..."
else
    record_fail "SNS-xx: ..." "..."
fi

# 5. Cleanup
reset_all_oids   # or sim_set_scenario default
restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
```

### Pattern for the per-OID HTTP endpoint

The new endpoint `POST /oid/{oid}/{value}` stores a value in `_oid_overrides` dict and
`DynamicInstance.getValue` checks overrides before falling back to the active scenario.
This allows tests to set individual OIDs for specific tenants without affecting others.

```python
# In e2e_simulator.py

# New state dict for per-OID overrides
_oid_overrides: dict[str, int] = {}

async def post_oid_value(request: web.Request) -> web.Response:
    oid = request.match_info["oid"]      # e.g. "999.4.1" or ".999.4.1"
    value = int(request.match_info["value"])
    full_oid = f"{E2E_PREFIX}.{oid.lstrip('.')}" if not oid.startswith(E2E_PREFIX) else oid
    _oid_overrides[full_oid] = value
    log.info("OID override set: %s = %d", full_oid, value)
    return web.json_response({"oid": full_oid, "value": value})

async def delete_oid_overrides(request: web.Request) -> web.Response:
    _oid_overrides.clear()
    log.info("OID overrides cleared")
    return web.json_response({"cleared": True})
```

And `DynamicInstance.getValue` checks overrides first:
```python
def getValue(self, name, **ctx):
    # Check per-OID override first
    if self._oid_str in _oid_overrides:
        return self.getSyntax().clone(_oid_overrides[self._oid_str])
    # Fall back to active scenario
    val = SCENARIOS[_active_scenario].get(self._oid_str, STALE)
    if val is STALE:
        raise NoSuchInstanceError(name=name, idx=(0,))
    return self.getSyntax().clone(val)
```

### sim.sh helper additions

```bash
# In tests/e2e/lib/sim.sh

# sim_set_oid <oid_suffix> <value>
# POST to /oid/{oid}/{value}. oid_suffix is relative to E2E prefix e.g. "4.1" or "5.1"
sim_set_oid() {
    local oid="$1"
    local value="$2"
    log_info "Setting OID ${oid} = ${value}"
    local http_code
    http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
        -X POST "${SIM_URL}/oid/${oid}/${value}" 2>/dev/null) || {
        log_error "curl failed setting OID ${oid}"
        return 1
    }
    if [ "$http_code" = "200" ]; then
        return 0
    else
        log_error "Unexpected HTTP ${http_code} setting OID ${oid}"
        return 1
    fi
}

# reset_oid_overrides — clear all per-OID overrides, fall back to active scenario
reset_oid_overrides() {
    curl -sf -o /dev/null -X DELETE "${SIM_URL}/oid/overrides" 2>/dev/null || true
}
```

### Recommended project structure additions

```
simulators/e2e-sim/
└── e2e_simulator.py        # Add /oid/{oid}/{value} route + _oid_overrides dict
                            # Add 9 new OIDs for .999.5.x, .999.6.x, .999.7.x
                            # Add DELETE /oid/overrides reset endpoint

tests/e2e/
├── fixtures/
│   └── tenant-cfg05-four-tenant-snapshot.yaml  # NEW: 4-tenant, 2 groups
├── lib/
│   └── sim.sh              # Add sim_set_oid, reset_oid_overrides
├── scenarios/
│   ├── 41-sns-01-not-ready.sh
│   ├── 42-sns-02-stale-to-commands.sh
│   ├── 43-sns-03-resolved.sh
│   ├── 44-sns-04-unresolved.sh
│   ├── 45-sns-05-healthy.sh
│   ├── 46-sns-a1-both-resolved.sh
│   ├── 47-sns-a2-both-healthy.sh
│   ├── 48-sns-a3-resolved-healthy.sh
│   ├── 49-sns-b1-both-unresolved.sh
│   ├── 50-sns-b2-both-not-ready.sh
│   ├── 51-sns-b3-resolved-unresolved.sh
│   └── 52-sns-b4-healthy-unresolved.sh

deploy/k8s/snmp-collector/
└── simetra-oid-metric-map.yaml  # Add 9 OID entries for T2-T4 metrics
```

### Anti-Patterns to Avoid

- **Not priming before asserting readiness**: All 4 tenants need their holders populated before any evaluation will occur. With IntervalSeconds=1 and GraceMultiplier=2.0 and TimeSeriesSize=3: ReadinessGrace = 3*1*2.0 = 6s. Prime with in-range values and sleep >=8s at fixture load time.
- **Shared OIDs between tenants**: The CONTEXT locks per-tenant OIDs. Do not attempt to reuse T1's .999.4.x for T2-T4. Each tenant must reference its own metric names (e2e_eval_T2, e2e_res1_T2, etc.) mapped to .999.5.1, .999.5.2, .999.5.3.
- **Not resetting OID overrides in cleanup**: If a test sets OID overrides and does not reset them, the next test will see stale state. Call `reset_oid_overrides` (or `reset_scenario` + explicit set) in the cleanup block.
- **Asserting P2 group is blocked using a brief log window**: Pattern from MTS-02A/MTS-03C uses `--since=15s` or `--since=120s`. For the gate tests (SNS-Bx), since the SnapshotJob interval is 1s, it fires frequently. Check over a short but definitive window (e.g., 10s of observation after confirming P1 state).
- **Negative assertions for P2 without confirming P1 first**: Always confirm P1's state (tier log) before asserting P2 is absent. If P1 confirmation times out, P2 absence means nothing.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Waiting for a log line | Custom sleep loop | `poll_until_log <timeout> <interval> <pattern>` from sim.sh | Already handles multi-pod, --since window, grep |
| Waiting for counter increment | Custom counter check loop | `poll_until <timeout> <interval> <metric> <label> <baseline>` from prometheus.sh | Already handles Prometheus query, deadline math |
| Counter snapshot | Raw curl query | `snapshot_counter <metric> <label_filter>` from prometheus.sh | Returns integer, handles missing metric as 0 |
| Saving/restoring ConfigMap | kubectl get + apply | `save_configmap / restore_configmap` from kubectl.sh | Strips resourceVersion/uid for clean reapply |
| Pass/fail tracking | Custom arrays | `record_pass / record_fail` from common.sh | Feeds into generate_report |
| Simulator value control | New scenario variants | `sim_set_oid` (new) for per-OID control | One call per tenant, no combinatorial scenario explosion |

**Key insight:** The existing lib/ functions handle all timing, K8s interaction, and reporting. New scenarios only need test logic and assertions — they should not duplicate infrastructure.

## Common Pitfalls

### Pitfall 1: Readiness grace window blocks first evaluation

**What goes wrong:** With TimeSeriesSize=3, IntervalSeconds=1, GraceMultiplier=2.0, the ReadinessGrace
is 6 seconds. After applying a new fixture, all 4 tenant holders are fresh (ConstructedAt=now). The
first 6 seconds of SnapshotJob cycles return `TierResult.Unresolved` with a "not ready (in grace window)"
debug log. If the test immediately polls for a tier=2/3/4 log, it may get a timeout or catch a stale log.

**Why it happens:** Phase 60 readiness window prevents sentinel-poisoned evaluation during fill.

**How to avoid:** Prime tenants after fixture load: set all OIDs to in-range values, sleep 8s (grace=6s
+ margin). After priming, all holders have real data and IsReady=true regardless of ConstructedAt.

**Warning signs:** Test sees "not ready (in grace window)" debug logs but no tier=2/3/4 in assertion window.

### Pitfall 2: SnapshotJob log level is Debug — not Info

**What goes wrong:** The tier=1/2/3 logs in SnapshotJob.cs all use `_logger.LogDebug(...)`. Only tier=4
uses `_logger.LogInformation(...)`. The E2E cluster runs with `LogLevel.Default: "Debug"` per
`snmp-collector-config.yaml`, so all tiers are visible. If this ever changes to Information, tier<4 logs
will vanish.

**How to avoid:** Rely on the existing `snmp-collector-config.yaml` K8s config which sets Debug globally.
No action needed for this phase, but note it as a dependency.

**Warning signs:** poll_until_log times out for tier=1/2/3 patterns even though the scenario is correct.

### Pitfall 3: Log pattern matching must include tenant name

**What goes wrong:** Bare `tier=4` pattern matches ALL tenants. In a 4-tenant scenario, P1/P2 tenants
will also log tier=4 while you are trying to assert P2 group is NOT evaluated. Scope all log patterns
to the specific tenant name: `"e2e-tenant-G1-T1.*tier=4"`.

**Why it happens:** Existing scenarios (STS-01 through STS-06) use a single-tenant fixture so bare
patterns worked. With 4 tenants it breaks.

**How to avoid:** Every `poll_until_log` call must scope to the tenant of interest. Every negative
"absent" check must scope to the group being excluded.

### Pitfall 4: OID map ConfigMap must be reloaded for new OIDs to be recognized

**What goes wrong:** The 9 new OIDs (.999.5.x, .999.6.x, .999.7.x) are mapped to metric names in
`simetra-oid-metric-map.yaml`. If the configmap reload does not occur after `kubectl apply`, the
pipeline will receive poll values but silently drop them as "unknown OID" (no MetricName match).

**Why it happens:** The OID map watcher detects configmap changes asynchronously (watch loop). There is
typically a 2-30s delay before the new map is active.

**How to avoid:** After applying the updated OID map ConfigMap (or deploying with it pre-baked), wait
for an "OID map reload" log entry. Alternatively, deploy the updated configmap before running the E2E
suite so it is already active. The safest approach for this phase is to include the new OID entries in
the base `simetra-oid-metric-map.yaml` that ships with the cluster.

### Pitfall 5: Per-OID overrides survive across test cleanup

**What goes wrong:** A scenario sets OID overrides (e.g., T1 evaluate=0) via `sim_set_oid` and then
fails mid-test. The cleanup block runs but only calls `reset_scenario`, which switches the scenario
name but does NOT clear `_oid_overrides`. The next scenario starts with stale per-OID values.

**How to avoid:** Always call `reset_oid_overrides` (DELETE /oid/overrides) in every scenario cleanup
block, in addition to `reset_scenario`. Alternatively, make `reset_scenario` also call
`reset_oid_overrides` — a small change to sim.sh.

### Pitfall 6: Advance gate assertion timing with 1s SnapshotJob interval

**What goes wrong:** With 1s SnapshotJob interval, the tenant cycles fire very fast. The "P2 absent"
check (negative assertion) needs a window long enough to be meaningful. If the window is too short
(1-2s), the assertion is statistically weak. If too long, it adds unnecessary test time.

**How to avoid:** Use 10-15s observation window for negative assertions. With 1s interval, 10 cycles
is a strong signal. Pattern: confirm P1 state first (poll_until_log), then check P2 absent over
`--since=15s` window.

## Code Examples

### Tenant fixture structure (4-tenant, 2-group)

```yaml
# Source: tenant-cfg03-two-diff-prio.yaml (existing pattern), extended to 4 tenants
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenants
  namespace: simetra
data:
  tenants.json: |
    [
      {
        "Name": "e2e-tenant-G1-T1",
        "Priority": 1,
        "SuppressionWindowSeconds": 10,
        "Metrics": [
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_eval_T1",
            "TimeSeriesSize": 3,
            "GraceMultiplier": 2.0,
            "Role": "Evaluate",
            "Threshold": { "Min": 10.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_res1_T1",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_res2_T1",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0 }
          }
        ],
        "Commands": [
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "CommandName": "e2e_command_response",
            "Value": "0",
            "ValueType": "Integer32"
          }
        ]
      }
      // ... G1-T2 (Priority=1), G2-T3 (Priority=2), G2-T4 (Priority=2) same structure
    ]
```

### OID map entries for the 9 new OIDs

```json
// Source: deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml (existing format)
{ "Oid": "1.3.6.1.4.1.47477.999.5.1.0", "MetricName": "e2e_eval_T2"  },
{ "Oid": "1.3.6.1.4.1.47477.999.5.2.0", "MetricName": "e2e_res1_T2"  },
{ "Oid": "1.3.6.1.4.1.47477.999.5.3.0", "MetricName": "e2e_res2_T2"  },
{ "Oid": "1.3.6.1.4.1.47477.999.6.1.0", "MetricName": "e2e_eval_T3"  },
{ "Oid": "1.3.6.1.4.1.47477.999.6.2.0", "MetricName": "e2e_res1_T3"  },
{ "Oid": "1.3.6.1.4.1.47477.999.6.3.0", "MetricName": "e2e_res2_T3"  },
{ "Oid": "1.3.6.1.4.1.47477.999.7.1.0", "MetricName": "e2e_eval_T4"  },
{ "Oid": "1.3.6.1.4.1.47477.999.7.2.0", "MetricName": "e2e_res1_T4"  },
{ "Oid": "1.3.6.1.4.1.47477.999.7.3.0", "MetricName": "e2e_res2_T4"  }
```

T1 existing OIDs in the map:
```json
// Already present:
{ "Oid": "1.3.6.1.4.1.47477.999.4.1.0", "MetricName": "e2e_port_utilization" }
{ "Oid": "1.3.6.1.4.1.47477.999.4.2.0", "MetricName": "e2e_channel_state"     }
{ "Oid": "1.3.6.1.4.1.47477.999.4.3.0", "MetricName": "e2e_bypass_status"     }
```

The new fixture must name T1 metrics "e2e_eval_T1", "e2e_res1_T1", "e2e_res2_T1" mapped to the
existing .999.4.1, .999.4.2, .999.4.3 OIDs. Or reuse the existing metric names (e2e_port_utilization,
e2e_channel_state, e2e_bypass_status) for T1 — either works, but separate names are cleaner.

### Simulator OID registration for new subtrees

```python
# Source: e2e_simulator.py (existing DynamicInstance pattern)
NEW_TEST_OIDS = [
    # T2: subtree .999.5.x
    (f"{E2E_PREFIX}.5.1", "e2e_eval_T2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.5.2", "e2e_res1_T2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.5.3", "e2e_res2_T2",  v2c.Gauge32, False),
    # T3: subtree .999.6.x
    (f"{E2E_PREFIX}.6.1", "e2e_eval_T3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.6.2", "e2e_res1_T3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.6.3", "e2e_res2_T3",  v2c.Gauge32, False),
    # T4: subtree .999.7.x
    (f"{E2E_PREFIX}.7.1", "e2e_eval_T4",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.7.2", "e2e_res1_T4",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.7.3", "e2e_res2_T4",  v2c.Gauge32, False),
]
# All registered as DynamicInstance (same as existing TEST_OIDS)
# Default value in baseline scenario: 0 for all
```

### State encoding table (how to produce each result via OID values)

| Result | eval OID | res1 OID | res2 OID | Notes |
|--------|----------|----------|----------|-------|
| Not Ready | any | any | any | In grace window — no data yet, or freshly constructed |
| Stale → Commands | STALE | STALE | STALE | Switch to STALE override after priming |
| Resolved (tier=2) | 0 | 0 | 0 | Both resolved < Min:1 (violated) — tier=2 |
| Unresolved (commands, tier=4) | 0 | 1 | 1 | eval < Min:10 (violated), resolved in range |
| Healthy (tier=3) | 10 | 1 | 1 | eval >= Min:10 (not violated) |

For "STALE": use a special sentinel approach. The per-OID endpoint should support a sentinel value
(e.g., POST /oid/{oid}/stale) OR a separate `/oid/{oid}/stale` endpoint that sets the OID to
return NoSuchInstance. Alternatively, add a `_stale_oids` set in the simulator and check it in
`getValue` before `_oid_overrides`.

### SnapshotJob log patterns for polling

```bash
# Source: SnapshotJob.cs — verified log formats

# Pre-tier: not ready (LogDebug)
"Tenant e2e-tenant-G1-T1 priority=1 not ready (in grace window) — skipping"
# Pattern: "e2e-tenant-G1-T1.*not ready"

# Tier 1: stale (LogDebug)
"Tenant e2e-tenant-G1-T1 priority=1 tier=1 stale — skipping to commands"
# Pattern: "e2e-tenant-G1-T1.*tier=1 stale"

# Tier 2: all resolved violated (LogDebug)
"Tenant e2e-tenant-G1-T1 priority=1 tier=2 — all resolved violated, no commands"
# Pattern: "e2e-tenant-G1-T1.*tier=2 — all resolved violated"

# Tier 3: healthy (LogDebug)
"Tenant e2e-tenant-G1-T1 priority=1 tier=3 — not all evaluate metrics violated, no action"
# Pattern: "e2e-tenant-G1-T1.*tier=3"

# Tier 4: commands enqueued (LogInformation)
"Tenant e2e-tenant-G1-T1 priority=1 tier=4 — commands enqueued, count=1"
# Pattern: "e2e-tenant-G1-T1.*tier=4 — commands enqueued"
```

All tier=1/2/3 logs are LogDebug. The E2E cluster uses LogLevel.Default=Debug per snmp-collector-config.yaml.

### Readiness timing math for the new fixture

```
TimeSeriesSize=3, IntervalSeconds=1, GraceMultiplier=2.0
ReadinessGrace = 3 * 1 * 2.0 = 6 seconds

SnapshotJob.IntervalSeconds: 1 second (E2E override — requires adding to snmp-collector-config.yaml
or a separate E2E appsettings override)

Prime sequence:
  - Apply fixture → all holders at ConstructedAt=now → not ready for 6s
  - Set OIDs to valid values immediately (before grace ends)
  - sleep 8  → all holders now past grace, IsReady=true (OR data is present, which also = ready)
  - Now assertions can begin
```

### Advance gate test pattern (Part 2)

```bash
# Template derived from 35-mts-02-advance-gate.sh

# Set P1 group state
sim_set_oid "4.1" "0"  # T1 eval violated (tier=4)
sim_set_oid "4.2" "1"  # T1 res1 in-range
sim_set_oid "4.3" "1"  # T1 res2 in-range
sim_set_oid "5.1" "0"  # T2 eval violated (tier=4)
sim_set_oid "5.2" "1"  # T2 res1 in-range
sim_set_oid "5.3" "1"  # T2 res2 in-range

# Assert P1 tenants reach expected tier
if poll_until_log 30 2 "e2e-tenant-G1-T1.*tier=4 — commands enqueued" 20; then
    record_pass "SNS-B1: G1-T1 tier=4 (Unresolved)" "..."
fi

# Confirm gate blocks by asserting P2 tenants NOT evaluated
P2_FOUND=0
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
for POD in $PODS; do
    P2_LOGS=$(kubectl logs "$POD" -n simetra --since=10s 2>/dev/null \
        | grep "e2e-tenant-G2.*tier=" || echo "") || true
    if [ -n "$P2_LOGS" ]; then P2_FOUND=1; break; fi
done
if [ "$P2_FOUND" -eq 0 ]; then
    record_pass "SNS-B1: G2 not evaluated (gate blocked)" "..."
fi
```

### report.sh category extension

```bash
# Source: tests/e2e/lib/report.sh — existing categories end at index 40 (scenarios 01-40)
# New SNS category covers scenarios 41-52 (0-based indices 40-51)

_REPORT_CATEGORIES=(
    "Pipeline Counters|0|9"
    "Business Metrics|10|22"
    "OID Mutations|23|25"
    "Device Lifecycle|26|27"
    "Snapshot Evaluation|28|40"
    "Snapshot State Suite|41|51"    # NEW: indices 41-51 = scenarios 42-52 (1-based)
)
```

Note: report.sh uses 0-based indices. Current last scenario is 40 (mts-03-starvation-proof.sh = index
39 in 0-based, since there are 40 scenarios 01-40). The 12 new SNS scenarios occupy indices 40-51.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Shared OIDs for all tenants | Per-tenant OID subtrees | Phase 61 (this phase) | Independent control per tenant |
| Scenario-based sim control only | Per-OID HTTP endpoint + scenarios | Phase 61 (this phase) | Fine-grained test control without scenario explosion |
| Sentinel sample in holders | Readiness grace window (IsReady) | Phase 60 | Fresh tenant must be primed before evaluation begins |
| Stale data = no commands | Stale data = tier=1 skip to tier=4 | Phase 59 (Quick-076) | Stale→Commands is now an explicit result |

**Key constraint from Phase 60:** Every scenario that applies a new tenant fixture MUST include a priming
step (sleep 8s after fixture load) to get past the 6-second readiness grace window.

## Open Questions

1. **SnapshotJob IntervalSeconds=1 in E2E cluster**
   - What we know: CONTEXT says "SnapshotJob interval: 1 second". Current snmp-collector-config.yaml
     does NOT include a SnapshotJob.IntervalSeconds override (defaults to 15s).
   - What's unclear: Does the E2E cluster already have a local override? Or is this something this
     phase must add to snmp-collector-config.yaml?
   - Recommendation: The planner should add a task to verify/add the SnapshotJob.IntervalSeconds=1
     override to snmp-collector-config.yaml. With 15s interval, tests will be slow; with 1s they run
     fast but the grace window math (6s) changes meaning.

2. **Metric name naming for T1 in the new 4-tenant fixture**
   - What we know: T1 currently uses e2e_port_utilization (.999.4.1), e2e_channel_state (.999.4.2),
     e2e_bypass_status (.999.4.3). The new per-tenant approach assigns e2e_eval_T1 / e2e_res1_T1 /
     e2e_res2_T1 to those same OIDs.
   - What's unclear: Should we reuse the existing metric names (for backward compat with STS scenarios)
     or introduce new T1-specific names?
   - Recommendation: Use new names (e2e_eval_T1 etc.) for the 4-tenant fixture. The existing STS/MTS
     fixtures reference the old names and are unaffected. The OID map must have both old and new names
     pointing to the same OIDs — but the oid map maps OID→MetricName (1:1), so you cannot have two
     names for the same OID. Solution: the new fixture uses the existing metric names for T1
     (e2e_port_utilization etc.) rather than introducing new names. Then no new OID map entries are
     needed for T1.

3. **Stale state via per-OID endpoint**
   - What we know: The stale sentinel (STALE object) is currently per-scenario-level only.
     `_oid_overrides` stores integers, but stale needs NoSuchInstance.
   - What's unclear: How to set individual OIDs to stale via the per-OID endpoint.
   - Recommendation: Support a special value sentinel. One approach: `POST /oid/{oid}/stale` as a
     separate endpoint, or `POST /oid/{oid}/-1` treated as stale. The stale state test (SNS-02) only
     needs T1 to be stale while T2-T4 are normal — achievable by setting T1 OIDs to stale then
     resetting T2-T4 OIDs to valid values. Planner should choose the approach.

## Sources

### Primary (HIGH confidence)
- Direct source reading: `src/SnmpCollector/Jobs/SnapshotJob.cs` — verified log messages, tier names, TierResult enum, IsReady logic
- Direct source reading: `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — verified IsReady, ReadinessGrace, ConstructedAt
- Direct source reading: `simulators/e2e-sim/e2e_simulator.py` — verified HTTP endpoint structure, DynamicInstance pattern, scenario dict, STALE sentinel
- Direct source reading: `tests/e2e/lib/*.sh` — verified all helper function signatures and behavior
- Direct source reading: `tests/e2e/scenarios/29-40.sh` — verified patterns for priming, polling, negative assertions
- Direct source reading: `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` — verified existing OID entries
- Direct source reading: `deploy/k8s/snmp-collector/snmp-collector-config.yaml` — verified LogLevel=Debug, confirmed no SnapshotJob override

### Secondary (MEDIUM confidence)
- Phase 60 CONTEXT.md — confirmed readiness grace formula and IsReady semantics
- Phase 61 CONTEXT.md — locked decisions for OID assignments, tenant structure, thresholds

## Metadata

**Confidence breakdown:**
- Simulator extension (OID registration, HTTP endpoint): HIGH — pattern is clear from existing code
- Tenant fixture format: HIGH — directly modeled on existing fixtures
- Scenario script structure: HIGH — directly modeled on existing scenarios
- Timing (priming, readiness grace): HIGH — formula verified in MetricSlotHolder.cs
- SnapshotJob interval=1s E2E config: MEDIUM — stated in CONTEXT but not found in current k8s yaml
- Stale via per-OID endpoint: LOW — implementation approach not fully specified

**Research date:** 2026-03-19
**Valid until:** 2026-04-18 (stable infrastructure, 30-day window appropriate)
