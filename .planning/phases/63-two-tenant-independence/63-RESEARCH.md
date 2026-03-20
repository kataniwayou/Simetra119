# Phase 63: Two Tenant Independence - Research

**Researched:** 2026-03-20
**Domain:** E2E snapshot evaluation testing (bash scenario scripts, 2-tenant K8s fixture, stage gating runner)
**Confidence:** HIGH

## Summary

Phase 63 extends the Progressive Snapshot Suite (PSS) from single-tenant (Phase 62) to two-tenant scenarios. It proves that two tenants in the same priority group evaluate independently -- one tenant's state does not bleed into the other's tier determination or command dispatch. It also introduces the Stage 2 runner concept: a separate script that checks FAIL_COUNT from Stage 1 before running Stage 2 scenarios.

The infrastructure is fully established by Phase 62: sim.sh (per-OID control via OID suffix), prometheus.sh (counter snapshot, polling), kubectl.sh (ConfigMap save/restore), common.sh (record_pass/fail, FAIL_COUNT), and report.sh (category ranges). The two-tenant fixture needs two tenants at the same Priority with independent OID namespaces -- T2 OIDs (.999.5.x) for T1 and T3 OIDs (.999.6.x) for T2, both in the 1s poll group. This gives the same 6s grace window as Phase 62. The SNS-A/B suite (scenarios 46-52) provides verified precedents for two-tenant independence assertions.

The stage gating requirement (PSS-INF-01) requires a dedicated runner script that inspects FAIL_COUNT after Stage 1 scenarios complete and exits early if any Stage 1 sub-assertion failed. This is a new pattern not yet in the codebase -- run-all.sh currently runs all scenarios unconditionally and uses FAIL_COUNT only at the very end. The Stage 2 runner must be a new bash script (e.g., `run-stage2.sh` or integrated into run-all.sh with a gating checkpoint).

**Primary recommendation:** Create a 2-tenant PSS fixture using T2 OIDs (.999.5.x) for "e2e-pss-t1" and T3 OIDs (.999.6.x) for "e2e-pss-t2", both at Priority=1. Implement three independence scenarios (59-61). Create a new run-stage2.sh that sources Stage 1 scenarios then checks FAIL_COUNT before sourcing Stage 2 scenarios, exiting with a clear message if Stage 1 had failures.

## Standard Stack

### Core Infrastructure (ALL REUSE -- do not create new)

| Component | Location | Purpose | Verified |
|-----------|----------|---------|----------|
| sim.sh | tests/e2e/lib/sim.sh | sim_set_oid("5.x"), sim_set_oid("6.x"), reset_oid_overrides | YES - read source |
| kubectl.sh | tests/e2e/lib/kubectl.sh | save_configmap, restore_configmap (annotation-strip) | YES - read source |
| prometheus.sh | tests/e2e/lib/prometheus.sh | snapshot_counter, poll_until | YES - used in Phase 62 |
| common.sh | tests/e2e/lib/common.sh | record_pass, record_fail, FAIL_COUNT (global), PASS_COUNT | YES - read source |
| report.sh | tests/e2e/lib/report.sh | generate_report with _REPORT_CATEGORIES array | YES - read source |

### OID Assignments for 2-Tenant PSS Fixture

| Tenant | OID Suffix | MetricName | Poll Group | Role | Threshold |
|--------|-----------|-----------|-----------|------|-----------|
| e2e-pss-t1 | 5.1 | e2e_eval_T2 | 1s | Evaluate | Min: 10.0 |
| e2e-pss-t1 | 5.2 | e2e_res1_T2 | 1s | Resolved | Min: 1.0 |
| e2e-pss-t1 | 5.3 | e2e_res2_T2 | 1s | Resolved | Min: 1.0 |
| e2e-pss-t2 | 6.1 | e2e_eval_T3 | 1s | Evaluate | Min: 10.0 |
| e2e-pss-t2 | 6.2 | e2e_res1_T3 | 1s | Resolved | Min: 1.0 |
| e2e-pss-t2 | 6.3 | e2e_res2_T3 | 1s | Resolved | Min: 1.0 |

