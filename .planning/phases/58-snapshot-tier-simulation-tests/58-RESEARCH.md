# Phase 58: SnapshotJob Tier Simulation Tests - Research

**Researched:** 2026-03-19
**Domain:** E2E scenario scripting for SnapshotJob 4-tier evaluation paths
**Confidence:** HIGH

## Summary

Phase 58 adds E2E scenario scripts that verify every SnapshotJob tier path by source type. The research reveals three critical findings that directly shape planning:

**First**, two existing scenarios are outdated because quick tasks 076 and earlier changed SnapshotJob behavior and log messages after the scenarios were written. Scenario 31 (STS-03) polls for the obsolete log pattern `"tier=2 — all resolved violated, device confirmed bad"` (pre-quick-076 text); the current code logs `"tier=2 — all resolved violated, no commands"`. Scenario 33 (STS-05) asserts that stale data produces zero commands — this was correct before quick-076, but the fix reversed the behavior so staleness now skips to tier=4 command dispatch. Both scenarios need updating as part of this phase.

**Second**, the source-aware threshold check (Poll/Synthetic checks all time series samples; Trap/Command checks newest sample only) is already implemented and unit-tested, but has no E2E coverage. Testing it in E2E requires scenarios that feed data via Trap or Command source into a tenant's Resolved/Evaluate metric — this currently has no simulator support because Trap-sourced and Command-sourced holders get their `Source` label set at runtime (via ChannelConsumerService and CommandWorkerService respectively), not from tenant config. All existing fixtures use Poll-sourced metrics only.

