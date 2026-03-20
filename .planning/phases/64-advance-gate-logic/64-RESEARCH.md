# Phase 64: Advance Gate Logic - Research

**Researched:** 2026-03-20
**Domain:** E2E PSS Stage 3 -- 4-tenant advance gate verification (bash scenarios, K8s fixtures)
**Confidence:** HIGH (all findings from direct source code inspection of existing codebase)

---

## Summary

Phase 64 builds Stage 3 of the PSS E2E test suite. It extends the established pattern from Stage 1 (single-tenant, scenarios 53-58) and Stage 2 (two-tenant independence, scenarios 59-61) to a 4-tenant 2-group fixture that specifically exercises the advance gate. The gate logic is already correct in `SnapshotJob.cs` -- this phase produces the verification evidence for it.

The 4-tenant fixture (`tenant-cfg05-four-tenant-snapshot.yaml`) already exists and has the exact group structure required: G1 (T1+T2, Priority=1) gates G2 (T3+T4, Priority=2). The OID map already has .999.4.x through .999.7.x mapped. The SNS gate scenarios (46-52) in the existing suite use this exact fixture with this exact pattern, giving complete reference implementations for all assertion types needed by PSS-14 through PSS-20.

The PSS Stage 3 work is: (1) a new 4-tenant PSS fixture (tenant-cfg08-pss-four-tenant.yaml) with PSS naming conventions and independent OID namespaces, (2) 7 scenario scripts (62-68), (3) a run-stage3.sh runner with FAIL_COUNT gating, and (4) updates to report.sh and run-all.sh for cross-stage summary. The fixture uses tenant names `e2e-pss-g1-t1`, `e2e-pss-g1-t2`, `e2e-pss-g2-t3`, `e2e-pss-g2-t4` matching the PSS naming convention.

**Primary recommendation:** Follow the SNS gate scenarios (46-52) as direct templates. The gate-pass pattern (poll G2 tier log) and gate-block pattern (sleep 10s + grep absence in --since=15s window) are already proven and directly reusable.

---

## Standard Stack

This phase uses no new libraries. All tooling is the existing E2E infrastructure.

### Core Infrastructure

| Component | Location | Purpose |
|-----------|----------|---------|
| `tenant-cfg05-four-tenant-snapshot.yaml` | `tests/e2e/fixtures/` | Reference 4-tenant fixture (SNS scenarios) |
| `sim.sh` | `tests/e2e/lib/` | `sim_set_oid`, `reset_oid_overrides`, `poll_until_log` |
| `prometheus.sh` | `tests/e2e/lib/` | `snapshot_counter`, `poll_until` |
| `kubectl.sh` | `tests/e2e/lib/` | `save_configmap`, `restore_configmap`, `check_pods_ready` |
| `common.sh` | `tests/e2e/lib/` | `record_pass`, `record_fail`, `FAIL_COUNT` global |
| `report.sh` | `tests/e2e/lib/` | `generate_report`, `_REPORT_CATEGORIES` array |
| `run-stage2.sh` | `tests/e2e/` | Stage 2 runner -- direct template for Stage 3 |
| SNS-A1..A3 scenarios | `tests/e2e/scenarios/46-48-*.sh` | Gate-pass reference implementations |
| SNS-B1..B4 scenarios | `tests/e2e/scenarios/49-52-*.sh` | Gate-block reference implementations |

### OID Map (Already Registered)

All OIDs required for the 4-tenant fixture are already in `simetra-oid-metric-map`:

| OID Suffix | MetricName | OID Full |
|------------|------------|----------|
| `4.1` | `e2e_port_utilization` | `1.3.6.1.4.1.47477.999.4.1.0` |
| `4.2` | `e2e_channel_state` | `1.3.6.1.4.1.47477.999.4.2.0` |
| `4.3` | `e2e_bypass_status` | `1.3.6.1.4.1.47477.999.4.3.0` |
| `5.1` | `e2e_eval_T2` | `1.3.6.1.4.1.47477.999.5.1.0` |
| `5.2` | `e2e_res1_T2` | `1.3.6.1.4.1.47477.999.5.2.0` |
| `5.3` | `e2e_res2_T2` | `1.3.6.1.4.1.47477.999.5.3.0` |
| `6.1` | `e2e_eval_T3` | `1.3.6.1.4.1.47477.999.6.1.0` |
| `6.2` | `e2e_res1_T3` | `1.3.6.1.4.1.47477.999.6.2.0` |
| `6.3` | `e2e_res2_T3` | `1.3.6.1.4.1.47477.999.6.3.0` |
| `7.1` | `e2e_eval_T4` | `1.3.6.1.4.1.47477.999.7.1.0` |
| `7.2` | `e2e_res1_T4` | `1.3.6.1.4.1.47477.999.7.2.0` |
| `7.3` | `e2e_res2_T4` | `1.3.6.1.4.1.47477.999.7.3.0` |

No OID map changes are needed for Phase 64.

---

## Architecture Patterns

### Fixture Design: New 4-Tenant PSS Fixture