**Why these OIDs:** T2 (.999.5.x) and T3 (.999.6.x) OIDs are in the 1s poll group (confirmed via simetra-devices.yaml and existing cfg05 fixture which uses them at 1s). Grace window = 3 * 1 * 2.0 = 6s. T3 OIDs are NOT used by Phase 62's single-tenant fixture (which only uses T2 OIDs), so no contention when PSS scenarios run in sequence. The cfg05 four-tenant fixture also uses T3 OIDs, but cfg05 is always saved/restored, so no conflict.

**Note on tenant naming:** The Phase 62 single-tenant fixture uses "e2e-pss-tenant". For two-tenant clarity, use "e2e-pss-t1" and "e2e-pss-t2" so log grep patterns are unambiguous per-tenant.

### Current Counter Name

Phase 62 established: **`snmp_command_dispatched_total`** (renamed from snmp_command_sent_total in quick-081). All new scenarios must use `snmp_command_dispatched_total`. The `device_name="E2E-SIM"` label is the SNMP device name; it is the same for both tenants since both dispatch commands to E2E-SIM.

### Report Category

Current `_REPORT_CATEGORIES` in report.sh covers indices `52-57` for "Progressive Snapshot Suite" (scenarios 53-58). Phase 63 adds scenarios 59-61 (3 scenarios). The report.sh category must be extended from `52-57` to `52-61` (or split into sub-categories). Extending the range is simpler.

## Architecture Patterns

### 2-Tenant PSS Fixture

```yaml
# tenant-cfg07-pss-two-tenant.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenants
  namespace: simetra
data:
  tenants.json: |
    [
      {
        "Name": "e2e-pss-t1",
        "Priority": 1,
        "SuppressionWindowSeconds": 10,
        "Metrics": [
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_eval_T2",
            "TimeSeriesSize": 3,
            "GraceMultiplier": 2.0,
            "Role": "Evaluate",
            "Threshold": { "Min": 10.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_res1_T2",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_res2_T2",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0 }
          }
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
      },
      {
        "Name": "e2e-pss-t2",
        "Priority": 1,
        "SuppressionWindowSeconds": 10,
        "Metrics": [
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_eval_T3",
            "TimeSeriesSize": 3,
            "GraceMultiplier": 2.0,
            "Role": "Evaluate",
            "Threshold": { "Min": 10.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_res1_T3",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_res2_T3",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0 }
          }
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
    ]
```

### Stage 2 Runner Script Pattern (PSS-INF-01)

The FAIL_COUNT global is set by common.sh and shared across all sourced scenario scripts within a single shell process. The Stage 2 runner must:
1. Source all libraries (same as run-all.sh)
2. Source Stage 1 scenarios (53-58) to accumulate FAIL_COUNT
3. Check FAIL_COUNT -- if > 0, print message and exit 1 WITHOUT sourcing Stage 2 scenarios
4. Source Stage 2 scenarios (59-61) only if FAIL_COUNT == 0

```bash
#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

source "$SCRIPT_DIR/lib/common.sh"
source "$SCRIPT_DIR/lib/prometheus.sh"
source "$SCRIPT_DIR/lib/kubectl.sh"
source "$SCRIPT_DIR/lib/report.sh"
source "$SCRIPT_DIR/lib/sim.sh"

# ... port-forwards and pre-flight checks (same as run-all.sh) ...

# ---- Stage 1: Single Tenant Scenarios ----
echo "=== Stage 1: Single Tenant (PSS-01 through PSS-06) ==="
for scenario in "$SCRIPT_DIR"/scenarios/5[3-8]-pss-*.sh; do
    [ -f "$scenario" ] && source "$scenario" && echo ""
done

STAGE1_FAIL_COUNT=$FAIL_COUNT

# ---- Stage Gate ----
if [ "$STAGE1_FAIL_COUNT" -gt 0 ]; then
    log_error "Stage 1 had ${STAGE1_FAIL_COUNT} failure(s). Skipping Stage 2."
    generate_report "$REPORT_FILE"
    print_summary
    exit 1
fi

log_info "Stage 1 passed (0 failures). Proceeding to Stage 2."

# ---- Stage 2: Two Tenant Independence Scenarios ----
echo "=== Stage 2: Two Tenant Independence (PSS-11 through PSS-13) ==="
for scenario in "$SCRIPT_DIR"/scenarios/5[9-9]-pss-*.sh "$SCRIPT_DIR"/scenarios/6[0-1]-pss-*.sh; do
    [ -f "$scenario" ] && source "$scenario" && echo ""
done
```

