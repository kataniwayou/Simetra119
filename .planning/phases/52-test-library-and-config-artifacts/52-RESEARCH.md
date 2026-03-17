# Phase 52: Test Library and Config Artifacts - Research

**Researched:** 2026-03-17
**Domain:** Bash E2E test infrastructure, YAML config authoring, simulator scenario wiring
**Confidence:** HIGH

## Summary

Phase 52 is a pure authoring phase: no new C# code, no new simulator code. Everything is
configuration files (YAML/JSON), bash library additions, and new scenario scripts. The
codebase is fully built and understood from reading existing files directly.

The existing E2E test framework (tests/e2e/) is mature. All patterns — port-forward
lifecycle, configmap save/restore, pod-log multi-pod assertions, Prometheus counter polling
— are already established. Phase 52 adds a sim control library (sim.sh), 7 new tenant
fixture YAML files, 6 new OID map entries, 1 new command map entry, and extends the devices
ConfigMap with a new poll group. New scenario scripts (29–35+) source the sim library to
drive e2e_simulator scenarios and assert tier debug log lines and command counters.

**Primary recommendation:** Follow existing patterns exactly. sim.sh is a thin wrapper
around `curl -sf -X POST http://localhost:8080/scenario/{name}`. All assertions use
existing record_pass/record_fail and query_counter/poll_until functions. No new assertion
primitives are needed.

---

## Standard Stack

### Core (already in repo — no new installs)
| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| bash | system | test scripting | established project pattern |
| kubectl | cluster version | ConfigMap apply/restore, pod logs | established |
| curl + jq | system | Prometheus HTTP API queries | established |
| aiohttp | 3.13.3 (in requirements.txt) | simulator HTTP endpoint | Phase 51 complete |

### Config Formats
| Format | Where Used | Notes |
|--------|-----------|-------|
| JSON inside YAML ConfigMap | simetra-oid-metric-map.yaml, simetra-oid-command-map.yaml, simetra-devices.yaml | Must match exact field names from C# model binding |
| JSON array (tenants.json) | tenant fixture YAML files | Sourced from TenantVectorOptions.Tenants deserialization |

**Installation:** Nothing new to install. Port 8080 already exposed in e2e-sim-deployment.yaml Service.

---

## Architecture Patterns

### Recommended Project Structure

```
tests/e2e/
├── lib/
│   ├── common.sh       (existing)
│   ├── kubectl.sh      (existing)
│   ├── prometheus.sh   (existing)
│   ├── report.sh       (existing)
│   └── sim.sh          (NEW — simulator HTTP control)
├── fixtures/
│   ├── ...existing yaml...
│   ├── tenant-cfg01-single.yaml        (NEW)
│   ├── tenant-cfg02-two-same-prio.yaml (NEW)
│   ├── tenant-cfg03-two-diff-prio.yaml (NEW)
│   ├── tenant-cfg04-aggregate.yaml     (NEW)
│   └── ...other variants per CFG-05/06/07...
└── scenarios/
    ├── 01-28-...existing...
    ├── 29-snapshot-single-tenant-command.sh   (NEW)
    ├── 30-snapshot-resolved-gate.sh           (NEW)
    ├── 31-snapshot-two-tenants-same-prio.sh   (NEW)
    ├── 32-snapshot-two-tenants-diff-prio.sh   (NEW)
    ├── 33-snapshot-aggregate-evaluate.sh      (NEW)
    ├── 34-snapshot-suppression.sh             (NEW)
    └── 35-snapshot-stale-blocks.sh            (NEW)

deploy/k8s/snmp-collector/
├── simetra-oid-metric-map.yaml   (EXTEND — add 6 entries)
├── simetra-oid-command-map.yaml  (EXTEND — add 1 entry)
└── simetra-devices.yaml          (EXTEND — add E2E-SIM .4.x poll group)
```

### Pattern 1: sim.sh — Simulator HTTP Control Library

**What:** A sourced bash library. Provides `sim_set_scenario` and `poll_until_log`.

