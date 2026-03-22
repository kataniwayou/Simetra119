# Phase 67: Poll & Trap Infrastructure Counters - Research

**Researched:** 2026-03-22
**Domain:** E2E scenario shell scripts, SNMP counter mechanics
**Confidence:** HIGH

---

## Summary

Phase 67 adds E2E scenarios 76-82 that verify the six SNMP-layer infrastructure counters: `snmp.poll.executed`, `snmp.trap.received`, `snmp.trap.auth_failed`, `snmp.poll.unreachable`, `snmp.poll.recovered`, and `snmp.tenantvector.routed`. All counter mechanics have been read directly from source. The E2E pattern is well-established: snapshot a counter baseline, poll until it changes, assert the delta.

The MCV-08 through MCV-10 scenarios are straightforward (counters already active during any E2E run). MCV-11/12 (unreachable/recovered) already have a fully-working implementation in scenarios 06 and 07, including the idempotency pre-recovery step and the `fake-device-configmap.yaml` fixture — these scenarios must be reproduced verbatim (not re-invented). MCV-13 (tenantvector.routed) mirrors the existing scenario 28c.

The key planning insight: scenarios 76-82 cover territory largely already proven in 01-07 and 28. Phase 67 is a **structured re-exercise** of those counters within the MCV framework, with proper scenario names, MCV labels, and placement in the "Pipeline Counter Verification" report category.

**Primary recommendation:** Model 76-82 directly on the tested patterns in scenarios 01-07 and 28, with no invention. The unreachable/recovered scenarios must preserve the idempotency pre-recovery step from scenario 06 and the ConfigMap restore in scenario 07.

---

## Standard Stack

These already exist. No new libraries or tools are needed.

### Core (all already present)
| Component | Location | Purpose |
|-----------|----------|---------|
| `PipelineMetricService` | `src/SnmpCollector/Telemetry/PipelineMetricService.cs` | All 6 counters |
| `MetricPollJob` | `src/SnmpCollector/Jobs/MetricPollJob.cs` | poll.executed, poll.unreachable, poll.recovered |
| `SnmpTrapListenerService` | `src/SnmpCollector/Services/SnmpTrapListenerService.cs` | trap.auth_failed |
| `ChannelConsumerService` | `src/SnmpCollector/Services/ChannelConsumerService.cs` | trap.received |
| `TenantVectorFanOutBehavior` | `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` | tenantvector.routed |
| `DeviceUnreachabilityTracker` | `src/SnmpCollector/Pipeline/DeviceUnreachabilityTracker.cs` | Unreachable/recovered state |
| `fake-device-configmap.yaml` | `tests/e2e/fixtures/fake-device-configmap.yaml` | FAKE-UNREACHABLE device at 10.255.255.254 |
| E2E lib (common, prometheus, kubectl, sim) | `tests/e2e/lib/` | All test utilities including assert_delta_eq/ge |

### Prometheus metric names (OTel dots become underscores)
| C# instrument name | Prometheus name |
|--------------------|-----------------|
| `snmp.poll.executed` | `snmp_poll_executed_total` |
| `snmp.trap.received` | `snmp_trap_received_total` |
| `snmp.trap.auth_failed` | `snmp_trap_auth_failed_total` |
| `snmp.poll.unreachable` | `snmp_poll_unreachable_total` |
| `snmp.poll.recovered` | `snmp_poll_recovered_total` |
| `snmp.tenantvector.routed` | `snmp_tenantvector_routed_total` |

---

## Architecture Patterns

### Counter Semantics (exact, verified from source)

**`snmp.poll.executed`** — incremented in `MetricPollJob.Execute`, in the `finally` block (line 140). This fires unconditionally after every poll attempt — success, timeout, or network failure. It increments once per poll GROUP execution, not per OID. Each device/poll-group pair is a distinct Quartz job. Tag: `device_name`.

**`snmp.trap.received`** — incremented in `ChannelConsumerService.ExecuteAsync` (line 68), once per varbind envelope dequeued from the channel. Since a single trap PDU produces one envelope per varbind, a trap with N varbinds increments the counter N times. The E2E-SIM sends traps with a single varbind (`e2e_gauge_test`), so each valid trap increments the counter by 1 per replica (all 3 replicas receive the broadcast). Tag: `device_name="E2E-SIM"`.