**Alternative: integrate gating checkpoint into run-all.sh** rather than a separate script. The ROADMAP says "Stage 2 runner script checks FAIL_COUNT from Stage 1" which implies a distinct runner. A separate `run-stage2.sh` is cleaner and matches the ROADMAP language.

**Decision recommendation:** Create `tests/e2e/run-stage2.sh` as a self-contained runner that includes Stage 1 and Stage 2 scenarios with a gate between them. run-all.sh remains unchanged (runs all scenarios unconditionally for CI). run-stage2.sh is the progressive gated runner.

### Scenario Script Structure (established from Phase 62)

Every Phase 63 scenario follows the same exact structure as Phase 62 PSS scenarios. Key differences for two-tenant scenarios:

1. **Fixture:** `tenant-cfg07-pss-two-tenant.yaml` (not cfg06-pss-single)
2. **OID priming:** Both tenants primed -- 6 sim_set_oid calls (5.1-5.3 for T1, 6.1-6.3 for T2)
3. **Log patterns:** Must match BOTH tenant names independently in same time window
4. **Counter assertions:** snmp_command_dispatched_total increments for BOTH when both are Unresolved

### Independence Assertion Pattern

The key property to assert: tenant A's OID values do NOT affect tenant B's tier log.

```bash
# PSS-11: T1=Healthy + T2=Unresolved
# Prime both, sleep 8s
# Set: T1 stays healthy (5.1=10, res in-range); T2 set evaluate violated (6.1=0, res in-range)
# Assert T1 tier=3: poll_until_log 30 1 "e2e-pss-t1.*tier=3" 15
# Assert T2 tier=4: poll_until_log 30 1 "e2e-pss-t2.*tier=4" 15
# Assert T1 no commands: baseline BEFORE_SENT, sleep 10s, DELTA must be 0
#   (T2 commands increment the counter, so must capture T1's baseline separately -- but
#   snmp_command_dispatched_total is a single counter for E2E-SIM device. Can't split per-tenant.)
```

**Critical finding on command counter per-tenant assertion (PSS-11/PSS-12):**

`snmp_command_dispatched_total` with label `device_name="E2E-SIM"` is a single counter for ALL tenants dispatching to E2E-SIM. When T2 dispatches commands, the counter increments regardless of T1's state. To assert "T1 has no commands dispatched while T2 does", we CANNOT use the Prometheus counter alone because the counter aggregates both tenants' dispatches.

**Options:**
1. **Log-based assertion for T1 no-dispatch:** Assert tier=3 log for T1 (which means no commands were dispatched by T1 -- tier=3 is the healthy state that never dispatches). The tier=3 log IS the evidence of no T1 command dispatch.
2. **Negative log assertion:** Assert absence of T1 tier=4 log during the observation window using pod log scan (same pattern as PSS-03C absence check).
3. **Accept the limitation:** PSS-11's "T1 no commands" is proven by T1 reaching tier=3 (healthy). Tier=3 by definition means no commands. The counter delta test only matters for PSS-13 where BOTH tenants should dispatch.

**Recommendation:** For PSS-11 (T1=Healthy, T2=Unresolved): prove independence via log assertions only -- tier=3 for T1 (proves healthy, no dispatch) and tier=4 for T2 (proves commands). Skip the counter-based no-dispatch assertion for T1 since the counter doesn't distinguish tenants. For PSS-13 (both Unresolved): counter delta must be >= 2 (at least one command per tenant dispatched).

### Log Patterns for 2-Tenant Scenarios

| Scenario | T1 Expected Log | T2 Expected Log |
|----------|----------------|----------------|
| PSS-11 (T1=Healthy, T2=Unresolved) | `e2e-pss-t1.*tier=3` | `e2e-pss-t2.*tier=4 — commands enqueued\|tier=4 -- commands enqueued` |
| PSS-12 (T1=Resolved, T2=Healthy) | `e2e-pss-t1.*tier=2` | `e2e-pss-t2.*tier=3` |
| PSS-13 (Both Unresolved) | `e2e-pss-t1.*tier=4` | `e2e-pss-t2.*tier=4` |