The existing `tenant-cfg05-four-tenant-snapshot.yaml` uses tenant names `e2e-tenant-G1-T1`, `e2e-tenant-G1-T2`, `e2e-tenant-G2-T3`, `e2e-tenant-G2-T4` -- the SNS naming convention. Per the CONTEXT.md decisions, PSS Stage 3 must follow the PSS naming convention. The new fixture must be a separate file with PSS-appropriate tenant names.

**New fixture file:** `tests/e2e/fixtures/tenant-cfg08-pss-four-tenant.yaml`

**Tenant layout:**

| Tenant Name | Priority | OIDs | Role |
|------------|----------|------|------|
| `e2e-pss-g1-t1` | 1 | 4.x (`e2e_port_utilization`, `e2e_channel_state`, `e2e_bypass_status`) | G1 |
| `e2e-pss-g1-t2` | 1 | 5.x (`e2e_eval_T2`, `e2e_res1_T2`, `e2e_res2_T2`) | G1 |
| `e2e-pss-g2-t3` | 2 | 6.x (`e2e_eval_T3`, `e2e_res1_T3`, `e2e_res2_T3`) | G2 |
| `e2e-pss-g2-t4` | 2 | 7.x (`e2e_eval_T4`, `e2e_res1_T4`, `e2e_res2_T4`) | G2 |

Each tenant uses the standard metric structure: 1 Evaluate (Min:10, TimeSeriesSize:3, GraceMultiplier:2.0) + 2 Resolved (Min:1). All tenants have 1 Command (`e2e_set_bypass`). SuppressionWindowSeconds=10 for all tenants.

**T1 is special:** Uses .999.4.x OIDs (`e2e_port_utilization`=4.1, `e2e_channel_state`=4.2, `e2e_bypass_status`=4.3). The Evaluate metric for T1 is `e2e_port_utilization` (OID 4.1) and Resolved metrics are `e2e_channel_state` (4.2) and `e2e_bypass_status` (4.3). This is consistent with how the SNS fixture (`tenant-cfg05-four-tenant-snapshot.yaml`) defines T1.

### OID Control Map (Priming Reference)

The sim_set_oid suffix is the short OID (without the prefix), as used in all existing SNS and PSS scenarios:

```
Healthy state (all in-range):
  sim_set_oid "4.1" "10"   # T1 eval >= Min:10
  sim_set_oid "4.2" "1"    # T1 res1 >= Min:1
  sim_set_oid "4.3" "1"    # T1 res2 >= Min:1
  sim_set_oid "5.1" "10"   # T2 eval
  sim_set_oid "5.2" "1"    # T2 res1
  sim_set_oid "5.3" "1"    # T2 res2
  sim_set_oid "6.1" "10"   # T3 eval
  sim_set_oid "6.2" "1"    # T3 res1
  sim_set_oid "6.3" "1"    # T3 res2
  sim_set_oid "7.1" "10"   # T4 eval
  sim_set_oid "7.2" "1"    # T4 res1
  sim_set_oid "7.3" "1"    # T4 res2

Unresolved (eval violated, resolved in-range):
  sim_set_oid "X.1" "0"    # eval < Min:10 -> Unresolved

Resolved (all resolved violated, eval in-range):
  sim_set_oid "X.2" "0"    # res1 < Min:1 -> violated
  sim_set_oid "X.3" "0"    # res2 < Min:1 -> violated
  # eval stays at 10 (in-range)
```

### Scenario Structure (All 7 PSS Scenarios)

**Scenario numbering:** PSS scenarios continue from 61 (last Stage 2 scenario). Stage 3 scenarios occupy file numbers 62-68.

| File # | Scenario ID | Description | Type |
|--------|------------|-------------|------|
| 62 | PSS-14 | All G1 Resolved -- gate passes, G2 evaluated | pass |
| 63 | PSS-15 | All G1 Healthy -- gate passes, G2 evaluated | pass |
| 64 | PSS-16 | G1 mixed Resolved+Healthy -- gate passes, G2 evaluated | pass |
| 65 | PSS-17 | All G1 Unresolved -- gate blocks, G2 absent | block |
| 66 | PSS-18 | G1 mixed Resolved+Unresolved -- gate blocks, G2 absent | block |
| 67 | PSS-19 | G1 mixed Healthy+Unresolved -- gate blocks, G2 absent | block |
| 68 | PSS-20 | All G1 Not Ready -- gate blocks, G2 absent | block |

### Pattern 1: Gate-Pass Scenario Structure

All 3 pass scenarios follow the same pattern (reference: `46-sns-a1-both-resolved.sh`, `47-sns-a2-both-healthy.sh`, `48-sns-a3-resolved-healthy.sh`):