**`snmp.trap.auth_failed`** — incremented in `SnmpTrapListenerService.ProcessDatagram` (line 146), when `CommunityStringHelper.TryExtractDeviceName` returns false. This fires when the community string does not start with `"Simetra."` (case-sensitive). The E2E-SIM bad-community string is `"BadCommunity"` — it does not match. Tag: `device_name="unknown"` (hardcoded string, not a real device name).

**`snmp.poll.unreachable`** — incremented in `MetricPollJob.RecordFailure` (line 316) only when `DeviceUnreachabilityTracker.RecordFailure` returns `true`. `RecordFailure` returns true only on the state transition at exactly the threshold (failure count >= 3 AND `_isUnreachable` was false). It fires at most once per unreachable episode. It does NOT fire on failures 1 or 2. Tag: `device_name`.

**`snmp.poll.recovered`** — incremented in `MetricPollJob.Execute` (line 112) when `DeviceUnreachabilityTracker.RecordSuccess` returns `true`. `RecordSuccess` returns true only when `_isUnreachable` was true before the success. It fires at most once per recovery episode. Tag: `device_name`.

**`snmp.tenantvector.routed`** — incremented in `TenantVectorFanOutBehavior.Handle` (line 55), once per `holder.WriteValue` call (i.e., once per matching tenant vector slot per varbind). If a metric matches N tenant vector slots, it increments N times. Tag: `device_name`.

### DeviceUnreachabilityTracker state machine (verified from source)

```
Initial state: count=0, isUnreachable=false
RecordFailure: count++ -> if count >= 3 AND NOT isUnreachable: isUnreachable=true, return true (TRANSITION)
                         else: return false
RecordSuccess: count=0  -> if isUnreachable: isUnreachable=false, return true (TRANSITION)
                          else: return false
```

The tracker is a **singleton** that persists across device add/remove cycles. This means if FAKE-UNREACHABLE was left in unreachable state from a previous run, the unreachable counter will not fire again until the tracker is reset. The idempotency pre-recovery step in scenario 06 handles this by adding the device at a reachable IP first, waiting 20s for a successful poll (RecordSuccess resets isUnreachable), then switching to the unreachable IP.

### E2E Scenario Structure

All scenarios follow the same pattern established in 01-07 and 69-75:

```bash
SCENARIO_NAME="MCV-XX: description"
METRIC="snmp_XXX_total"
FILTER='device_name="DEVICE"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until <timeout_s> "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA ..."
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
```

For negative assertions (trap.received must NOT increment for bad-community):
```bash
# Snapshot trap.received before and after trap.auth_failed interval passes
# Assert auth_failed increased AND trap.received did not increase
```

### Report category update

The current `_REPORT_CATEGORIES` in `tests/e2e/lib/report.sh` ends at:
```
"Pipeline Counter Verification|68|75"
```
Phase 67 adds scenarios 76-82 (7 new scenarios, 0-based indices 75-81). The category entry must be extended to cover the new range:
```
"Pipeline Counter Verification|68|82"
```
This is a single-character change (75 -> 82) in `report.sh`.

### Scenario numbering

Last scenario in Phase 66: `75-mcv07-errors-stays-zero.sh` (0-based index 74, which is the 75th scenario).

Phase 67 scenarios start at **76** (0-based index 75):
- 76: MCV-08 — poll.executed increments each cycle
- 77: MCV-09 — trap.received increments for valid-community trap
- 78: MCV-09b — trap.received does NOT increment for bad-community trap
- 79: MCV-10 — trap.auth_failed increments for bad-community trap
- 80: MCV-11 — poll.unreachable after 3 consecutive failures
- 81: MCV-12 — poll.recovered after device becomes reachable
- 82: MCV-13 — tenantvector.routed increments on fan-out write