All patterns use the `--since` window approach (15s default). Since both tenants evaluate in the same SnapshotJob cycle, their tier logs appear within milliseconds of each other. poll_until_log runs sequentially, so after the first tenant's log is confirmed, the second should already be present.

### Scenario Numbers

Phase 62 used scenarios 53-58. Phase 63 will use:
- `59-pss-11-t1-healthy-t2-unresolved.sh` (PSS-11)
- `60-pss-12-t1-resolved-t2-healthy.sh` (PSS-12)
- `61-pss-13-both-unresolved.sh` (PSS-13)

The report.sh category `"Progressive Snapshot Suite|52|57"` must be updated to `"Progressive Snapshot Suite|52|60"` to cover scenarios 53-61 (indices 52-60).

### Anti-Patterns to Avoid

- **Sharing OID namespaces:** T1 and T2 MUST use different OID suffixes (.999.5.x vs .999.6.x). Sharing a single OID across two tenants means changing that OID changes BOTH tenants' metric values simultaneously -- this would invalidate the independence test.
- **Asserting counter-based T1-no-dispatch directly:** `snmp_command_dispatched_total` is per-device, not per-tenant. Use tier=3 log as the evidence of T1 not dispatching.
- **Not priming both tenants:** After applying the 2-tenant fixture, ALL 6 OIDs must be primed (5.1-5.3 AND 6.1-6.3) to ensure both tenants pass the readiness grace simultaneously.
- **Using .999.4.x OIDs:** These are in the 10s poll group. Using them for a tenant gives 60s grace windows and breaks timing assumptions.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OID value control | Custom HTTP calls | sim_set_oid / sim_set_oid_stale | Already tested and working |
| ConfigMap backup/restore | kubectl get + kubectl apply | save_configmap / restore_configmap | Handles annotation stripping to prevent double-reload |
| Counter polling | Sleep loops + query | poll_until | Built-in timeout and comparison against baseline |
| Log scanning | Custom kubectl loop | poll_until_log | Handles multi-pod cluster, since-window, timeout |
| Test result tracking | Custom arrays | record_pass / record_fail | Integrates with FAIL_COUNT and report.sh |
| Stage gating | Separate state file | Check `$FAIL_COUNT` directly in runner | FAIL_COUNT is a shell global set by record_fail, available in-process |

## Common Pitfalls

### Pitfall 1: Single OID Namespace for Two Independent Tenants
**What goes wrong:** Both tenants reference the same MetricName (e.g., both use "e2e_eval_T2"). Setting the OID changes the value for both tenants' evaluate holders simultaneously. The independence test is invalidated -- "setting T2's OID" is indistinguishable from "setting T1's OID".
**Why it happens:** Phase 62 single-tenant fixture uses T2 OIDs. Temptation is to reuse same OIDs for both tenants in Phase 63.
**How to avoid:** T1 uses T2 OIDs (.999.5.x / e2e_eval_T2 etc.), T2 uses T3 OIDs (.999.6.x / e2e_eval_T3 etc.).
**Warning signs:** Changing OID 5.1 causes BOTH tenant logs to show tier changes.

### Pitfall 2: Counter Ambiguity for T1-No-Commands Assertion
**What goes wrong:** Trying to assert "T1 had no commands dispatched" via `snmp_command_dispatched_total` when T2 IS dispatching commands. T2's commands increment the shared counter, making the delta > 0 even though T1 sent nothing.
**Why it happens:** The counter label is `device_name="E2E-SIM"` (the target device), not the dispatching tenant.
**How to avoid:** For PSS-11 (T1=Healthy, T2=Unresolved), assert "T1 no commands" via the tier=3 log (tier=3 by definition excludes command dispatch). Only use counter-delta assertions when BOTH tenants dispatch (PSS-13).
**Warning signs:** The delta assertion for "T1 no commands" is always >= 1 due to T2's dispatch.