```bash
# 1. Save ConfigMap
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

# 2. Apply 4-tenant PSS fixture
kubectl apply -f "$FIXTURES_DIR/tenant-cfg08-pss-four-tenant.yaml" > /dev/null 2>&1 || true

# 3. Wait for reload (pattern: poll_until_log 60 5)
poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30

# 4. Prime all 12 OIDs to healthy
sim_set_oid "4.1" "10" ... (all 12 OIDs)
sleep 8  # grace=6s + 2s margin

# 5. Apply scenario-specific state manipulation (e.g., set resolved OIDs to 0 for Resolved state)

# 6. Assert G1 tenants REACHED expected tier (positive assertion for each G1 tenant)
poll_until_log 30 1 "e2e-pss-g1-t1.*tier=X" 15
poll_until_log 30 1 "e2e-pss-g1-t2.*tier=X" 15

# 7. Assert G2 tenants WERE evaluated (positive gate-pass assertion)
# Use tier value from primed state (Healthy=tier=3) or the specific tier set for G2
poll_until_log 30 1 "e2e-pss-g2-t3.*tier=" 15
record_pass/fail "PSS-14X: G2-T3 evaluated (gate passed)"

# 8. Cleanup
reset_oid_overrides
restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml"
```

**Gate-pass G2 assertion:** The CONTEXT.md requires asserting "G2 tenant tier values (e.g., Healthy)" not just log presence. Since G2 tenants are primed to healthy (all in-range OIDs), after gate passes they reach tier=3. The assertion should use `"e2e-pss-g2-t3.*tier=3"` not just `"e2e-pss-g2-t3.*tier="`. This is a stronger proof.

### Pattern 2: Gate-Block Scenario Structure

All 4 block scenarios follow the same pattern (reference: `49-sns-b1-both-unresolved.sh`, `51-sns-b3-resolved-unresolved.sh`, `52-sns-b4-healthy-unresolved.sh`):

```bash
# (Steps 1-5 same as gate-pass, but different state manipulation)

# 6. Assert G1 tenants REACHED expected tier (positive assertion -- avoids false pass from idle system)
# For Unresolved G1 tenants: assert tier=4
poll_until_log 30 1 "e2e-pss-g1-t1.*tier=4" 15
poll_until_log 30 1 "e2e-pss-g1-t2.*tier=4" 15  # if T2 is also Unresolved

# 7. Observation window: wait 10s to accumulate SnapshotJob cycles
sleep 10

# 8. Assert G2 tier logs are ABSENT (negative assertion)
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
G2_FOUND=0
for POD in $PODS; do
    G2_LOGS=$(kubectl logs "$POD" -n simetra --since=15s 2>/dev/null \
        | grep "e2e-pss-g2.*tier=" || echo "") || true
    if [ -n "$G2_LOGS" ]; then
        G2_FOUND=1
        break
    fi
done
if [ "$G2_FOUND" -eq 0 ]; then
    record_pass "PSS-17B: G2 not evaluated (gate blocked)"
else
    record_fail "PSS-17B: G2 not evaluated (gate blocked)"
fi
```

**Dual-proof requirement (from CONTEXT.md):** Gate-block scenarios must also assert G1 WAS evaluated (positive assertion alongside negative G2 check). This avoids a false pass if the entire system is idle (no evaluation happening at all). The positive G1 assertion comes first, the negative G2 check comes second.

**PSS-20 special case (Not Ready):** Do NOT prime G1 OIDs. Do NOT sleep 8s. Assert "not ready" log for G1 tenants within a short timeout (5s, before grace window expires), then assert G2 absence. Reference: `50-sns-b2-both-not-ready.sh`.

### Pattern 3: PSS-20 Not Ready Gate Block

```bash
# Apply fixture (same)
# Prime ONLY G2 OIDs (T3 and T4) -- G1 stays unprimed -> Not Ready
sim_set_oid "6.1" "10" ... (6 OIDs for T3+T4 only)
# DO NOT prime 4.x or 5.x
# DO NOT sleep 8s

# Assert G1 "not ready" before grace expires (short timeout)
poll_until_log 5 1 "e2e-pss-g1-t1.*not ready" 5
poll_until_log 5 1 "e2e-pss-g1-t2.*not ready" 5

# Wait briefly, check G2 absence
sleep 5
# grep G2 with --since=10s (within not-ready window)
```

**CONTEXT.md requirement for PSS-20:** Also verify G1 shows "Not Ready" in logs AND G2 is absent -- confirms blocking reason. This is already satisfied by asserting the not-ready logs first.

### Pattern 4: Stage 3 Runner (run-stage3.sh)

Direct clone of `run-stage2.sh` with the following modifications:

1. Banner: "PSS Stage 3 Runner"
2. Stage 1 = scenarios 62-68 (PSS-14 through PSS-20) -- no internal sub-staging within Stage 3
3. **FAIL_COUNT gating from Stage 2:** The runner must check Stage 2's FAIL_COUNT BEFORE running any Stage 3 scenarios. This means run-stage3.sh must source run-stage2.sh's scenarios first OR check a persisted exit code from Stage 2. The established pattern (from run-stage2.sh checking Stage 1) is to run the prior stage's scenarios WITHIN the same runner and check the accumulated FAIL_COUNT before proceeding.