**When to use:** Every new E2E scenario that drives the e2e_simulator.

**Implementation:**
```bash
# Source: established pattern from kubectl.sh / prometheus.sh
SIM_URL="http://localhost:8080"

sim_set_scenario() {
    local name="$1"
    log_info "Setting simulator scenario: ${name}"
    local response
    response=$(curl -sf -X POST "${SIM_URL}/scenario/${name}" 2>&1) || {
        log_error "Failed to set scenario ${name}: ${response}"
        return 1
    }
    log_info "Scenario active: ${name}"
}

poll_until_log() {
    # Usage: poll_until_log <timeout_s> <interval_s> <grep_pattern> [since_seconds]
    local timeout="$1"
    local interval="$2"
    local pattern="$3"
    local since="${4:-60}"

    local deadline
    deadline=$(( $(date +%s) + timeout ))
    local PODS
    PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
        -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true

    while [ "$(date +%s)" -lt "$deadline" ]; do
        for POD in $PODS; do
            local logs
            logs=$(kubectl logs "$POD" -n simetra --since="${since}s" 2>/dev/null) || true
            if echo "$logs" | grep "${pattern}" > /dev/null 2>&1; then
                return 0
            fi
        done
        sleep "$interval"
    done
    return 1
}
```

**Key details:**
- Port-forward for 8080 must be started alongside the Prometheus port-forward in run-all.sh.
- `sim_set_scenario default` at the START of every snapshot scenario (belt-and-suspenders reset).
- `sim_set_scenario <specific>` immediately after the reset.
- `poll_until_log` searches ALL pods (any match = pass), mirrors the pattern in scenario 28.

### Pattern 2: New Snapshot Scenario Structure

```bash
# At start of each snapshot scenario
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# 1. Reset scenario to default
sim_set_scenario default

# 2. Save current tenant ConfigMap
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.snap-tenants-${SCENARIO_ID}.yaml" || true

# 3. Apply tenant fixture
kubectl apply -f "$FIXTURES_DIR/tenant-cfg01-single.yaml"

# 4. Wait for watcher hot-reload (15s — established pattern from scenario 28)
log_info "Waiting 15s for tenant watcher reload..."
sleep 15

# 5. Set specific scenario
sim_set_scenario threshold_breach

# 6. Wait for SnapshotJob cycle (SnapshotJob interval + poll interval)
sleep 30

# 7. Assert log line OR Prometheus counter
# ...

# 8. Cleanup: restore tenant ConfigMap, reset scenario
sim_set_scenario default
restore_configmap "$FIXTURES_DIR/.snap-tenants-${SCENARIO_ID}.yaml" || true
```

### Pattern 3: Tenant Config Fixture YAML Shape

Follows exact shape of existing simetra-tenants.yaml. Tenant JSON structure:
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenants
  namespace: simetra