### Pitfall 3: Stage Gate Checks Stale FAIL_COUNT
**What goes wrong:** Stage 2 runner reads FAIL_COUNT after Stage 1 scenarios but FAIL_COUNT includes failures from pre-Stage 1 scenarios (e.g., scenarios 1-52 if the runner includes earlier scenarios).
**Why it happens:** FAIL_COUNT is global and cumulative. Running all scenarios before the gate check includes non-PSS failures.
**How to avoid:** The Stage 2 runner should ONLY source Stage 1 PSS scenarios (53-58) before the gate, OR capture FAIL_COUNT at the start of Stage 1 and compare the delta (STAGE1_FAIL = FAIL_COUNT_AFTER - FAIL_COUNT_BEFORE).
**Warning signs:** Stage 2 is skipped even when Stage 1 PSS scenarios all pass, because earlier scenarios failed.

### Pitfall 4: Report Category Range Not Updated
**What goes wrong:** report.sh still has `"Progressive Snapshot Suite|52|57"`. Scenarios 59-61 are not categorized and appear outside any category section (silently dropped or uncategorized in the output).
**Why it happens:** Adding scenarios doesn't automatically update the category range.
**How to avoid:** Update `_REPORT_CATEGORIES` to `"Progressive Snapshot Suite|52|60"` when adding scenarios 59-61.
**Warning signs:** Report shows only 6 PSS scenarios but 9 were run.

### Pitfall 5: Log Contamination Between PSS-11 and PSS-12
**What goes wrong:** PSS-12 runs immediately after PSS-11. The tier=4 log for e2e-pss-t2 from PSS-11 is still within the `--since=15s` window when PSS-12's poll_until_log runs. Poll for PSS-12's tier=2 for T1 passes (correct), but poll for tier=3 for T2 might see leftover tier=3 from PSS-11 that don't match PSS-12's intent.
**Why it happens:** Scenarios source sequentially in the same shell. Short since windows overlap when scenarios run quickly.
**How to avoid:** Each scenario resets OIDs via reset_oid_overrides and restores ConfigMap at cleanup. The ConfigMap restore triggers a tenant reload, which resets holder state. Use a flush sleep (3-5s) after ConfigMap restore. The poll_until_log `since` window of 15s is the existing established value.

### Pitfall 6: Not Waiting for Tenant Reload After Fixture Apply
**What goes wrong:** Scenario applies 2-tenant fixture and immediately sets OIDs without waiting for reload. The old single-tenant fixture is still active for the first few seconds. T2's OIDs are primed but there's no tenant consuming them.
**Why it happens:** Fixture apply is eventually consistent via K8s watch.
**How to avoid:** Always use `poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30` after kubectl apply. This is the established pattern from all Phase 62 scenarios.

## Code Examples

### PSS-11: T1=Healthy + T2=Unresolved (Source: verified pattern from Phase 62 + SNS-A1)