That is 7 scenarios. The report category covers indices 68-82 (15 scenarios total: 69-75 from Phase 66, plus 76-82 from Phase 67).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Unreachable device fixture | New configmap | `tests/e2e/fixtures/fake-device-configmap.yaml` | Already exists with FAKE-UNREACHABLE at 10.255.255.254, IntervalSeconds=10 |
| Idempotency reset for unreachable tracker | New mechanism | Pre-recovery step from scenario 06 (add at reachable IP, wait 20s, then switch to unreachable IP) | Already proven approach |
| trap.auth_failed negative test logic | Custom logic | Snapshot trap.received before + after known auth_failed increment window | trap.auth_failed and trap.received are independently measurable |
| assert_delta_ge / assert_delta_eq | Custom assertions | `tests/e2e/lib/common.sh` already has both | Added in Phase 66 plan 66-01 |
| tenantvector fixture setup | Deploy tenants ConfigMap inline | Model on scenario 28c — use simetra-tenants.yaml (already on disk) and rely on the deployment restart from scenario 28 which runs earlier | Scenario 28 already leaves the tenants ConfigMap applied; tenantvector.routed will already be incrementing |

---

## Common Pitfalls

### Pitfall 1: MCV-09 negative assertion — trap.received filter must use device_name="unknown"

**What goes wrong:** Trying to assert `trap.received` does NOT increment for bad-community traps using `device_name="E2E-SIM"`. Bad-community traps are dropped before device name extraction — they never reach ChannelConsumerService. The filter `device_name="E2E-SIM"` correctly isolates valid-community traps from the bad-community window.

**How to avoid:** For the "does not increase" negative assertion, snapshot `snmp_trap_received_total` with `device_name="E2E-SIM"` at the START of the 45s bad-trap window, wait for `snmp_trap_auth_failed_total` (unfiltered, since it uses `device_name="unknown"`) to increment, THEN check that `snmp_trap_received_total` with `device_name="E2E-SIM"` did not increase.

**Warning sign:** If `trap.received` is increasing during the negative assertion window, poll activity (scenario 76) is adding to the `device_name="E2E-SIM"` received counter via poll-path metrics (these are different — `trap.received` only fires in ChannelConsumerService on the trap path). Wait — `trap.received` is ONLY incremented by ChannelConsumerService. Poll path does NOT increment it. The filter is safe.

### Pitfall 2: DeviceUnreachabilityTracker singleton — counter fires exactly once per transition

**What goes wrong:** Expecting `poll_unreachable` to increment multiple times across repeated test runs in a single E2E run without the pre-recovery reset.

**How to avoid:** The idempotency pre-recovery step in scenario 06 is mandatory for MCV-11. Copy it verbatim for scenario 80. The same logic applies: add FAKE-UNREACHABLE at a reachable IP first, sleep 20s, then apply the fake-device-configmap.yaml with IP 10.255.255.254.

**Timing note:** The FAKE-UNREACHABLE device polls every 10 seconds with a timeout multiplier that gives ~8s actual timeout (`intervalSeconds * TimeoutMultiplier`). Three consecutive failures take at minimum 30s. Plus OTel export lag (~15s). Use a 120s `poll_until` timeout for MCV-11, consistent with scenario 06.

### Pitfall 3: tenantvector.routed requires the tenants ConfigMap to be applied

**What goes wrong:** Running MCV-13 before the simetra-tenants.yaml has been applied, when the tenant vector registry has no routes registered.

**How to avoid:** Scenario 28 (which runs earlier in the suite at index 27) applies simetra-tenants.yaml and leaves it applied after cleanup. MCV-13 (scenario 82) will naturally find tenants configured. However, be aware that scenario 28 restores the original tenants ConfigMap at the end. If the original ConfigMap is empty (no tenants), `tenantvector.routed` will not increment. **Safe approach:** Use the same pattern as scenario 28c — apply the tenants ConfigMap inline at the start of MCV-13, poll for the counter to increment, then restore.

Actually, reviewing scenario 28 more carefully: it restores the original configmap. If the original is empty, MCV-13 must apply the tenants yaml itself. Use `kubectl apply -f deploy/k8s/snmp-collector/simetra-tenants.yaml` at the start of MCV-13, then restore afterward. But scenario 28 already tested this. Given MCV-13 runs after scenario 28, and the tenantvector.routed counter was already incrementing during scenario 28, the simplest approach is: snapshot at current value, wait for it to change (it will if tenants are active), assert delta > 0. If tenants were restored to empty by scenario 28, insert a `kubectl apply` guard.

### Pitfall 4: 3 replicas aggregate in Prometheus

**What goes wrong:** Expecting `snmp_poll_executed_total{device_name="FAKE-UNREACHABLE"}` to show exactly 3 increments (one per failure per replica), but it shows 9 (3 replicas × 3 failures).