**Third**, the staleness-to-commands path (success criteria #1) is the most behaviorally significant change from quick-076. A dedicated scenario proving tier=1 stale → tier=4 commands dispatched (counter increments) is the highest-value new script to add. This path is not currently tested in any E2E scenario.

**Primary recommendation:** Plan 4 tasks: (1) fix scenario 31 log pattern, (2) update scenario 33 to assert commands ARE sent when stale, (3) add new staleness-skips-to-commands scenario (38-sts-06), (4) add source-aware threshold scenario (39-src-01) using a new simulator scenario and fixture that populates a Resolved metric via Command response — with `report.sh` category update for the expanded range.

## Standard Stack

### Core

| Component | Version | Purpose | Why Standard |
|-----------|---------|---------|--------------|
| Bash E2E scripts | — | Scenario scripting | Established pattern in phases 53-55 |
| `tests/e2e/lib/sim.sh` | — | Simulator control (sim_set_scenario, reset_scenario, poll_until_log) | All STS/MTS/ADV scripts use these |
| `tests/e2e/lib/prometheus.sh` | — | Counter snapshots and polling (snapshot_counter, poll_until) | All counter assertions use these |
| `tests/e2e/lib/common.sh` | — | record_pass/record_fail/assert_delta_gt | All scenario result tracking |
| `tests/e2e/lib/kubectl.sh` | — | save_configmap/restore_configmap, check_pods_ready | Fixture management |
| `tests/e2e/fixtures/*.yaml` | — | Tenant ConfigMap fixtures | One fixture per scenario family |
| `simulators/e2e-sim/e2e_simulator.py` | — | SNMP simulator with HTTP scenario control | Source for all OID values |

### Supporting

| Component | Purpose | When to Use |
|-----------|---------|-------------|
| `run-all.sh` glob `[0-9]*.sh` | Auto-discovers scenario scripts by numeric prefix | All new scripts must follow `NN-name.sh` naming |
| `report.sh` categories | Maps scenario index ranges to report section names | Must update `"Snapshot Evaluation|28|NN"` end index when adding scripts |

## Architecture Patterns

### Recommended Project Structure

```
tests/e2e/
├── scenarios/
│   ├── 29-sts-01-healthy.sh         # existing
│   ├── 30-sts-02-evaluate-violated.sh   # existing
│   ├── 31-sts-03-resolved-gate.sh       # NEEDS LOG PATTERN FIX
│   ├── 32-sts-04-suppression-window.sh  # existing, no changes
│   ├── 33-sts-05-staleness.sh           # NEEDS BEHAVIOR FIX
│   ├── 34-mts-01-same-priority.sh       # existing
│   ├── 35-mts-02-advance-gate.sh        # existing
│   ├── 36-adv-01-aggregate-evaluate.sh  # existing
│   ├── 37-adv-02-depth3-allsamples.sh   # existing
│   ├── 38-sts-06-stale-to-commands.sh   # NEW — staleness dispatches commands
│   └── 39-src-01-source-aware-threshold.sh  # NEW — Trap/Command newest-only
├── fixtures/
│   ├── tenant-cfg01-single.yaml         # reuse for scenario 38
│   └── tenant-cfg05-source-aware.yaml   # NEW — fixture for scenario 39
└── lib/                                 # no changes
```

### Pattern 1: Setup/Baseline/Assert/Cleanup

Every scenario follows this exact structure (verified in all 9 existing scripts):

```bash
# Setup: save original ConfigMap, apply fixture, wait for reload
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
kubectl apply -f "$FIXTURES_DIR/tenant-cfg-XX.yaml" > /dev/null 2>&1 || true
poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30 || \
    log_warn "Tenant vector reload not detected; proceeding"

# Simulator scenario
sim_set_scenario <name>

# Baseline: snapshot counters BEFORE assertion window
BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')

# Assert: poll for log, then poll_until for counter (never bare snapshot_counter)
if poll_until_log 90 5 "<log pattern>" 60; then
    record_pass "Scenario: description" "log=found"
else
    record_fail "Scenario: description" "log=not_found"
fi

if poll_until 45 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    record_pass "Scenario: sent counter" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    record_fail "Scenario: sent counter" "sent_delta=${DELTA_SENT} after 45s"
fi

# Cleanup: reset simulator, restore ConfigMap
reset_scenario
restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
```

### Pattern 2: Counter polling (CRITICAL)

Never use bare `snapshot_counter` to assert a counter has incremented. Always use `poll_until` first, then snapshot. Reason: SNMP SET is async, OTel export + Prometheus scrape adds ~15-30s latency. Bare snapshot after tier=4 log detection consistently races.

```bash
# CORRECT — poll_until 45s then snapshot
if poll_until 45 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    record_pass ...
else
    AFTER=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    record_fail ...
fi

# WRONG — races against async command dispatch + scrape
AFTER=$(snapshot_counter ...)
if [ "$((AFTER - BEFORE))" -gt 0 ]; then ...
```

### Pattern 3: Tenant name uniqueness per scenario family

Each scenario that uses a distinct fixture must have a unique tenant name (e.g. `e2e-tenant-A`, `e2e-tenant-A-supp`, `e2e-tenant-agg`). This prevents suppression cache bleed between scenarios because the suppression key includes the tenant ID prefix.

### Pattern 4: Priming for staleness scenarios

The `stale` simulator scenario returns `NoSuchInstance` for `.4.1` and `.4.2`. Before switching to `stale`, switch to `healthy` and wait at least 20s so poll timestamps are populated. Without this, `HasStaleness` skips null slots (never polled) and the stale scenario never triggers tier=1.

```bash
sim_set_scenario healthy
log_info "Waiting 20s to populate fresh poll timestamps..."
sleep 20
sim_set_scenario stale
```

### Pattern 5: Timing windows

| Timing factor | Duration | Source |
|---------------|----------|--------|
| Poll interval (devices config) | 10s | MetricPollJob config |
| TimeSeriesSize fill time (size=3) | ~30s (3 polls × 10s) | MetricSlotHolder fill |
| SnapshotJob cycle | 15s | SnapshotJobOptions |
| Staleness grace window | 20s (10s × GraceMultiplier 2.0) | HasStaleness calculation |
| OTel export + Prometheus scrape | 15s | Observation timing |
| Minimum stabilization wait | ~45s | Additional context |

### Anti-Patterns to Avoid

- **Bare snapshot_counter for counter assertions:** Always use `poll_until` first (see Pattern 2).
- **Stale scenario without priming:** Switch to `healthy` first to populate timestamps or staleness never fires.
- **Same tenant name across fixtures:** Suppression cache bleed — each fixture must have a distinct tenant ID.
- **Not resetting simulator before fixture restore:** Always call `reset_scenario` before `restore_configmap` to prevent the new configuration loading into a triggered scenario.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Counter polling | manual sleep + query loop | `poll_until` from prometheus.sh | Handles deadline, interval, baseline comparison |
| Log polling | manual kubectl logs loop | `poll_until_log` from sim.sh | Handles multi-pod, since window, timeout |
| Result tracking | raw echo output | `record_pass`/`record_fail` from common.sh | Feeds `generate_report` and exit code |
| ConfigMap save/restore | raw kubectl | `save_configmap`/`restore_configmap` from kubectl.sh | Strips resourceVersion/uid for clean re-apply |
| Scenario switching | raw curl | `sim_set_scenario`/`reset_scenario` from sim.sh | Error-checks HTTP response code |

## Common Pitfalls

### Pitfall 1: Scenario 31 (STS-03) uses obsolete log pattern

**What goes wrong:** Quick-076 renamed `TierResult.ConfirmedBad` to `TierResult.Violated` and changed the tier=2 log message from `"tier=2 — all resolved violated, device confirmed bad"` to `"tier=2 — all resolved violated, no commands"`. Scenario 31 still polls for the old text — it will reliably fail.

**How to avoid:** Update scenario 31 to poll for `"tier=2 — all resolved violated, no commands"` and update comments/scenario names that mention "ConfirmedBad".

### Pitfall 2: Scenario 33 (STS-05) tests the OLD staleness behavior

**What goes wrong:** Quick-076 changed staleness from "return early, no commands" to "skip to tier 4, dispatch commands". Scenario 33 currently asserts `DELTA_SENT == 0` during staleness — this assertion will now fail because commands ARE dispatched when stale.

**How to avoid:** Update scenario 33 to assert that:
- tier=1 stale log IS present (unchanged)
- snmp_command_sent_total DOES increment (reversed from old behavior)
- Remove or reverse the `DELTA_SENT == 0` sub-scenarios

### Pitfall 3: Source-aware threshold E2E requires Trap or Command data source

**What goes wrong:** The source-aware distinction (Poll/Synthetic = all samples, Trap/Command = newest only) cannot be tested with the existing `stale`/`command_trigger`/`healthy` scenarios because all current tenant fixtures use Poll-sourced metrics. The `Source` field on `MetricSlotHolder` is set at runtime by which pipeline path delivers the data — not from tenant config.

**How to avoid:** To test Trap-source threshold behavior, a tenant fixture must reference a metric name that arrives via the trap pipeline (e.g. a metric that `e2e_trap_value` resolves to). Similarly for Command source, the metric watched by the tenant must be the same one written back by CommandWorkerService SET response. Alternatively, the scenario can verify the source-aware behavior indirectly by observing that one in-range sample in a multi-sample series prevents tier=4 (Poll/Synthetic all-samples behavior), then verify recovery from a single in-range sample. The direct Trap/Command-newest-only path requires new simulator OIDs routed through the trap pipeline, which is more complex.

**Recommendation:** Scenario 39 (source-aware) should target the simpler end: verify Poll/Synthetic all-samples check (already validated by ADV-02) is identified by source=poll label. For Trap/Command newest-only, a new simulator trap scenario and fixture can route a trap-sourced metric as Resolved. This is feasible but requires:
  1. A new simulator trap scenario that sends a specific OID value via trap
  2. A new tenant fixture where a Resolved metric is the OID that traps carry
  3. The test sets the trap to in-range (not violated), waits for it to arrive, then asserts tier=3 (not tier=2) to prove the Resolved gate passed based on single newest-only sample

**Confidence:** MEDIUM — the trap routing path through ChannelConsumerService sets `Source=Trap` on the holder, but whether the tenant fixture can reference a trap-sourced metric name depends on the OID map mapping the trap OID to a named metric.

### Pitfall 4: report.sh category range needs updating

**What goes wrong:** `_REPORT_CATEGORIES` in `report.sh` has `"Snapshot Evaluation|28|36"` (0-indexed, maps to scenarios 29-37). Adding scenarios 38-39 would silently exclude them from the report.

**How to avoid:** Update the end index in `report.sh` to cover new scenarios. New range: `"Snapshot Evaluation|28|40"` or similar depending on final count.

### Pitfall 5: poll_until_log `since` window must cover the assertion timing

**What goes wrong:** poll_until_log uses kubectl logs --since=Xs. If `since` is too short, the target log line may be in the pod log but outside the `since` window, causing false failures.

**How to avoid:** Use `since` of at least 60s for most assertions. For scenarios where tier=1 stale could appear after 35+ seconds, use `since=60` minimum.

### Pitfall 6: Staleness scenario priming timing

**What goes wrong:** Scenario 38 (new stale-to-commands) needs to verify commands ARE sent after staleness. However, suppression window matters: if `SuppressionWindowSeconds` is 10 (default in tenant-cfg01-single.yaml), the first stale-triggered command sends, but subsequent cycles suppress. The scenario must baseline BEFORE stale switch, then assert delta > 0 within one cycle.

**How to avoid:** Use `tenant-cfg01-single.yaml` (10s suppression window). Baseline BEFORE switching to stale. The first command will be sent (not suppressed if no prior command in window). Poll for tier=4 log (stale path), then poll_until counter > baseline.

## Code Examples

### Existing tier log patterns (verified from SnapshotJob.cs)

```
tier=1: "Tenant {TenantId} priority={Priority} tier=1 stale — skipping to commands"
tier=2 (stop): "Tenant {TenantId} priority={Priority} tier=2 — all resolved violated, no commands"
tier=2 (pass): "Tenant {TenantId} priority={Priority} tier=2 — resolved not all violated, proceeding to evaluate check"
tier=3: "Tenant {TenantId} priority={Priority} tier=3 — not all evaluate metrics violated, no action"
tier=4: "Tenant {TenantId} priority={Priority} tier=4 — commands enqueued, count={CommandCount}"
```

Key: In grep patterns, use `\|` for OR matching across patterns in poll_until_log:
```bash
poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30
```

### Simulator scenarios (verified from e2e_simulator.py SCENARIOS dict)

| Name | OID values | What it exercises |
|------|-----------|-------------------|
| `default` | all 0 | tier=2 stop (Resolved at 0 < Min:1.0) |
| `command_trigger` | .4.1=90, .4.2=2, .4.3=2 | tier=4 (Evaluate violated, Resolved in-range) |
| `healthy` | .4.1=5, .4.2=2, .4.3=2 | tier=3 (all in-range) |
| `stale` | .4.1=STALE, .4.2=STALE | tier=1 → tier=4 (staleness detected) |
| `agg_breach` | .4.2=2, .4.3=2, .4.5=50, .4.6=50 | tier=4 via synthetic aggregate |

No new simulator scenarios are needed for scenarios 38-39 if we reuse `stale` for stale-to-commands and `command_trigger` for source-aware. A new simulator scenario would only be needed if testing Trap-source directly.

### Fixture naming convention (verified from existing fixtures)

```
tenant-cfg01-single.yaml         — e2e-tenant-A, Priority=1, SuppressionWindowSeconds=10
tenant-cfg01-suppression.yaml    — e2e-tenant-A-supp, Priority=1, SuppressionWindowSeconds=30
tenant-cfg02-two-same-prio.yaml  — e2e-tenant-A + e2e-tenant-B, both Priority=1
tenant-cfg03-two-diff-prio-mts.yaml — e2e-tenant-P1 + e2e-tenant-P2, Priority=1+2
tenant-cfg04-aggregate.yaml      — e2e-tenant-agg, synthetic aggregate metric
```

New fixture for phase 58 (if source-aware scenario uses dedicated fixture):
```
tenant-cfg05-source-aware.yaml   — e2e-tenant-src-aware, distinct name prevents cache bleed
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Staleness returns `TierResult.Stale`, no commands dispatched | Staleness skips to tier=4, commands dispatched | Quick-076 (Mar 2026) | Scenario 33 must be updated to assert commands ARE sent |
| `TierResult.ConfirmedBad` (tier=2 stop) | `TierResult.Violated` (tier=2 stop) | Quick-076 (Mar 2026) | Scenario 31 log pattern must be updated |
| Log: `"tier=2 — all resolved violated, device confirmed bad"` | Log: `"tier=2 — all resolved violated, no commands"` | Quick-076 (Mar 2026) | grep pattern in scenario 31 is broken |
| All time series samples checked for all sources | Poll/Synthetic = all samples; Trap/Command = newest only | Quick-071 (Mar 2026) | New E2E coverage needed for source-aware path |
| Staleness checked for all sources | Trap and Command sources excluded from staleness check | Quick-070 (Mar 2026) | Stale scenario only affects Poll/Synthetic holders |

## Open Questions

1. **Source-aware Trap/Command E2E feasibility**
   - What we know: `SnmpSource.Trap` is set by ChannelConsumerService when trap arrives; `SnmpSource.Command` is set by CommandWorkerService SET response. Both can populate a MetricSlotHolder if the tenant's metric name matches an OID the trap/command carries.
   - What's unclear: Whether a tenant fixture can be constructed where a Resolved metric receives its value from a trap rather than a poll — this depends on whether oidmap names the trap varbind OID as a metric name.
   - Recommendation: For phase 58, demonstrate source-aware via Poll/Synthetic all-samples check only (ADV-02 already covers this as a timing test). Trap/Command newest-only can be a separate Quick task if needed. Accept this as partial coverage in phase 58.

2. **Advance gate scenario already covered by 35-mts-02**
   - Success criteria #6 (advance gate: commanded tenant blocks lower-priority group) is fully covered by scenario 35 (MTS-02). No new script needed.
   - Recommendation: Confirm success criteria #6 is satisfied by existing scenario 35 and document as such in the plan.

3. **Scenario 33 update: should it become a separate new script or in-place update?**
   - What we know: run-all.sh sources scripts by numeric glob order; updating in-place changes the same scenario slot.
   - What's unclear: Whether to rename scenario 33 since its semantics change from "no commands while stale" to "commands dispatched when stale."
   - Recommendation: Update in-place (scenario 33 stays as STS-05) with updated assertions. The scenario name can remain STS-05 with an updated subtitle.

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — Full 4-tier evaluation logic, all log messages, staleness exclusion rules, source-aware threshold checks
- `tests/e2e/scenarios/29-37-*.sh` — All 9 existing scenario scripts (complete pattern reference)
- `tests/e2e/lib/*.sh` — All 5 helper modules (sim.sh, prometheus.sh, kubectl.sh, common.sh, report.sh)
- `simulators/e2e-sim/e2e_simulator.py` — Complete SCENARIOS dict, all OID definitions
- `tests/e2e/fixtures/tenant-cfg0*.yaml` — All 5 existing fixture files
- `git log -- src/SnmpCollector/Jobs/SnapshotJob.cs` — Commit history confirming quick-076 changes
- `.planning/quick/076-snapshot-tier-fixes/076-SUMMARY.md` — Documented staleness fix and log rename

## Metadata

**Confidence breakdown:**
- Tier paths and log messages: HIGH — read directly from SnapshotJob.cs
- Existing scenario patterns: HIGH — read all 9 scripts + 5 lib files
- Outdated scenario identification: HIGH — cross-referenced code with scenario grep patterns, confirmed log mismatch
- Source-aware E2E feasibility: MEDIUM — understand the mechanism but Trap/Command fixture construction not validated end-to-end
- New scenario count/numbering: HIGH — based on run-all.sh glob, report.sh ranges

**Research date:** 2026-03-19
**Valid until:** 2026-04-19 (stable codebase, no fast-moving external dependencies)