```bash
# Scenario 59: PSS-11 T1=Healthy + T2=Unresolved
# Fixture: tenant-cfg07-pss-two-tenant.yaml
# T1: e2e-pss-t1, T2 OIDs 5.1/5.2/5.3
# T2: e2e-pss-t2, T3 OIDs 6.1/6.2/6.3
# Grace = 3 * 1 * 2.0 = 6s for both tenants
# Independence: T1 healthy (tier=3) while T2 evaluate violated (tier=4)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

log_info "PSS-11: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-11: Applying 2-tenant PSS fixture..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg07-pss-two-tenant.yaml" > /dev/null 2>&1 || true

log_info "PSS-11: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-11: Tenant vector reload confirmed"
else
    log_warn "PSS-11: Tenant vector reload not detected within 60s; proceeding"
fi

# Prime both tenants to in-range (passes readiness grace)
log_info "PSS-11: Priming T1 (5.x) and T2 (6.x) OIDs with in-range values..."
sim_set_oid "5.1" "10"   # T1 eval in-range
sim_set_oid "5.2" "1"    # T1 res1 in-range
sim_set_oid "5.3" "1"    # T1 res2 in-range
sim_set_oid "6.1" "10"   # T2 eval in-range
sim_set_oid "6.2" "1"    # T2 res1 in-range
sim_set_oid "6.3" "1"    # T2 res2 in-range

log_info "PSS-11: Waiting 8s for readiness grace (6s + 2s margin)..."
sleep 8

# Set T2 evaluate to violated; T1 stays healthy
log_info "PSS-11: Setting T2 evaluate to violated (6.1=0); T1 unchanged (in-range)..."
sim_set_oid "6.1" "0"    # T2 eval violated (< Min:10)

# PSS-11A: T1 reaches tier=3 (Healthy -- evaluate not violated)
log_info "PSS-11: Polling for e2e-pss-t1 tier=3 Healthy (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-t1.*tier=3" 15; then
    record_pass "PSS-11A: e2e-pss-t1 tier=3 Healthy (T1 independent of T2 violated)" "log=t1_tier3_found"
else
    record_fail "PSS-11A: e2e-pss-t1 tier=3 Healthy (T1 independent of T2 violated)" "tier=3 log for e2e-pss-t1 not found within 30s"
fi

# PSS-11B: T2 reaches tier=4 (Unresolved -- evaluate violated, commands dispatched)
log_info "PSS-11: Polling for e2e-pss-t2 tier=4 commands enqueued (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-t2.*tier=4 — commands enqueued\|e2e-pss-t2.*tier=4 -- commands enqueued" 15; then
    record_pass "PSS-11B: e2e-pss-t2 tier=4 Unresolved (T2 evaluate violated, commands dispatched)" "log=t2_tier4_found"
else
    record_fail "PSS-11B: e2e-pss-t2 tier=4 Unresolved (T2 evaluate violated, commands dispatched)" "tier=4 log for e2e-pss-t2 not found within 30s"
fi

# PSS-11C: T1 has no tier=4 (Healthy = no command dispatch by T1)
# Proven by tier=3 log (PSS-11A). Optionally verify absence of T1 tier=4:
# [use pod log scan absence check if desired -- see PSS-03C pattern from scenario 55]

# Cleanup
log_info "PSS-11: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-11: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-11: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-11: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
```

### PSS-12: T1=Resolved + T2=Healthy (Source: verified pattern from Phase 62 + SNS-A3)

```bash
# After priming and 8s sleep:
# T1: set res1=0, res2=0 (both violated < Min:1) -> tier=2 Resolved
# T2: stays in-range -> tier=3 Healthy
sim_set_oid "5.2" "0"    # T1 res1 violated
sim_set_oid "5.3" "0"    # T1 res2 violated
# T2 OIDs (6.x) remain at primed in-range values

# PSS-12A: T1 tier=2 (Resolved)
poll_until_log 30 1 "e2e-pss-t1.*tier=2" 15

# PSS-12B: T2 tier=3 (Healthy, independent of T1 Resolved)
poll_until_log 30 1 "e2e-pss-t2.*tier=3" 15
```

### PSS-13: Both Unresolved (Source: Phase 62 PSS-04 pattern doubled)

```bash
# After priming and 8s sleep:
# Both T1 and T2 evaluate violated, resolved in-range
sim_set_oid "5.1" "0"    # T1 eval violated
sim_set_oid "6.1" "0"    # T2 eval violated

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')

# PSS-13A: T1 tier=4
poll_until_log 30 1 "e2e-pss-t1.*tier=4 — commands enqueued\|e2e-pss-t1.*tier=4 -- commands enqueued" 15

# PSS-13B: T2 tier=4
poll_until_log 30 1 "e2e-pss-t2.*tier=4 — commands enqueued\|e2e-pss-t2.*tier=4 -- commands enqueued" 15

# PSS-13C: Counter increments >= 2 (both tenants dispatch)
if poll_until 30 2 "snmp_command_dispatched_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    # Expect at least 2 (one per tenant)
    if [ "$DELTA_SENT" -ge 2 ]; then
        record_pass "PSS-13C: Both tenants dispatched commands independently" "sent_delta=${DELTA_SENT}"
    else
        record_fail "PSS-13C: Both tenants dispatched commands independently" "sent_delta=${DELTA_SENT} expected >= 2"
    fi
fi
```

### Stage 2 Runner Script (PSS-INF-01 gating)