**Implication:** run-stage3.sh sources Stage 1 (53-58) + Stage 2 (59-61) scenarios first, checks FAIL_COUNT gate, then sources Stage 3 scenarios (62-68). This matches how run-stage2.sh sources Stage 1 scenarios and then checks FAIL_COUNT.

**Alternatively** (simpler): run-stage3.sh runs Stage 2 first (sources run-stage2.sh scenarios as a block), checks FAIL_COUNT, then runs Stage 3. The CONTEXT.md says "FAIL_COUNT gating from Stage 2 (same pattern as Stage 2 gates on Stage 1)" which means the same structural pattern.

Report file: `e2e-pss-stage3-report-YYYYMMDD-HHMMSS.md`

### Pattern 5: Cross-Stage Summary in run-all.sh

The CONTEXT.md requires `run-all.sh` to print cross-stage summary (total pass/fail across Stage 1, 2, and 3). The current `run-all.sh` uses glob `scenarios/[0-9]*.sh` which will auto-include scenarios 62-68 when they are created. However, run-all.sh only runs the non-PSS scenarios (01-52) by convention -- it is the "all except PSS" runner.

**Clarification needed at plan time:** Does run-all.sh need to be updated to include Stage 3, or does cross-stage summary live in run-stage3.sh? The existing run-stage2.sh generates its own report with PASS/FAIL across both Stage 1 and 2. Extending run-stage3.sh to source all 3 stages and provide a unified summary is the cleanest approach without changing run-all.sh.

### Recommended Project Structure

New files to create:

```
tests/e2e/
├── fixtures/
│   └── tenant-cfg08-pss-four-tenant.yaml    # new 4-tenant PSS fixture
├── scenarios/
│   ├── 62-pss-14-all-g1-resolved.sh         # PSS-14 gate pass
│   ├── 63-pss-15-all-g1-healthy.sh          # PSS-15 gate pass
│   ├── 64-pss-16-g1-mixed-pass.sh           # PSS-16 gate pass (Resolved+Healthy)
│   ├── 65-pss-17-all-g1-unresolved.sh       # PSS-17 gate block
│   ├── 66-pss-18-g1-resolved-unresolved.sh  # PSS-18 gate block
│   ├── 67-pss-19-g1-healthy-unresolved.sh   # PSS-19 gate block
│   └── 68-pss-20-all-g1-not-ready.sh        # PSS-20 gate block (Not Ready)
├── run-stage3.sh                             # new Stage 3 runner
└── lib/
    └── report.sh                             # update: add Stage 3 PSS category
```

Files to update:
- `tests/e2e/lib/report.sh` -- extend PSS category from `52|60` to `52|67` (scenarios 53-68)
- `tests/e2e/run-all.sh` -- add cross-stage summary (scope TBD at plan time)

### Anti-Patterns to Avoid

- **Using `tenant-cfg05-four-tenant-snapshot.yaml` directly:** That fixture uses SNS naming (`e2e-tenant-G1-T1`). PSS scenarios must use PSS naming (`e2e-pss-g1-t1`) for unambiguous log grep. Create a new fixture.
- **Glob-based fixture sourcing:** Existing scenarios all use explicit file references. Continue with explicit references in run-stage3.sh.
- **Omitting G1 positive assertion in gate-block scenarios:** Without asserting G1 was evaluated (tier=4 found), the G2 absence check could be a false pass if the system hadn't cycled yet.
- **Using `--since=15s` as the observation window for Not Ready (PSS-20):** The grace window is 6s. For Not Ready check, must use a shorter `--since` window (10s) and assert quickly before grace expires.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Log-based assertions | Custom kubectl log parser | `poll_until_log` in `sim.sh` |
| Absence proof | Custom time-bounded log poll | `kubectl logs --since=15s` + grep in for-loop (MTS-03C / SNS-B1c pattern) |
| Prometheus counter queries | Raw curl + jq | `snapshot_counter` / `poll_until` in `prometheus.sh` |
| ConfigMap lifecycle | Manual kubectl commands | `save_configmap` / `restore_configmap` in `kubectl.sh` |
| OID control | Custom HTTP calls | `sim_set_oid` in `sim.sh` |
| Stage gating | Custom FAIL_COUNT tracking | Existing `FAIL_COUNT` global from `common.sh` -- same pattern as run-stage2.sh |

**Key insight:** The SNS gate scenarios (46-52) already prove the exact gate behaviors that PSS-14 through PSS-20 need to verify. Phase 64 is primarily scripting work against proven patterns, not new logic.

---

## Common Pitfalls

### Pitfall 1: Gate-Pass Assertion Too Weak