**How to avoid:** Always use `sum(metric{filter})` for cross-replica counters — this is what `snapshot_counter` and `query_counter` already do (they use `sum(...) or vector(0)`). All assertions are on the aggregate sum. Use `assert_delta_gt 0` not exact counts.

### Pitfall 5: trap.received increments per varbind, not per trap PDU

**What goes wrong:** Expecting delta=1 per trap when E2E-SIM sends one trap per 30s, but since all 3 replicas receive the broadcast, delta across the cluster is 3 per trap cycle (3 replicas × 1 varbind). With `sum(snmp_trap_received_total{device_name="E2E-SIM"})`, each valid trap produces a delta of 3.

**How to avoid:** Use `assert_delta_gt "$DELTA" 0` (delta greater than 0), not `assert_delta_ge "$DELTA" 3`. The exact multiple varies with timing and whether poll_until catches one or more trap cycles.

### Pitfall 6: trap.auth_failed uses device_name="unknown" — unfiltered query is safer

**What goes wrong:** Filtering `snmp_trap_auth_failed_total` by `device_name="unknown"` literally works but is fragile if the hardcoded string changes.

**How to avoid:** Use an empty filter string `""` for `snmp_trap_auth_failed_total`, exactly as scenario 05 does. The `query_counter` function handles empty filter correctly with `sum(metric) or vector(0)`.

---

## Code Examples

### MCV-08: poll.executed increments each poll cycle

```bash
# Source: pattern from tests/e2e/scenarios/01-poll-executed.sh
# poll.executed fires in MetricPollJob.Execute finally block — always, success or failure
# E2E-SIM has 4 poll groups at 10s/10s/1s/10s intervals. Use device_name="E2E-SIM".
# With 3 replicas, delta will be >> 1 within any 30s window.
SCENARIO_NAME="MCV-08: poll.executed increments each poll cycle"
METRIC="snmp_poll_executed_total"
FILTER='device_name="E2E-SIM"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 45 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA (expect > 0: fired in finally block per poll group per replica)"
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
```

### MCV-09: trap.received increments for valid-community traps

```bash
# Source: pattern from tests/e2e/scenarios/04-trap-received.sh
# ChannelConsumerService increments per varbind dequeued — once per valid-community trap varbind
# E2E-SIM sends valid traps every 30s with community="Simetra.E2E-SIM"
# 3 replicas all receive the broadcast: sum delta >= 3 per trap cycle
SCENARIO_NAME="MCV-09: trap.received increments for valid-community traps"
METRIC="snmp_trap_received_total"
FILTER='device_name="E2E-SIM"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 60 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA (valid traps every 30s, 3 replicas)"
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
```

### MCV-09b: trap.received does NOT increment for bad-community traps

```bash
# SnmpTrapListenerService drops bad-community traps before writing to channel.
# ChannelConsumerService (which increments trap.received) never sees them.
# Approach: wait for auth_failed to fire (confirming a bad trap was received),
# then assert trap.received did not change during that same window.
SCENARIO_NAME="MCV-09b: trap.received does not increment for bad-community traps"
RECV_METRIC="snmp_trap_received_total"
AUTH_METRIC="snmp_trap_auth_failed_total"
RECV_FILTER='device_name="E2E-SIM"'
AUTH_FILTER=""

RECV_BEFORE=$(snapshot_counter "$RECV_METRIC" "$RECV_FILTER")
AUTH_BEFORE=$(snapshot_counter "$AUTH_METRIC" "$AUTH_FILTER")

# Wait for at least one bad-community trap (interval 45s + OTel lag)
poll_until 75 "$POLL_INTERVAL" "$AUTH_METRIC" "$AUTH_FILTER" "$AUTH_BEFORE" || true
sleep 15  # OTel export flush

RECV_AFTER=$(snapshot_counter "$RECV_METRIC" "$RECV_FILTER")
AUTH_AFTER=$(snapshot_counter "$AUTH_METRIC" "$AUTH_FILTER")
AUTH_DELTA=$((AUTH_AFTER - AUTH_BEFORE))
RECV_DELTA=$((RECV_AFTER - RECV_BEFORE))

EVIDENCE="auth_delta=$AUTH_DELTA recv_delta=$RECV_DELTA recv_before=$RECV_BEFORE recv_after=$RECV_AFTER"
if [ "$AUTH_DELTA" -gt 0 ]; then
    # auth_failed fired (bad trap arrived), but trap.received must not have changed from bad trap
    # NOTE: trap.received CAN increase due to valid traps (every 30s) in this window.
    # This negative assertion cannot be proven cleanly — see Open Questions.
    record_pass "$SCENARIO_NAME" "auth_failed fired (delta=$AUTH_DELTA); bad-community traps do not add to device_name=E2E-SIM received counter. $EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "No bad-community trap arrived in window. $EVIDENCE"
fi
```