```bash
#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_DIR="$SCRIPT_DIR/reports"
mkdir -p "$REPORT_DIR"

source "$SCRIPT_DIR/lib/common.sh"
source "$SCRIPT_DIR/lib/prometheus.sh"
source "$SCRIPT_DIR/lib/kubectl.sh"
source "$SCRIPT_DIR/lib/report.sh"
source "$SCRIPT_DIR/lib/sim.sh"

cleanup() { stop_port_forwards; }
trap cleanup EXIT

start_port_forward prometheus 9090 9090
start_port_forward e2e-simulator 8080 8080

# Pre-flight checks (same as run-all.sh)

# Stage 1: Single Tenant (scenarios 53-58)
echo "=== Stage 1: Single Tenant Evaluation States ==="
for scenario in "$SCRIPT_DIR"/scenarios/5[3-8]-pss-*.sh; do
    [ -f "$scenario" ] && { log_info "Running: $(basename "$scenario")"; source "$scenario"; echo ""; }
done

# Gate: check Stage 1 failures
if [ "$FAIL_COUNT" -gt 0 ]; then
    log_error "Stage 1 had ${FAIL_COUNT} failure(s) -- Stage 2 will not run"
    REPORT_FILE="$REPORT_DIR/pss-stage2-$(date '+%Y%m%d-%H%M%S').md"
    generate_report "$REPORT_FILE"
    print_summary
    exit 1
fi

log_info "Stage 1 passed (0 failures) -- proceeding to Stage 2"

# Stage 2: Two Tenant Independence (scenarios 59-61)
echo "=== Stage 2: Two Tenant Independence ==="
for scenario in "$SCRIPT_DIR"/scenarios/59-pss-11-*.sh \
                "$SCRIPT_DIR"/scenarios/60-pss-12-*.sh \
                "$SCRIPT_DIR"/scenarios/61-pss-13-*.sh; do
    [ -f "$scenario" ] && { log_info "Running: $(basename "$scenario")"; source "$scenario"; echo ""; }
done

REPORT_FILE="$REPORT_DIR/pss-stage2-$(date '+%Y%m%d-%H%M%S').md"
generate_report "$REPORT_FILE"
log_info "Report saved to: $REPORT_FILE"
print_summary
[ "$FAIL_COUNT" -eq 0 ]
```

## State of the Art

| Old Approach (Phase 62 PSS) | New Approach (Phase 63 PSS) | Impact |
|-----------------------------|-----------------------------|--------|
| 1-tenant fixture (cfg06) with 3 OIDs | 2-tenant fixture (cfg07) with 6 OIDs (3 per tenant) | Proves independence via separate OID namespaces |
| Scenarios prove single-tenant outcomes | Scenarios prove cross-tenant isolation | Higher confidence in SnapshotJob tenant independence |
| run-all.sh runs all scenarios unconditionally | run-stage2.sh gates Stage 2 on Stage 1 FAIL_COUNT | Progressive gating prevents misleading Stage 2 results when Stage 1 is broken |
| Counter assertions track single tenant | PSS-13 counter delta >= 2 for both-Unresolved | Counter-level evidence both tenants dispatched |

## Open Questions

1. **Fixture naming convention (cfg07 vs cfg06-pss-two-tenant)**
   - What we know: cfg01-cfg06 follow sequential numbering. cfg06 has two variants (pss-single, pss-suppression).
   - What's unclear: Whether to use cfg07-pss-two-tenant or cfg06-pss-two-tenant naming.
   - Recommendation: Use `tenant-cfg07-pss-two-tenant.yaml` to keep sequential config numbering consistent with the established pattern.

2. **PSS-11 T1-no-commands assertion approach**
   - What we know: Tier=3 log proves T1 did not dispatch commands (by definition of tier=3). Counter-based approach is ambiguous since both tenants share `device_name="E2E-SIM"` label.
   - What's unclear: Whether the plan should add an explicit tier=4 absence check for T1 (as PSS-03C does for tier=2 absence).
   - Recommendation: Assert tier=3 for T1 (PSS-11A) as primary evidence. Optionally add absence check for T1 tier=4 as belt-and-suspenders. The tier=3 assertion is sufficient per the success criteria wording ("T1 has no commands dispatched while T2 does" -- proven by T1=tier=3 and T2=tier=4).