data:
  tenants.json: |
    [
      {
        "Priority": 1,
        "Metrics": [
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_port_utilization",
            "TimeSeriesSize": 3,
            "Role": "Evaluate",
            "Threshold": { "Min": null, "Max": 80.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_channel_state",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0, "Max": 1.0 }
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
        ],
        "SuppressionWindowSeconds": 10
      }
    ]
```

### Pattern 4: OID Map Entry Shape

```json
{ "Oid": "1.3.6.1.4.1.47477.999.4.1.0", "MetricName": "e2e_port_utilization" }
```

Note: OIDs in the map file MUST include the trailing `.0` instance suffix (confirmed from
all 107 existing entries in simetra-oid-metric-map.yaml).

### Pattern 5: Command Map Entry Shape

```json
{ "Oid": "1.3.6.1.4.1.47477.999.4.4.0", "CommandName": "e2e_set_bypass" }
```

### Pattern 6: Device Config Poll Group Shape

New poll group for E2E-SIM targeting the .4.x OIDs:
```json
{
  "IntervalSeconds": 10,
  "GraceMultiplier": 2.0,
  "MetricNames": [
    "e2e_port_utilization",
    "e2e_channel_state",
    "e2e_bypass_status",
    "e2e_command_response",
    "e2e_agg_source_a",
    "e2e_agg_source_b"
  ]
}
```
This is added as a second Polls entry in the existing E2E-SIM device block (alongside the
existing .999.1.x poll group for the 7 existing E2E-SIM metrics).

### Anti-Patterns to Avoid

- **Separate ConfigMap for E2E OIDs:** Decided context locks these into the existing
  `simetra-oid-metric-map` ConfigMap. Separate ConfigMap causes watcher complication.
- **Poll group for each OID individually:** Use one poll group for all 6 new OIDs.
  Consistent with how OBP and NPB groups work.
- **Shebangs in scenario files:** Existing scenarios have no `#!/usr/bin/env bash` header.
  They are sourced by run-all.sh, not executed directly. New scenarios must follow suit.
- **Using `grep -q` in pod log assertions:** scenario 28 uses `grep > /dev/null 2>&1`
  specifically to avoid SIGPIPE under `set -euo pipefail`. All new log assertions must
  use the same pattern.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Pod log scanning | Custom kubectl log parser | `kubectl logs $POD -n simetra --since=Xs` piped to grep | Established, handles multi-pod |
| Prometheus polling | Custom wait loop | `poll_until` from prometheus.sh | Already handles timeout/interval |
| ConfigMap save/restore | Custom kubectl wrapper | `save_configmap` / `restore_configmap` from kubectl.sh | Already handles resource version stripping |
| Scenario reset | Implicit state assumptions | Explicit `sim_set_scenario default` at scenario start | Belt-and-suspenders per CONTEXT.md decision |
| Suppression window management | Complex timing | Use short `SuppressionWindowSeconds: 10` in fixture | Allows suppression scenario to test within 30s |

**Key insight:** Every needed primitive already exists. sim.sh is the only new library file.

---

## Common Pitfalls

### Pitfall 1: Tier 2 blocks Tier 3 (default scenario design)

**What goes wrong:** The "default" scenario sets all .4.x OIDs to 0. If resolved metrics
(`e2e_channel_state`, `e2e_bypass_status`) have thresholds using equality (Min==Max==0),
they are ALL violated by default. `AreAllResolvedViolated` returns true → ConfirmedBad →
no Tier 3 evaluation, no commands ever fire. This is the intended idle behavior per
CONTEXT.md.

**Why it matters for test design:** To test Tier 3/Tier 4 (evaluate check and command
dispatch), scenarios MUST use a scenario that clears the resolved metrics. The
`threshold_clear` or a new scenario must set resolved OIDs to values that are NOT violated
(e.g., e2e_channel_state = 1 when threshold is Min==Max==1 means "primary" = in range if
you use Max only).

**How to avoid:** Design thresholds carefully:
- Resolved metric `e2e_channel_state`: Threshold `{ "Max": 0.5 }` means violated when
  value > 0.5. Default value is 0 (not violated). Only violated when simulator returns 1+.
  OR use equality semantics differently. See threshold recommendation below.
- Evaluate metric `e2e_port_utilization`: Threshold `{ "Max": 80.0 }` means violated when
  > 80. Simulator `threshold_breach` scenario sets .4.1 = 90 (violated). Default = 0 (not
  violated).

**Recommended threshold values (Claude's discretion):**

For resolved metrics to be violated-by-default (idle = ConfirmedBad as per CONTEXT.md):
- `e2e_channel_state` (`.4.2`): Threshold `{ "Min": 1.0, "Max": 1.0 }` = equality check.
  Value 0 is NOT 1.0 → NOT violated. Wait — CONTEXT.md says "resolved metrics are VIOLATED
  by default". So default value 0 must be violated.
  Use: `{ "Min": 1.0, "Max": 1.0 }` means violated if value == 1.0. Default = 0 → NOT
  violated. That's the opposite of what CONTEXT.md says.

  **Re-read:** `AreAllResolvedViolated` returns true → ConfirmedBad. Violated = bad state.
  We want default (value=0) to be "violated" (bad). So threshold must make value=0 bad.
  Use: `{ "Min": 1.0, "Max": 1.0 }` = equality, violated when value == 1.0. Default 0 ≠
  1.0 → NOT violated → goes to Tier 3. That is wrong.

  Use: `{ "Min": 1.0 }` = violated when value < 1.0. Default 0 < 1 → VIOLATED. Correct.
  In-range (primary channel): set simulator to value ≥ 1. Use `threshold_clear` scenario
  that sets .4.2 = 2 (or any value ≥ 1).

- `e2e_bypass_status` (`.4.3`): Same pattern. `{ "Min": 1.0 }`. Default 0 < 1 → VIOLATED.
  Clear: simulator `bypass_active` scenario sets .4.3 = 1.

For evaluate metric to be NOT violated by default (avoid false command triggers):
- `e2e_port_utilization` (`.4.1`): `{ "Max": 80.0 }`. Default 0 is NOT > 80 → not violated.
  Breach: `threshold_breach` scenario sets .4.1 = 90 > 80 → violated.

For aggregate evaluate:
- `e2e_agg_source_a` + `e2e_agg_source_b`: aggregated to `e2e_total_util`.
  Threshold on aggregate: `{ "Max": 80.0 }`. Default sum = 0 (not violated).

**Warning signs:** If a snapshot scenario never fires a command, check whether Tier 2 is
blocking. Add `tier=2` log assertion first to confirm the gate state.

### Pitfall 2: Suppression window prevents command observation

**What goes wrong:** SnapshotJob runs every N seconds. If a command was sent in a previous
test run (or earlier in the same scenario), the suppression cache blocks subsequent
dispatches within `SuppressionWindowSeconds`. Default is 60 seconds.

**How to avoid:** Fixture YAML for E2E tenants should set `"SuppressionWindowSeconds": 10`.
This allows a suppression scenario to force-suppress and then verify behavior within a
30s polling window. Normal command scenarios should set SuppressionWindowSeconds short
enough that a scenario can observe at least one command, then one suppression.

**Warning signs:** `snmp_command_suppressed_total` incrementing when you expected
`snmp_command_sent_total` to increment.

### Pitfall 3: Port-forward to 8080 not started before sim_set_scenario calls

**What goes wrong:** run-all.sh only starts a Prometheus port-forward. Calls to
`sim_set_scenario` will fail with `curl: (7) Failed to connect`.

**How to avoid:** Add `start_port_forward e2e-simulator 8080 8080` to run-all.sh alongside
the Prometheus port-forward. Note: this is a TCP port-forward which kubectl supports
cleanly (unlike UDP/161).

**Warning signs:** Scenario fails immediately at `sim_set_scenario default` with curl error.

### Pitfall 4: Tenant config hot-reload timing

**What goes wrong:** A scenario applies a new tenant ConfigMap and immediately calls
`sim_set_scenario`, but the watcher hasn't loaded the new config yet. Tier 3/4 still
operates on old tenant definitions.

**How to avoid:** After `kubectl apply` of a tenant ConfigMap, sleep 15s (same timeout
used in scenario 28 for watcher detection). The watcher detects file changes via inotify
within seconds but processing takes a moment.

**Warning signs:** Log shows old tenant count or missing tenant-{N} entries.

### Pitfall 5: OID .0 suffix missing in ConfigMap entries

**What goes wrong:** All 107 existing entries in simetra-oid-metric-map.yaml include the
`.0` scalar instance suffix (e.g., `"1.3.6.1.4.1.47477.999.1.1.0"`). New entries without
the `.0` will never match polled OIDs, silently failing metric routing.

**How to avoid:** All 6 new OID entries must include the `.0` suffix:
- `1.3.6.1.4.1.47477.999.4.1.0` → e2e_port_utilization
- `1.3.6.1.4.1.47477.999.4.2.0` → e2e_channel_state
- `1.3.6.1.4.1.47477.999.4.3.0` → e2e_bypass_status
- `1.3.6.1.4.1.47477.999.4.4.0` → e2e_command_response
- `1.3.6.1.4.1.47477.999.4.5.0` → e2e_agg_source_a
- `1.3.6.1.4.1.47477.999.4.6.0` → e2e_agg_source_b

### Pitfall 6: CommandWorkerService is leader-gated

**What goes wrong:** `CommandWorkerService.ExecuteCommandAsync` checks `_leaderElection.IsLeader`
before executing SET. In a 3-pod deployment, only the leader sends commands. Non-leaders
silently skip. The Prometheus `snmp_command_sent_total` counter only increments on the leader.

**How to avoid:** Use `sum(snmp_command_sent_total)` across all pods in Prometheus queries
(consistent with the `query_counter` function in prometheus.sh which already uses `sum()`).
Do NOT assert on per-pod counters.

**Warning signs:** Counter delta = 0 even though tier-4 log lines appear on a non-leader pod.

### Pitfall 7: snmp.command.sent metric name → Prometheus underscore conversion

**What goes wrong:** .NET instruments use dots (e.g., `snmp.command.sent`). OpenTelemetry
Prometheus exporter converts dots to underscores. The actual Prometheus metric name is
`snmp_command_sent_total` (with `_total` suffix for counters).

**Confirmed metric names for assertions:**
- `snmp_command_sent_total` (label: `device_name="E2E-SIM"`)
- `snmp_command_suppressed_total` (label: `tenant_id` from PipelineMetricService)
- `snmp_command_failed_total`

**Note:** `IncrementCommandSent` takes `device.Name` (the device name from registry, which
is derived from CommunityString: `Simetra.E2E-SIM` → device name `E2E-SIM`). The
`IncrementCommandSuppressed` takes `tenant.Id`. Suppressed counter uses tenant label, not
device label.

---

## Code Examples

### sim.sh — Complete Library

```bash
#!/usr/bin/env bash
# sim.sh -- E2E simulator HTTP control utilities
# Requires port-forward to e2e-simulator:8080 to be active

SIM_URL="http://localhost:8080"

sim_set_scenario() {
    local name="$1"
    log_info "Setting simulator scenario: ${name}"
    local http_code
    http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
        -X POST "${SIM_URL}/scenario/${name}" 2>/dev/null) || {
        log_error "curl failed setting scenario ${name}"
        return 1
    }
    if [ "$http_code" = "200" ]; then
        log_info "Scenario active: ${name}"
    else
        log_error "Unexpected HTTP ${http_code} setting scenario ${name}"
        return 1
    fi
}

poll_until_log() {
    local timeout="$1"
    local interval="$2"
    local pattern="$3"
    local since="${4:-60}"

    local deadline
    deadline=$(( $(date +%s) + timeout ))

    local PODS
    PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
        -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true

    while [ "$(date +%s)" -lt "$deadline" ]; do
        for POD in $PODS; do
            local logs
            logs=$(kubectl logs "$POD" -n simetra --since="${since}s" 2>/dev/null) || true
            if echo "$logs" | grep "${pattern}" > /dev/null 2>&1; then
                return 0
            fi
        done
        sleep "$interval"
    done
    return 1
}
```

### Snapshot scenario — command dispatch assertion

```bash
# Scenario 29: SnapshotJob dispatches command for single-tenant CFG-04
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
SCENARIO_NAME="SnapshotJob dispatches e2e_set_bypass command (CFG-04 single tenant)"

# Reset
sim_set_scenario default
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.snap-tenants-29.yaml" || true

# Apply single-tenant fixture
kubectl apply -f "$FIXTURES_DIR/tenant-cfg01-single.yaml"
sleep 15  # watcher reload

# Put simulator in threshold_breach (evaluate violated) AND resolved not-all-violated
# (default scenario: resolved OIDs = 0, with threshold Min:1.0, these are ALL violated →
# goes to ConfirmedBad. We need a scenario that clears resolved.)
# Use a combined scenario: threshold_breach sets .4.1=90, but .4.2/.4.3 are still 0 (violated).
# Need to design a "command_trigger" scenario that sets .4.1=90, .4.2=2, .4.3=2.
sim_set_scenario threshold_breach
# (threshold_breach only sets .4.1=90, leaves .4.2=0 and .4.3=0 → still ConfirmedBad)
# Actually need the simulator to have a scenario that clears resolved AND breaches evaluate.
# SEE: Open Questions #1 below.

BEFORE=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
poll_until 60 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE" || true
AFTER=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
DELTA=$((AFTER - BEFORE))

assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" \
    "$(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"

# Cleanup
sim_set_scenario default
restore_configmap "$FIXTURES_DIR/.snap-tenants-29.yaml" || true
```

### OID metric map additions (in simetra-oid-metric-map.yaml)

```json
{ "Oid": "1.3.6.1.4.1.47477.999.4.1.0", "MetricName": "e2e_port_utilization" },
{ "Oid": "1.3.6.1.4.1.47477.999.4.2.0", "MetricName": "e2e_channel_state" },
{ "Oid": "1.3.6.1.4.1.47477.999.4.3.0", "MetricName": "e2e_bypass_status" },
{ "Oid": "1.3.6.1.4.1.47477.999.4.4.0", "MetricName": "e2e_command_response" },
{ "Oid": "1.3.6.1.4.1.47477.999.4.5.0", "MetricName": "e2e_agg_source_a" },
{ "Oid": "1.3.6.1.4.1.47477.999.4.6.0", "MetricName": "e2e_agg_source_b" }
```

### Command map addition (in simetra-oid-command-map.yaml)

```json
{ "Oid": "1.3.6.1.4.1.47477.999.4.4.0", "CommandName": "e2e_set_bypass" }
```

### Tier debug log line patterns (from SnapshotJob.cs)

```
# Tier 1 stale:
"tier=1 stale"

# Tier 2 ConfirmedBad:
"tier=2 — all resolved violated, device confirmed bad, no commands"

# Tier 2 → Tier 3 (resolved gate passed):
"tier=2 — resolved not all violated, proceeding to evaluate check"

# Tier 3 healthy:
"tier=3 — not all evaluate metrics violated, no action"

# Tier 4 command enqueued:
"tier=4 — commands enqueued"
```

All log lines are at LogDebug level. Ensure the deployment's log level is Debug for
SnapshotJob (or at minimum for the relevant namespace). Check existing log output in
scenario 28 for whether debug logs are visible — that scenario asserts debug-level watcher
lines, suggesting debug is enabled.

---

## State of the Art

| Old Approach | Current Approach | Notes |
|--------------|------------------|-------|
| Static tenant config only | Hot-reload via TenantVectorWatcherService | ConfigMap changes detected within ~5s |
| No simulator control | HTTP endpoint POST /scenario/{name} on port 8080 | Phase 51 complete |
| No command dispatch | CommandWorkerService drains channel, leader-gated | Phase 47 complete |

---

## Open Questions

### 1. Missing simulator scenario: clear-resolved + breach-evaluate

**What we know:** The 5 existing simulator scenarios are:
- `default`: all .4.x = 0
- `threshold_breach`: .4.1 = 90 (evaluate only)
- `threshold_clear`: .4.1 = 5
- `bypass_active`: .4.3 = 1
- `stale`: .4.1 and .4.2 return noSuchInstance

**What's unclear:** No scenario simultaneously sets evaluate = breached AND resolved = not
violated. `threshold_breach` sets .4.1 = 90 (evaluate violated) but leaves .4.2 = 0 and
.4.3 = 0. With resolved threshold `{ "Min": 1.0 }`, both resolved metrics have value 0 < 1
→ both violated → Tier 2 fires ConfirmedBad → no Tier 3/4 evaluation.

**Recommendation:** Add a 6th simulator scenario to e2e_simulator.py: `command_trigger`
that sets `.4.1 = 90` (evaluate breached), `.4.2 = 2` (resolved clear), `.4.3 = 2`
(resolved clear). This is a minimal simulator change (1 entry in SCENARIOS dict). The
planner should decide: is this in scope for Phase 52 or Phase 53 (scenario scripts)?
Given Phase 52 creates the simulator artifacts, it likely belongs here.

### 2. Log level confirmation for tier debug lines

**What we know:** SnapshotJob tier log lines are at LogDebug. Scenario 28 observes
watcher log lines which appear to be LogInformation ("Tenant vector reload complete").
Tier lines are explicitly `_logger.LogDebug(...)`.

**What's unclear:** Whether the K8s deployment has Debug log level enabled for
SnapshotJob. If only Information/Warning, tier assertions via pod logs won't work.

**Recommendation:** Verify by checking the deployment's appsettings or configmap for
`Logging:LogLevel:SnmpCollector.Jobs.SnapshotJob`. If not at Debug, scenario log
assertions must target the Tier 4 Information log instead:
`"tier=4 — commands enqueued"` which is at LogInformation (confirmed in SnapshotJob.cs
line 190: `_logger.LogInformation(...)`).

### 3. Aggregate metric for CFG-07

**What we know:** e2e_agg_source_a and e2e_agg_source_b need an aggregate poll group
to produce an aggregated metric. Device config requires an `AggregatedMetricName` and
`Aggregator` field.

**What's unclear:** The aggregate metric name. Suggested: `e2e_total_util` (sum of a+b).
The aggregate poll group is in the E2E-SIM device block, separate from the individual OID
poll group.

**Recommendation:** Add a second aggregate poll group:
```json
{
  "IntervalSeconds": 10,
  "GraceMultiplier": 2.0,
  "MetricNames": ["e2e_agg_source_a", "e2e_agg_source_b"],
  "AggregatedMetricName": "e2e_total_util",
  "Aggregator": "sum"
}
```
CFG-07 tenant uses `e2e_total_util` as the Evaluate metric with a threshold.

---

## Sources

### Primary (HIGH confidence)
- Direct codebase reading: `tests/e2e/lib/*.sh` — all library patterns confirmed
- Direct codebase reading: `tests/e2e/scenarios/28-tenantvector-routing.sh` — gold standard for complex scenario with configmap save/restore
- Direct codebase reading: `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` — OID entry format with .0 suffix confirmed for all 107 entries
- Direct codebase reading: `deploy/k8s/snmp-collector/simetra-oid-command-map.yaml` — command map entry format confirmed
- Direct codebase reading: `deploy/k8s/snmp-collector/simetra-devices.yaml` — device poll group format including aggregate pattern
- Direct codebase reading: `deploy/k8s/snmp-collector/simetra-tenants.yaml` — tenant JSON structure
- Direct codebase reading: `src/SnmpCollector/Jobs/SnapshotJob.cs` — 4-tier logic, exact log messages, TierResult enum
- Direct codebase reading: `src/SnmpCollector/Services/CommandWorkerService.cs` — leader gate, IncrementCommandSent uses device.Name
- Direct codebase reading: `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — all metric names confirmed
- Direct codebase reading: `simulators/e2e-sim/e2e_simulator.py` — 5 existing scenarios, OID subtree layout, HTTP API
- Direct codebase reading: `deploy/k8s/simulators/e2e-sim-deployment.yaml` — port 8080 confirmed in Service

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all from direct codebase reading
- Architecture patterns: HIGH — all from direct codebase reading, no inference needed
- Pitfalls: HIGH — threshold logic from SnapshotJob.cs source, leader gate from CommandWorkerService.cs source
- Open questions: the missing simulator scenario (Q1) is a genuine gap discovered by tracing scenario logic

**Research date:** 2026-03-17
**Valid until:** Stable — changes only if SnapshotJob tier logic changes or simulator scenarios are modified