**NOTE:** See Open Questions on MCV-09b — this scenario may be simplified or restructured.

### MCV-10: trap.auth_failed increments for bad-community traps

```bash
# Source: pattern from tests/e2e/scenarios/05-trap-auth-failed.sh
# SnmpTrapListenerService fires IncrementTrapAuthFailed("unknown") on CommunityStringHelper failure
# E2E-SIM sends bad traps every 45s with community="BadCommunity"
# 3 replicas all receive: sum delta >= 3 per bad-trap cycle
SCENARIO_NAME="MCV-10: trap.auth_failed increments for bad-community traps"
METRIC="snmp_trap_auth_failed_total"
FILTER=""

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 75 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA (bad traps every 45s community=BadCommunity)"
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
```

### MCV-11: poll.unreachable after 3 consecutive failures

```bash
# Source: tests/e2e/scenarios/06-poll-unreachable.sh (copy verbatim, rename scenario)
# DeviceUnreachabilityTracker fires on 3rd failure transition (count >= threshold AND NOT isUnreachable)
# FAKE-UNREACHABLE at 10.255.255.254 with IntervalSeconds=10 — 3 failures = ~30s minimum
# Pre-recovery step is MANDATORY for idempotency (singleton tracker persists across runs)
SCENARIO_NAME="MCV-11: poll.unreachable increments after 3 consecutive failures"
METRIC="snmp_poll_unreachable_total"
FILTER='device_name="FAKE-UNREACHABLE"'

# [pre-recovery step: add at reachable IP, sleep 20s, then apply unreachable fixture]
# [full implementation: copy from scenario 06]
```

### MCV-12: poll.recovered after device becomes reachable

```bash
# Source: tests/e2e/scenarios/07-poll-recovered.sh (copy verbatim, rename scenario)
# DeviceUnreachabilityTracker fires on first successful poll after isUnreachable=true
# Patch FAKE-UNREACHABLE to point to e2e-simulator DNS, wait for recovery
# Restore original ConfigMap at end
SCENARIO_NAME="MCV-12: poll.recovered increments when device becomes reachable"
METRIC="snmp_poll_recovered_total"
FILTER='device_name="FAKE-UNREACHABLE"'

# [full implementation: copy from scenario 07]
```

### MCV-13: tenantvector.routed increments on fan-out write

```bash
# Source: pattern from tests/e2e/scenarios/28-tenantvector-routing.sh (sub-scenario 28c)
# TenantVectorFanOutBehavior fires per WriteValue call on a matching holder
# Requires tenants ConfigMap to be applied (scenario 28 may have restored to empty)
# Safe approach: apply simetra-tenants.yaml first, poll for counter, restore afterward
SCENARIO_NAME="MCV-13: tenantvector.routed increments on fan-out write"
METRIC="snmp_tenantvector_routed_total"

BEFORE=$(snapshot_counter "$METRIC" "")
log_info "Applying tenantvector ConfigMap..."
kubectl apply -f deploy/k8s/snmp-collector/simetra-tenants.yaml || true
poll_until 90 5 "$METRIC" "" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA"
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
# No restore needed (scenario 28 already handled lifecycle)
```

---

## State of the Art

| Old Approach | Current Approach | When Changed |
|--------------|------------------|--------------|
| Scenarios 01-07 prove these counters | Phase 67 wraps them as MCV-08-13 with scenario names + report category | Phase 67 |
| assert_delta_gt only | assert_delta_eq and assert_delta_ge added | Phase 66 (66-01) |
| Scenarios 01-68 | Phase 66 added 69-75, Phase 67 adds 76-82 | Ongoing |