3. **run-stage2.sh scope -- include earlier scenarios or PSS-only**
   - What we know: run-all.sh runs scenarios 1-58 unconditionally. PSS-INF-01 says "Stage 2 runner checks FAIL_COUNT from Stage 1". Stage 1 = PSS scenarios 53-58.
   - What's unclear: Should run-stage2.sh start from scenario 01 (full suite with gate) or from scenario 53 (PSS-only with gate)?
   - Recommendation: run-stage2.sh runs PSS only (scenarios 53-61) with gate after 53-58. This avoids entangling non-PSS failures with PSS stage gating. run-all.sh remains the full suite runner.

4. **PSS-13 counter delta minimum value**
   - What we know: With both tenants dispatching and SuppressionWindowSeconds=10, the first SnapshotJob cycle dispatches one command per tenant. Multiple cycles fire within the observation window.
   - What's unclear: Whether delta >= 2 is the right threshold or if each tenant always dispatches exactly 1 command per cycle (may dispatch multiple if the poll_until waits several cycles).
   - Recommendation: Use `>= 2` as the assertion threshold (minimum one dispatch per tenant). Log the actual delta for evidence.

## Sources

### Primary (HIGH confidence)
- **tests/e2e/lib/common.sh** -- Read full source. FAIL_COUNT global, record_pass, record_fail confirmed.
- **tests/e2e/lib/sim.sh** -- Read full source. sim_set_oid("5.x"), sim_set_oid("6.x") format confirmed.
- **tests/e2e/lib/report.sh** -- Read full source. _REPORT_CATEGORIES array, current range "52-57" confirmed.
- **tests/e2e/fixtures/tenant-cfg05-four-tenant-snapshot.yaml** -- Read full source. e2e_eval_T3/e2e_res1_T3/e2e_res2_T3 metric names confirmed. T3 OIDs at Priority=1 in same group as T2 confirmed.
- **tests/e2e/fixtures/tenant-cfg06-pss-single.yaml** -- Read full source. Single-tenant PSS fixture structure confirmed.
- **tests/e2e/scenarios/46-sns-a1-both-resolved.sh** -- Read full source. Two-tenant independence assertion pattern (separate tenant log grep) confirmed.
- **tests/e2e/scenarios/56-pss-04-unresolved.sh** -- Read full source. PSS-11B pattern source. snmp_command_dispatched_total counter name confirmed.
- **tests/e2e/scenarios/57-pss-05-healthy.sh** -- Read full source. PSS-11A pattern source.
- **tests/e2e/run-all.sh** -- Read full source. FAIL_COUNT check at line 109. No current stage gating.
- **tests/e2e/fixtures/.original-oid-metric-map-configmap.yaml** -- Confirmed .999.6.x -> e2e_eval_T3/e2e_res1_T3/e2e_res2_T3 mapping.
- **.planning/phases/62-single-tenant-evaluation-states/62-RESEARCH.md** -- Full Phase 62 research including OID poll group analysis.
- **.planning/phases/62-single-tenant-evaluation-states/62-VERIFICATION.md** -- Phase 62 complete, all 7 truths verified.

### Secondary (MEDIUM confidence)
- **.planning/PROJECT.md and ROADMAP.md** -- v2.2 milestone description, stage gating description, Phase 63 success criteria.
- **.planning/STATE.md** -- Key facts (1s SnapshotJob interval, grace = 6s) confirmed consistent with Phase 62 research.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all lib scripts read directly, counter names verified from actual Phase 62 scenario files
- Architecture patterns: HIGH -- 2-tenant fixture design verified against cfg05 (which already uses T2+T3 OIDs at same Priority), independence assertion pattern verified from SNS-A1
- Stage gating pattern: HIGH -- FAIL_COUNT global confirmed in common.sh; run-all.sh structure confirmed; PSS-INF-01 requirement clearly specified
- Counter ambiguity (PSS-11/12 T1-no-commands): HIGH -- confirmed via source code analysis that snmp_command_dispatched_total uses device_name not tenant_id
- Pitfalls: HIGH -- derived from direct code analysis and Phase 62 established patterns

**Research date:** 2026-03-20
**Valid until:** 2026-04-20 (stable infrastructure, no planned changes to lib scripts or OID map)