**What goes wrong:** Asserting `"e2e-pss-g2-t3.*tier="` (any tier) instead of `"e2e-pss-g2-t3.*tier=3"` (specific tier value).
**Why it happens:** The generic pattern in SNS scenarios uses `tier=` for gate-pass. CONTEXT.md explicitly requires stronger proof for PSS Stage 3.
**How to avoid:** Assert the specific expected tier value for G2 tenants. Since G2 tenants are primed to healthy (all OIDs in-range), they will reach tier=3. Use `"e2e-pss-g2-t3.*tier=3"` and `"e2e-pss-g2-t4.*tier=3"`.
**Warning signs:** A test that passes with gate accidentally blocked (G2 happened to evaluate from a prior scenario's state).

### Pitfall 2: G2 Absence Check Without Prior G1 Positive Assertion

**What goes wrong:** asserting G2 tier logs absent before confirming G1 was evaluated, creating a false pass if the SnapshotJob hasn't cycled yet.
**Why it happens:** A developer writes the negative assertion first (it's the key requirement) and skips the positive one.
**How to avoid:** Always assert G1 tier (positive) FIRST, then sleep 10s, then assert G2 absence. Reference: SNS-B1 (scenarios 49-52) for the exact ordering.
**Warning signs:** Gate-block tests pass even when run immediately after fixture apply without waiting for a cycle.

### Pitfall 3: PSS-20 (Not Ready) sleep 8s Mistake

**What goes wrong:** Adding `sleep 8` (standard healthy-prime wait) before the "not ready" assertion in PSS-20, causing the grace window to expire and the tenant to transition out of Not Ready before the assertion fires.
**Why it happens:** All other PSS scenarios prime then sleep 8s. PSS-20 deliberately does NOT prime G1 and does NOT sleep 8s.
**How to avoid:** For PSS-20, DO NOT prime G1 OIDs and DO NOT sleep 8s. Assert "not ready" log within 5s of fixture apply. Then sleep 5s (not 10s) and check G2 absence with `--since=10s`.
**Warning signs:** PSS-20 "not ready" assertions fail because the grace window expired before polling.

### Pitfall 4: Observation Window Too Short for Gate-Block

**What goes wrong:** Using `sleep 5` + `--since=5s` for gate-block scenarios (non-Not-Ready ones), causing the assertion to run before enough SnapshotJob cycles have accumulated to confirm the gate is consistently blocking.
**Why it happens:** Short sleep is used in PSS-20 (Not Ready) but inappropriate for Unresolved gate-block.
**How to avoid:** For Unresolved gate-block scenarios (PSS-17/18/19), use `sleep 10` + `--since=15s`. This covers 10 SnapshotJob cycles at 1s interval, consistent with SNS-B1 pattern.
**Warning signs:** Intermittent gate-block test failures (G2 log appears occasionally from a prior scenario's state bleed).

### Pitfall 5: Tenant Name Typos in Log Grep Pattern

**What goes wrong:** Grepping for `"e2e-pss-g2.*tier="` when the actual tenant name is `e2e-pss-g2-t3`. A too-broad pattern could match tenant names from prior fixtures still in logs.
**Why it happens:** Multi-tenant scenarios have many similar log patterns.
**How to avoid:** Use specific tenant name patterns: `"e2e-pss-g2-t3.*tier="` not `"e2e-pss-g2.*tier="`. The `--since=15s` window helps isolate current scenario logs from previous scenarios.
**Warning signs:** Test passes on first run but fails on re-run when prior scenario logs bleed in.

### Pitfall 6: Mixed-State Scenarios Require Careful OID Choreography

**What goes wrong:** In PSS-16 (G1 mixed Resolved+Healthy), setting T1 to Resolved and expecting T2 to stay Healthy, but T2's OIDs were already manipulated by T1's state setup.
**Why it happens:** All 12 OIDs are independent but share the same `reset_oid_overrides` cleanup. After priming, individual OID changes only affect the specified OID.
**How to avoid:** For PSS-16 (T1 Resolved, T2 Healthy): set T1's resolved OIDs (4.2=0, 4.3=0). T2 keeps the primed values (5.x stay at 10/1/1). For PSS-18 (T1 Resolved, T2 Unresolved): set 4.2=0, 4.3=0 for T1 Resolved AND 5.1=0 for T2 Unresolved. For PSS-19 (T1 Healthy, T2 Unresolved): only set 5.1=0 for T2 Unresolved, T1 stays at primed (all in-range).
**Warning signs:** Mixed-state scenarios show both tenants in the wrong state.

### Pitfall 7: G2 Metric Counters for Gate-Pass Proof

**What goes wrong:** Using Prometheus counter increments (e.g., `snmp_snapshot_tenant_evaluations_total`) to prove G2 was evaluated, when the simpler log assertion is sufficient and already proven in SNS scenarios.
**Why it happens:** Wanting "stronger" proof via metrics.
**How to avoid:** Log-based assertions (`poll_until_log`) are sufficient per CONTEXT.md decisions ("verify G2 tenant tier values (e.g., Healthy), not just log presence"). The tier=3 log IS the tier value. No separate metric counter assertion is needed for gate-pass.

---

## Code Examples

### Fixture: tenant-cfg08-pss-four-tenant.yaml

```yaml
# Source: pattern from tests/e2e/fixtures/tenant-cfg07-pss-two-tenant.yaml
# T1: 4.x OIDs (e2e_port_utilization=eval, e2e_channel_state=res1, e2e_bypass_status=res2)
# T2: 5.x OIDs (e2e_eval_T2=eval, e2e_res1_T2=res1, e2e_res2_T2=res2)
# T3: 6.x OIDs (e2e_eval_T3=eval, e2e_res1_T3=res1, e2e_res2_T3=res2)
# T4: 7.x OIDs (e2e_eval_T4=eval, e2e_res1_T4=res1, e2e_res2_T4=res2)
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenants
  namespace: simetra
data:
  tenants.json: |
    [
      { "Name": "e2e-pss-g1-t1", "Priority": 1, "SuppressionWindowSeconds": 10,
        "Metrics": [
          { "Ip": "e2e-simulator.simetra.svc.cluster.local", "Port": 161,
            "MetricName": "e2e_port_utilization", "TimeSeriesSize": 3,
            "GraceMultiplier": 2.0, "Role": "Evaluate", "Threshold": { "Min": 10.0 } },
          { "Ip": "e2e-simulator.simetra.svc.cluster.local", "Port": 161,
            "MetricName": "e2e_channel_state", "Role": "Resolved", "Threshold": { "Min": 1.0 } },
          { "Ip": "e2e-simulator.simetra.svc.cluster.local", "Port": 161,
            "MetricName": "e2e_bypass_status", "Role": "Resolved", "Threshold": { "Min": 1.0 } }
        ],
        "Commands": [
          { "Ip": "e2e-simulator.simetra.svc.cluster.local", "Port": 161,
            "CommandName": "e2e_set_bypass", "Value": "0", "ValueType": "Integer32" }
        ]
      },
      { "Name": "e2e-pss-g1-t2", "Priority": 1, "SuppressionWindowSeconds": 10,
        "Metrics": [
          { "Ip": "e2e-simulator.simetra.svc.cluster.local", "Port": 161,
            "MetricName": "e2e_eval_T2", "TimeSeriesSize": 3, "GraceMultiplier": 2.0,
            "Role": "Evaluate", "Threshold": { "Min": 10.0 } },
          ... (res1_T2, res2_T2 with Min:1)
        ],
        "Commands": [ ... e2e_set_bypass ... ]
      },
      { "Name": "e2e-pss-g2-t3", "Priority": 2, ... (6.x OIDs) },
      { "Name": "e2e-pss-g2-t4", "Priority": 2, ... (7.x OIDs) }
    ]
```

### Gate-Pass Scenario: PSS-14 (All G1 Resolved)

```bash
# Source: pattern from tests/e2e/scenarios/46-sns-a1-both-resolved.sh
# (adapted for PSS naming and stronger G2 tier value assertion)

FIXTURES_DIR="..."

# Setup
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
kubectl apply -f "$FIXTURES_DIR/tenant-cfg08-pss-four-tenant.yaml" > /dev/null 2>&1 || true
poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30

# Prime all 12 OIDs
sim_set_oid "4.1" "10"; sim_set_oid "4.2" "1"; sim_set_oid "4.3" "1"
sim_set_oid "5.1" "10"; sim_set_oid "5.2" "1"; sim_set_oid "5.3" "1"
sim_set_oid "6.1" "10"; sim_set_oid "6.2" "1"; sim_set_oid "6.3" "1"
sim_set_oid "7.1" "10"; sim_set_oid "7.2" "1"; sim_set_oid "7.3" "1"
sleep 8  # 6s grace + 2s margin

# Set G1 to Resolved: all resolved OIDs violated, eval stays in-range
sim_set_oid "4.2" "0"; sim_set_oid "4.3" "0"  # T1 resolved violated
sim_set_oid "5.2" "0"; sim_set_oid "5.3" "0"  # T2 resolved violated

# Assert G1 reached tier=2 (positive, proves evaluation happened)
poll_until_log 30 1 "e2e-pss-g1-t1.*tier=2" 15 -> record_pass/fail "PSS-14A"
poll_until_log 30 1 "e2e-pss-g1-t2.*tier=2" 15 -> record_pass/fail "PSS-14B"

# Assert G2 was evaluated AND reached tier=3 (stronger proof per CONTEXT.md)
poll_until_log 30 1 "e2e-pss-g2-t3.*tier=3" 15 -> record_pass/fail "PSS-14C"
poll_until_log 30 1 "e2e-pss-g2-t4.*tier=3" 15 -> record_pass/fail "PSS-14D"

# Cleanup
reset_oid_overrides
restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml"
```

### Gate-Block Scenario: PSS-17 (All G1 Unresolved)

```bash
# Source: pattern from tests/e2e/scenarios/49-sns-b1-both-unresolved.sh

# (Setup + prime same as gate-pass)
# Set G1 to Unresolved: eval violated, resolved in-range
sim_set_oid "4.1" "0"   # T1 eval < Min:10
sim_set_oid "5.1" "0"   # T2 eval < Min:10

# Assert G1 reached tier=4 (positive assertion -- proves system is active)
poll_until_log 30 1 "e2e-pss-g1-t1.*tier=4" 15 -> record_pass/fail "PSS-17A"
poll_until_log 30 1 "e2e-pss-g1-t2.*tier=4" 15 -> record_pass/fail "PSS-17B"

# Observation window: 10s at 1s interval = 10 cycles of blocked gate
sleep 10

# Assert G2 absent from tier logs (15s window covers the observation period)
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
G2_FOUND=0
for POD in $PODS; do
    G2_LOGS=$(kubectl logs "$POD" -n simetra --since=15s 2>/dev/null \
        | grep "e2e-pss-g2.*tier=" || echo "") || true
    if [ -n "$G2_LOGS" ]; then G2_FOUND=1; break; fi
done
[ "$G2_FOUND" -eq 0 ] && record_pass "PSS-17C: G2 not evaluated (all G1 Unresolved)" \
                       || record_fail "PSS-17C: G2 not evaluated (all G1 Unresolved)"
```

### Gate-Block Scenario: PSS-20 (All G1 Not Ready)

```bash
# Source: pattern from tests/e2e/scenarios/50-sns-b2-both-not-ready.sh

# Apply fixture (same)
# Prime ONLY G2 OIDs -- G1 stays unprimed
sim_set_oid "6.1" "10"; sim_set_oid "6.2" "1"; sim_set_oid "6.3" "1"
sim_set_oid "7.1" "10"; sim_set_oid "7.2" "1"; sim_set_oid "7.3" "1"
# NO sim_set_oid for 4.x or 5.x
# NO sleep 8s

# Assert G1 "not ready" before grace window expires (5s timeout)
poll_until_log 5 1 "e2e-pss-g1-t1.*not ready" 5 -> record_pass/fail "PSS-20A"
poll_until_log 5 1 "e2e-pss-g1-t2.*not ready" 5 -> record_pass/fail "PSS-20B"

# Short observation, G2 absence check within not-ready window
sleep 5
# grep G2 with --since=10s
G2_FOUND=0 ... (same pattern, --since=10s)
record_pass/fail "PSS-20C: G2 not evaluated (G1 not ready)"
```

### run-stage3.sh Structure

```bash
#!/usr/bin/env bash
set -euo pipefail
# Source all libs, setup cleanup, start port-forwards, pre-flight checks

# Stage 1 (53-58) + Stage 2 (59-61)
for scenario in \
    "$SCRIPT_DIR/scenarios/53-pss-01-readiness.sh" \
    ... (54-58, 59-61) ...; do
    [ -f "$scenario" ] && source "$scenario"
done

# Gate: check FAIL_COUNT from Stage 1+2
if [ "$FAIL_COUNT" -gt 0 ]; then
    log_error "Stage 2 had $FAIL_COUNT failure(s) -- skipping Stage 3 scenarios"
    generate_report "$REPORT_FILE"; print_summary; exit 1
fi

# Stage 3 (62-68)
for scenario in \
    "$SCRIPT_DIR/scenarios/62-pss-14-all-g1-resolved.sh" \
    ... (63-68) ...; do
    [ -f "$scenario" ] && source "$scenario"
done

generate_report "$REPORT_FILE"
print_summary
[ "$FAIL_COUNT" -eq 0 ]
```

### Report Category Update

```bash
# Source: tests/e2e/lib/report.sh
# Current: "Progressive Snapshot Suite|52|60"
# Updated: "Progressive Snapshot Suite|52|67"
# This covers scenarios 53-68 (indices 52-67, 0-based)
```

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|-----------------|--------|
| SNS scenarios use generic `e2e-tenant-G1-T1` names | PSS uses `e2e-pss-g1-t1` names -- grep-safe disambiguation | Tenant name typos caught immediately |
| SNS gate-pass uses `tier=` (any tier) | PSS gate-pass must use `tier=3` (specific Healthy value) | Stronger proof of actual evaluation result |
| run-stage2.sh gates on Stage 1 FAIL_COUNT | run-stage3.sh gates on Stage 1+2 FAIL_COUNT | Same established pattern, extended |
| 4-tenant fixture shared by SNS and advance-gate | New PSS-specific fixture for PSS naming convention | Clean separation of SNS and PSS suites |

**Scenario numbering context:**
- Scenarios 1-28: Legacy (pipeline counters, labels, OID mutations)
- Scenarios 29-45: SNS single-tenant and multi-tenant
- Scenarios 46-52: SNS advance gate (4-tenant, SNS naming)
- Scenarios 53-61: PSS Stage 1 (single-tenant) + Stage 2 (two-tenant)
- Scenarios 62-68: PSS Stage 3 (four-tenant, advance gate) -- new

---

## Claude's Discretion Recommendations

### Fixture File Structure

**Recommendation:** Single fixture file `tenant-cfg08-pss-four-tenant.yaml`. Rationale: all existing multi-tenant fixtures are single files; separate files would require applying multiple configmaps in correct order. One file keeps scenario cleanup simple (single restore operation).

### Threshold Values for All 4 Tenants

**Recommendation:** Use identical threshold structure for all 4 tenants:
- Evaluate: Min=10.0, TimeSeriesSize=3, GraceMultiplier=2.0 (grace=6s)
- Resolved: Min=1.0

This matches the existing two-tenant PSS fixture (07) and the SNS four-tenant fixture (05). Identical thresholds simplify scenario scripting -- the same OID value map applies uniformly.

### Script Numbering Continuation from Stage 2

**Recommendation:** Stage 3 starts at scenario file 62 (immediately after 61-pss-13-both-unresolved.sh). Numbering: 62 through 68 for PSS-14 through PSS-20. This maintains strict sequential numbering alignment with the scenario files as seen by report.sh.

---

## Open Questions

1. **run-all.sh cross-stage summary scope**
   - What we know: run-all.sh currently uses glob `scenarios/[0-9]*.sh` which would auto-include 62-68 when created. But run-all.sh is documented as "E2E System Verification" (the full non-PSS suite).
   - What's unclear: Should run-all.sh print PSS Stage 3 results, or should cross-stage summary live only in run-stage3.sh? CONTEXT.md says "run-all.sh prints cross-stage summary at the end" which implies run-all.sh is updated.
   - Recommendation: Plan 1 should clarify. The safest approach is to update run-stage3.sh to include all 3 stages and produce the full PSS cross-stage summary, while run-all.sh is unchanged (it runs all non-PSS scenarios). If run-all.sh must be updated, it should just call/source run-stage3.sh and print a combined summary.

2. **Scenario file names for PSS-14 through PSS-20**
   - What we know: Naming convention `NN-pss-NN-description.sh` (e.g., `62-pss-14-all-g1-resolved.sh`).
   - What's unclear: Whether the file number prefix (62-68) or the PSS scenario number (14-20) is the primary identifier in the description.
   - Recommendation: `62-pss-14-all-g1-resolved.sh`, `63-pss-15-all-g1-healthy.sh`, `64-pss-16-g1-mixed-pass.sh`, `65-pss-17-all-g1-unresolved.sh`, `66-pss-18-g1-resolved-unresolved.sh`, `67-pss-19-g1-healthy-unresolved.sh`, `68-pss-20-all-g1-not-ready.sh`.

---

## Sources

### Primary (HIGH confidence)

All findings from direct source code inspection:

- `tests/e2e/scenarios/46-sns-a1-both-resolved.sh` -- gate-pass pattern (G1 Resolved)
- `tests/e2e/scenarios/47-sns-a2-both-healthy.sh` -- gate-pass pattern (G1 Healthy)
- `tests/e2e/scenarios/48-sns-a3-resolved-healthy.sh` -- gate-pass pattern (mixed)
- `tests/e2e/scenarios/49-sns-b1-both-unresolved.sh` -- gate-block pattern (G1 Unresolved)
- `tests/e2e/scenarios/50-sns-b2-both-not-ready.sh` -- gate-block pattern (Not Ready)
- `tests/e2e/scenarios/51-sns-b3-resolved-unresolved.sh` -- gate-block pattern (mixed)
- `tests/e2e/scenarios/52-sns-b4-healthy-unresolved.sh` -- gate-block pattern (mixed)
- `tests/e2e/scenarios/40-mts-03-starvation-proof.sh` -- absence assertion pattern (--since=120s)
- `tests/e2e/run-stage2.sh` -- Stage 2 runner template (FAIL_COUNT gating)
- `tests/e2e/fixtures/tenant-cfg05-four-tenant-snapshot.yaml` -- existing 4-tenant fixture (SNS naming)
- `tests/e2e/fixtures/tenant-cfg07-pss-two-tenant.yaml` -- existing 2-tenant PSS fixture (PSS naming)
- `tests/e2e/fixtures/.original-oid-metric-map-configmap.yaml` -- confirmed OID map for .999.4.x through .999.7.x
- `src/SnmpCollector/Jobs/SnapshotJob.cs` -- confirmed TierResult enum, gate logic, log message patterns
- `tests/e2e/lib/sim.sh`, `prometheus.sh`, `kubectl.sh`, `common.sh`, `report.sh` -- all lib functions confirmed

---

## Metadata

**Confidence breakdown:**
- Fixture design: HIGH -- OIDs confirmed in live OID map, naming convention from existing PSS fixtures
- Scenario structure: HIGH -- direct templates exist in SNS scenarios 46-52 for all 7 combinations
- Gate logic log patterns: HIGH -- log messages read directly from SnapshotJob.cs (lines 139, 158, 174, 212)
- Stage 3 runner: HIGH -- direct template in run-stage2.sh
- Report.sh update: HIGH -- category index arithmetic from current `52|60` to `52|67`
- run-all.sh cross-stage summary: LOW -- CONTEXT.md is ambiguous about whether run-all.sh or run-stage3.sh owns this

**Research date:** 2026-03-20
**Valid until:** 2026-04-20 (stable codebase, no external dependencies)