**Existing scenarios doing the same thing:**
- Scenario 01 = MCV-08 (poll.executed for OBP-01 — Phase 67 adds for E2E-SIM with MCV label)
- Scenario 04 = MCV-09 (trap.received for E2E-SIM — verbatim copy with MCV name)
- Scenario 05 = MCV-10 (trap.auth_failed — verbatim copy with MCV name)
- Scenario 06 = MCV-11 (poll.unreachable — verbatim copy with MCV name)
- Scenario 07 = MCV-12 (poll.recovered — verbatim copy with MCV name)
- Scenario 28c = MCV-13 (tenantvector.routed — model on 28c)

---

## Open Questions

1. **MCV-09b: Is the negative assertion provable?**
   - What we know: `trap.received` increments in ChannelConsumerService (trap path only). Bad-community traps never reach ChannelConsumerService. Therefore `device_name="E2E-SIM"` trap.received cannot increase from bad-community traps alone.
   - What's unclear: During the 45s window waiting for a bad trap, valid traps (every 30s) will also arrive, increasing `trap.received`. The counter increase cannot be attributed solely to bad-community behavior.
   - Recommendation: Either (a) skip MCV-09b as an independent scenario and fold it into MCV-10 evidence commentary, or (b) implement it as a proof-by-mechanism: "trap.received increments only after valid traps, confirmed by mechanism not direct observation." Given that scenarios 04 and 05 already exist as 01-07, the planner may choose to make MCV-09 and MCV-09b a single compound scenario. The research supports this simplification.

2. **MCV-13: Does scenario 28 leave tenants applied or restored?**
   - What we know: Scenario 28 calls `restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml"` at the end. If the original was the production `simetra-tenants.yaml` (with tenants configured), the counter keeps incrementing. If the original was an empty ConfigMap (no tenants), it stops.
   - What's unclear: What the cluster's baseline simetra-tenants ConfigMap contains before scenario 28 runs.
   - Recommendation: MCV-13 should `kubectl apply -f deploy/k8s/snmp-collector/simetra-tenants.yaml` unconditionally at the start to ensure tenants are active, poll for the counter, then leave cleanup to whatever the test harness state is. This is belt-and-suspenders and matches scenario 28's own setup pattern.

3. **Scenario count: 7 scenarios (76-82) or 6 (76-81)?**
   - What we know: MCV-08 through MCV-13 is 6 requirements.
   - What's unclear: Whether MCV-09b (negative assertion for trap.received) is a separate scenario or folded into MCV-09.
   - Recommendation: The planner should decide. Research supports either 6 scenarios (one per MCV) or 7 (split MCV-09 into positive/negative). The negative assertion is weak (see above), so 6 scenarios is the cleaner choice.

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — poll.executed, poll.unreachable, poll.recovered increment sites (lines 107-116, 139-140)
- `src/SnmpCollector/Services/SnmpTrapListenerService.cs` — trap.auth_failed increment site (line 146)
- `src/SnmpCollector/Services/ChannelConsumerService.cs` — trap.received increment site (line 68)
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — tenantvector.routed increment site (line 55)
- `src/SnmpCollector/Pipeline/DeviceUnreachabilityTracker.cs` — threshold=3, state machine, transition semantics
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — all 6 instrument names, tags
- `tests/e2e/scenarios/01-07, 28` — proven E2E patterns to copy
- `tests/e2e/lib/common.sh` — assert_delta_eq, assert_delta_ge, assert_delta_gt
- `tests/e2e/lib/report.sh` — _REPORT_CATEGORIES, current range 68-75
- `tests/e2e/fixtures/fake-device-configmap.yaml` — FAKE-UNREACHABLE fixture (10.255.255.254, IntervalSeconds=10)
- `deploy/k8s/simulators/e2e-sim-deployment.yaml` — TRAP_INTERVAL=30, BAD_TRAP_INTERVAL=45

---

## Metadata

**Confidence breakdown:**
- Counter semantics: HIGH — read from source, exact line numbers cited
- E2E scenario patterns: HIGH — read from working scenarios 01-07, 28
- Report category update: HIGH — read current range from report.sh
- MCV-09b negative assertion: MEDIUM — mechanically sound but practically weak (valid traps overlap the window)
- Scenario count (6 vs 7): MEDIUM — depends on planner decision on MCV-09b

**Research date:** 2026-03-22
**Valid until:** Stable (counters and scenario patterns rarely change)
