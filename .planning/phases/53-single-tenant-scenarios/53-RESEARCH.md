# Phase 53: Single-Tenant Scenarios - Research

**Researched:** 2026-03-17
**Domain:** Bash E2E scenario scripting, 4-tier evaluation assertion patterns
**Confidence:** HIGH

---

## Summary

Phase 53 writes five bash scenario scripts (STS-01 through STS-05) that validate every branch
of the SnapshotJob 4-tier evaluation tree. The codebase is fully built; this phase is pure
authoring. All patterns — sim scenario switching, ConfigMap apply/restore, pod-log polling,
Prometheus counter deltas — are established by the 28 existing scenarios and the Phase 52
libraries.

The critical constraint is the missing "healthy" simulator scenario. No existing scenario
produces a state where ALL resolved metrics are in-range AND ALL evaluate metrics are in-range,
which is the exact precondition for reaching Tier 3 "Healthy". This gap must be resolved
by adding a new `"healthy"` scenario to `e2e_simulator.py`.

The five scenarios run sequentially from a clean ConfigMap baseline (tenant-cfg01-single.yaml),
each restoring the tenant ConfigMap and resetting the simulator scenario before exit. Numbering
follows the existing file sequence: 29-sts-01-healthy.sh through 33-sts-05-staleness.sh.

**Primary recommendation:** Add a `"healthy"` simulator scenario first, then write the five
scenario scripts following the assertion discipline defined in CONTEXT.md: every test asserts
BOTH a specific log line AND a Prometheus counter delta, with poll_until_log (timeout-based,
not fixed sleep) for log assertions.

---

## Standard Stack

### Core (already in repo — no new installs)

| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| bash | system | test scripting | established project pattern |
| sim.sh | Phase 52 | sim_set_scenario, reset_scenario, poll_until_log | Phase 52 complete |
| prometheus.sh | existing | snapshot_counter, query_counter, poll_until, get_evidence | established |
| common.sh | existing | record_pass, record_fail, log_info, log_error | established |
| kubectl | cluster | ConfigMap apply, pod logs | established |

### Key Function Signatures (HIGH confidence — read from source)

```bash
# sim.sh
sim_set_scenario <name>              # POST /scenario/{name}; returns 1 on failure
reset_scenario                       # convenience: sim_set_scenario default
poll_until_log <timeout_s> <interval_s> <grep_pattern> [since_seconds]
                                     # returns 0 on first match any pod, 1 on timeout

# prometheus.sh
snapshot_counter <metric_name> [label_filter]   # returns integer value string
query_counter    <metric_name> [label_filter]   # alias of snapshot_counter (current value)
poll_until  <timeout_s> <interval_s> <metric> <filter> <baseline>  # polls until > baseline
get_evidence <metric_name> [label_filter]       # returns formatted evidence string

# common.sh
record_pass <scenario_name> <evidence>
record_fail <scenario_name> <evidence>
assert_delta_gt <delta> <threshold> <scenario_name> <evidence>
```

### Prometheus Metric Names (HIGH confidence — verified in PipelineMetricService.cs)

OTel instrument names use dots; Prometheus scrape converts to underscores:

| OTel Name | Prometheus Name | Label | What it counts |
|-----------|----------------|-------|----------------|
| snmp.command.sent | snmp_command_sent_total | device_name | SNMP SET commands executed by CommandWorkerService |
| snmp.command.suppressed | snmp_command_suppressed_total | device_name | SET commands suppressed by SuppressionCache |
| snmp.command.failed | snmp_command_failed_total | device_name | SET commands that failed |
| snmp.snapshot.cycle_duration_ms | snmp_snapshot_cycle_duration_ms_bucket | (histogram, no direct label) | SnapshotJob cycle duration |

**Label value for e2e commands:** `device_name="E2E-SIM"` (CommandWorkerService calls
`IncrementCommandSent(device.Name)` where device.Name is derived from CommunityString
`Simetra.E2E-SIM` → `E2E-SIM`).

**Label value for suppressed commands:** `device_name` is the tenant ID passed to
`IncrementCommandSuppressed(tenant.Id)`, which is `"e2e-tenant-A"` from the fixture.

---

## Architecture Patterns

### Recommended File Structure

```
tests/e2e/scenarios/
├── 29-sts-01-healthy.sh
├── 30-sts-02-evaluate-violated.sh
├── 31-sts-03-resolved-gate.sh
├── 32-sts-04-suppression-window.sh
└── 33-sts-05-staleness.sh

simulators/e2e-sim/
└── e2e_simulator.py     (EXTEND — add "healthy" scenario)

tests/e2e/lib/
└── report.sh            (EXTEND — add "Snapshot Evaluation" category)
```

### Standard Scenario Script Structure

```bash
# Every script follows this skeleton (from 28-tenantvector-routing.sh and 01-poll-executed.sh):

# 1. Reset simulator and apply fixture (belt-and-suspenders)
reset_scenario
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
kubectl apply -f "$FIXTURES_DIR/tenant-cfg01-single.yaml" -n simetra

# 2. Wait for ConfigMap reload (poll_until_log is safer than fixed sleep)
poll_until_log 60 5 "Tenant vector reload complete" 30 || log_warn "Watcher reload not detected"

# 3. Set the target scenario
sim_set_scenario <scenario_name>

# 4. Wait for SnapshotJob cycle (poll_until_log with timeout)
poll_until_log <timeout_s> <interval_s> "<log_pattern>" <since_s>

# 5. Assert log line (evidence from log poll result)
# 6. Snapshot and delta-check Prometheus counters
# 7. Cleanup: reset sim + restore tenant ConfigMap
reset_scenario
restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
```

### Report Category Addition

report.sh `_REPORT_CATEGORIES` array must be extended with a new entry covering scenarios 29-33:

```bash
"Snapshot Evaluation|28|32"   # 0-based: scenarios[28..32] = files 29-33
```

The current last category is `"Tenant Vector|33|36"` (0-based), which covers existing
scenarios 28 (index 33 is unreachable since only 28 scenarios exist). Adding the new category
at the end extends the report correctly.

**IMPORTANT:** The category `_REPORT_CATEGORIES` array uses 0-based indexes. With 28 existing
tests (indices 0-27), STS-01 is index 28, STS-05 is index 32. The new entry is:
`"Snapshot Evaluation|28|32"`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Waiting for log evidence | fixed `sleep 30` | `poll_until_log 90 5 "<pattern>" 60` | Fixed sleep is fragile under load; poll_until_log returns immediately on match |
| ConfigMap reload detection | regex-parse watcher logs manually | `poll_until_log` for "Tenant vector reload complete" | Watcher already logs this exact string |
| Prometheus counter zero-assertion | custom negative check | `snapshot_counter` + arithmetic `[ "$DELTA" -eq 0 ]` | No need for new function — just read and compare |
| Scenario cleanup | per-script custom cleanup | `reset_scenario` + `restore_configmap` pattern from scenario 28 | Already established |

---

## Common Pitfalls

### Pitfall 1: Missing "healthy" simulator scenario

**What goes wrong:** STS-01 (healthy baseline) requires resolved metrics in-range (e2e_channel_state ≥ 1.0, e2e_bypass_status ≥ 1.0) AND evaluate metric in-range (e2e_port_utilization ≤ 80). No existing simulator scenario produces this.

**Root cause analysis:**
- `default` scenario: .4.1=0, .4.2=0, .4.3=0 → both resolved violated → Tier 2 ConfirmedBad, never reaches Tier 3
- `threshold_clear` scenario: .4.1=5, .4.2=0, .4.3=0 → resolved still violated → Tier 2 ConfirmedBad
- `bypass_active` scenario: .4.3=1, .4.1=0, .4.2=0 → only bypass resolved, channel_state still 0 < 1.0 → still Tier 2 ConfirmedBad
- `command_trigger` scenario: .4.1=90 (evaluate violated) → would reach Tier 4, not Tier 3 healthy

**How to resolve:** Add a `"healthy"` scenario to `e2e_simulator.py`:

```python
"healthy": _make_scenario({
    f"{E2E_PREFIX}.4.1": 5,    # e2e_port_utilization = 5 (< Max:80 → in-range)
    f"{E2E_PREFIX}.4.2": 2,    # e2e_channel_state = 2 (≥ Min:1.0 → in-range)
    f"{E2E_PREFIX}.4.3": 2,    # e2e_bypass_status = 2 (≥ Min:1.0 → in-range)
}),
```

This produces: resolved NOT all violated → passes Tier 2 → evaluate NOT violated → Tier 3 Healthy.

**Warning signs:** If STS-01 logs Tier 2 ConfirmedBad instead of Tier 3, the scenario values are wrong.

### Pitfall 2: Tier 3 log line is LogDebug — may not appear in production log level

**What goes wrong:** Tier 3 "not all evaluate metrics violated, no action" is `_logger.LogDebug(...)`. If the deployment log level is higher than Debug, this line never appears.

**How to detect:** Run `kubectl logs <pod> -n simetra | grep "tier=3"` manually before writing assertion.

**Mitigation:** Use the absence of tier=4 log as backup evidence. Assert tier=3 log with `poll_until_log`; if it times out, check pod log level configuration.

**Note:** Tier 2 "resolved not all violated, proceeding to evaluate check" is also LogDebug. Both tiers 1, 2, 3 are Debug; only Tier 4 is LogInformation. If debug logs are suppressed, STS-01 and STS-03 must assert using counters only and absence checks.

### Pitfall 3: TimeSeriesSize=3 requires multiple poll cycles before evaluate assertion is valid

**What goes wrong:** The e2e_port_utilization metric has `TimeSeriesSize: 3` in the tenant fixture. `AreAllEvaluateViolated` requires ALL samples in the series to be violated. With TimeSeriesSize=3, the series needs 3 samples, meaning 3 poll cycles (at 10s each) before the series is fully populated with the new scenario's values.

**How to resolve:** After switching to `command_trigger` scenario (STS-02), wait for at least 2 SnapshotJob cycles (2 × 15s = 30s) after polling has refreshed the time series. Use `poll_until_log` targeting the Tier 4 log line rather than asserting after a fixed sleep.

**Note:** Resolved metrics (e2e_channel_state, e2e_bypass_status) have no `TimeSeriesSize` set in the fixture (they use default). Check the TenantConfigurationModel default for TimeSeriesSize.

### Pitfall 4: SuppressionCache persists across scenario runs within the same pod session

**What goes wrong:** If STS-02 fires a command, the suppression cache stamps the key `e2e-tenant-A:e2e-simulator.simetra.svc.cluster.local:161:e2e_set_bypass`. STS-04 (suppression test) runs after STS-02. If the suppression window (10s) has NOT expired, STS-04 may see the command already suppressed from a previous run rather than a fresh first-command cycle.

**How to resolve:** STS-04 must be structured to produce a fresh first command (outside the window) then a second command (inside the window). Use a sleep slightly longer than SuppressionWindowSeconds=10 between the STS-02 cleanup and STS-04 setup, or structure STS-04 with an initial "prime" step that waits for the window to expire first.

**Key timing:** SuppressionWindowSeconds=10. SnapshotJob interval=15s. After STS-02 fires a command, STS-04 will naturally run 15s+ later during sequential execution — the window will likely have expired.

### Pitfall 5: ConfigMap reload is not instantaneous

**What goes wrong:** `kubectl apply -f tenant-cfg01-single.yaml` does not synchronously reload the tenant vector in the running pod. The ConfigMap watcher detects changes via inotify/fsnotify with a small delay.

**How to resolve:** After applying the tenant ConfigMap, use `poll_until_log 60 5 "Tenant vector reload complete" 30` before asserting tier logs. Do NOT use `sleep 15` fixed.

**Baseline:** From existing scenario 28, `sleep 15` was used for watcher detection — but `poll_until_log` is more reliable and the CONTEXT.md decision mandates it.

### Pitfall 6: STS-05 stale scenario — staleness requires grace window to expire

**What goes wrong:** The `stale` simulator scenario returns NoSuchInstance for .4.1 and .4.2. But the staleness check in Tier 1 (`HasStaleness`) checks whether `age > graceWindow` where `graceWindow = IntervalSeconds * GraceMultiplier = 10 * 2.0 = 20s`. The last-polled data is still fresh immediately after switching to `stale`.

**How to resolve:** After switching to `stale` scenario, wait for at least one full grace window (20s) plus one SnapshotJob cycle (15s) = ~35s before asserting the Tier 1 stale log. Use `poll_until_log 90 5 "tier=1 stale"`.

**Note:** `HasStaleness` skips holders with `slot is null` (returns false — cannot judge). The slot becomes stale only after poll data ages beyond the grace window. The stale scenario makes the simulator return NoSuchInstance, which causes poll failures; then the existing timestamps age out.

### Pitfall 7: Counter label for command_suppressed is tenant ID not device name

**What goes wrong:** `snmp_command_suppressed_total` is labeled with the tenant ID (`e2e-tenant-A`), not the device name. The filter should be `tenant_id="e2e-tenant-A"` or no filter.

**Verification needed:** The SnapshotJob code calls `_pipelineMetrics.IncrementCommandSuppressed(tenant.Id)`. The `PipelineMetricService.IncrementCommandSuppressed` adds tag `device_name`. So the label key is still `device_name` but the value is the tenant ID `e2e-tenant-A`, not the device name.

**Filter to use:** `device_name="e2e-tenant-A"` for suppressed counter.

---

## Scenario-by-Scenario Technical Analysis

### STS-01: Healthy Baseline

**Requires:** New "healthy" simulator scenario (.4.1=5, .4.2=2, .4.3=2)

**Expected tier path:**
1. Tier 1: no staleness (data is fresh)
2. Tier 2: e2e_channel_state=2 ≥ 1.0 → NOT all resolved violated → log "resolved not all violated, proceeding to evaluate check"
3. Tier 3: e2e_port_utilization=5 ≤ 80 → NOT all evaluate violated → log "not all evaluate metrics violated, no action" → TierResult.Healthy

**Log assertions:**
- Tier 3: `"priority=1 tier=3 — not all evaluate metrics violated, no action"` (LogDebug)
- Tier 2 pass (optional but strong evidence): `"tier=2 — resolved not all violated, proceeding to evaluate check"` (LogDebug)

**Counter assertions:**
- `snmp_command_sent_total{device_name="E2E-SIM"}` delta == 0
- `snmp_command_suppressed_total{device_name="e2e-tenant-A"}` delta == 0

**Counter assertion method:** snapshot_counter BEFORE, wait for tier=3 log, snapshot_counter AFTER. Assert delta == 0 with direct arithmetic.

### STS-02: Evaluate Violated (command_trigger scenario)

**Simulator scenario:** `command_trigger` (.4.1=90, .4.2=2, .4.3=2)

**Expected tier path:**
1. Tier 2: resolved in-range → pass
2. Tier 3: e2e_port_utilization=90 > Max:80 → ALL evaluate violated → proceed to Tier 4
3. Tier 4: suppression window open → command enqueued → log "tier=4 — commands enqueued, count=1"

**Key timing:** e2e_port_utilization has TimeSeriesSize=3. After switching scenario, wait for 3 poll cycles (30s at 10s interval) before the evaluate series is fully populated with value=90. Use `poll_until_log 90 5 "tier=4"` rather than fixed sleep.

**Log assertions:**
- Tier 4: `"priority=1 tier=4 — commands enqueued, count=1"` (LogInformation — always visible)

**Counter assertions:**
- `snmp_command_sent_total{device_name="E2E-SIM"}` delta > 0 (CommandWorkerService executed the SET)

### STS-03: Resolved Gate (default scenario)

**Simulator scenario:** `default` (.4.1=0, .4.2=0, .4.3=0)

**Expected tier path:**
1. Tier 2: e2e_channel_state=0 < 1.0 AND e2e_bypass_status=0 < 1.0 → ALL resolved violated → log "all resolved violated, device confirmed bad" → TierResult.ConfirmedBad

**Log assertions:**
- Tier 2 ConfirmedBad: `"priority=1 tier=2 — all resolved violated, device confirmed bad, no commands"` (LogDebug)

**Counter assertions:**
- `snmp_command_sent_total{device_name="E2E-SIM"}` delta == 0
- `snmp_command_suppressed_total{device_name="e2e-tenant-A"}` delta == 0

**Claude's Discretion (STS-03 absence assertion):** Assert absence of Tier 3 and Tier 4 logs by confirming only the Tier 2 ConfirmedBad log is present. This adds confidence that evaluation truly stopped at Tier 2. Recommended: use a negative grep check after the Tier 2 log appears to verify no Tier 4 log appeared in the same time window.

**Note:** `default` is also the reset_scenario state. STS-03 may not need to call sim_set_scenario at all if the prior test already cleaned up — but calling `reset_scenario` explicitly is belt-and-suspenders.

### STS-04: Suppression Window (one script, 3 assertion windows)

**Simulator scenario:** `command_trigger` throughout

**Three sequential windows:**

Window 1 — First command (sent):
- Apply tenant-cfg01-single.yaml, poll until reload
- Set command_trigger scenario
- Wait for tier=4 log → assert `snmp_command_sent_total` delta > 0

Window 2 — Suppressed (within 10s window):
- Remain in command_trigger scenario; do NOT reset suppression cache
- Wait for next SnapshotJob cycle (15s ≤ suppression_window=10s... but 15s > 10s!)

**TIMING CRITICAL:** SuppressionWindowSeconds=10. SnapshotJob interval=15s. After the first command at T=0, at T=15s the next cycle runs. Is 15s > 10s? Yes — so by the time the next cycle runs, the suppression window has ALREADY expired. The suppression window (10s) is shorter than the SnapshotJob interval (15s).

**Resolution:** The suppression window of 10s will have expired before the next 15s SnapshotJob cycle. STS-04 as designed cannot observe in-window suppression with the current configuration. Two options:
1. Decrease SuppressionWindowSeconds in the tenant fixture to something > SnapshotJob interval (e.g., 30s or 60s) so the window outlasts 1 cycle
2. OR: re-read the CONTEXT.md suppression test description — it says "second cycle within window shows command suppressed"

The fixture tenant-cfg01-single.yaml has `SuppressionWindowSeconds: 10`. For suppression to trigger within a 15s cycle, the second cycle must fire within 10s of the first. That's impossible with a 15s job interval.

**Recommended resolution for STS-04:** Use a modified tenant fixture that sets `SuppressionWindowSeconds: 30` (or 60) instead of 10. A separate `tenant-cfg01-suppression.yaml` fixture gives STS-04 its own tuned config without affecting other tests. With a 30s window: first command at T=0, second cycle at T=15s → suppressed (15s < 30s), third cycle at T=30s → suppressed (still in window), fourth cycle at T=45s → sent again.

OR: The planner may decide to test with a fixture that has a larger suppression window. This is a planning decision, not a research gap.

Window 3 — Sent again (window expires):
- After SuppressionWindowSeconds passes, next cycle should send again
- Assert `snmp_command_sent_total` delta > 0 again and `snmp_command_suppressed_total` delta > 0 for the suppressed window

### STS-05: Staleness (stale scenario + poll_until_log)

**Simulator scenario:** `stale` (.4.1=STALE, .4.2=STALE → NoSuchInstance)

**Expected tier path:**
1. Tier 1: after grace window expires (10s interval × 2.0 multiplier = 20s), data is stale → log "tier=1 stale — skipping threshold checks"

**Timing:** After switching to stale scenario, poll data ages out. The last successful poll was before the switch. Staleness triggers when `age > graceWindow`. With 10s poll interval and 2.0 GraceMultiplier, grace window = 20s. The next SnapshotJob cycle after 20s of stale data will produce the Tier 1 log.

**Strategy:** `sim_set_scenario stale` → `poll_until_log 90 5 "tier=1 stale" 60`

**Counter assertions:**
- `snmp_command_sent_total{device_name="E2E-SIM"}` delta == 0 (no commands while stale)

---

## Code Examples

### ConfigMap apply + reload wait pattern

```bash
# Source: tests/e2e/scenarios/28-tenantvector-routing.sh (adapted)
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
kubectl apply -f "$FIXTURES_DIR/tenant-cfg01-single.yaml" > /dev/null 2>&1 || true
poll_until_log 60 5 "Tenant vector reload complete" 30 || \
    log_warn "Tenant vector reload not detected within 60s; proceeding"
```

### Simulator scenario + log assertion pattern

```bash
# Source: Pattern from sim.sh and common scenario structure
sim_set_scenario command_trigger
BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
if poll_until_log 90 5 "tier=4 — commands enqueued" 60; then
    AFTER_SENT=$(query_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA=$((AFTER_SENT - BEFORE_SENT))
    assert_delta_gt "$DELTA" 0 "STS-02: Evaluate violated — command dispatched" \
        "log=tier4_found $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
else
    record_fail "STS-02: Evaluate violated — command dispatched" \
        "tier=4 log not found within 90s"
fi
```

### Counter delta == 0 assertion pattern

```bash
# Source: Pattern from common.sh assert_delta_gt, adapted for zero assertion
BEFORE=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
poll_until_log 90 5 "tier=3 — not all evaluate metrics violated" 60 || true
AFTER=$(query_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
DELTA=$((AFTER - BEFORE))
if [ "$DELTA" -eq 0 ]; then
    record_pass "STS-01: Healthy baseline — zero commands" \
        "delta=${DELTA} $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
else
    record_fail "STS-01: Healthy baseline — zero commands" \
        "unexpected delta=${DELTA} expected=0"
fi
```

### Cleanup pattern (every scenario)

```bash
# Source: tests/e2e/scenarios/28-tenantvector-routing.sh cleanup section
reset_scenario
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "Failed to restore tenant ConfigMap from snapshot"
fi
```

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|-----------------|--------|
| Fixed sleep between scenario switch and assertion | poll_until_log with timeout | CONTEXT.md mandates poll_until_log for staleness test; apply to all log assertions |
| Single-assertion per test | Dual assertion (log + counter) | CONTEXT.md mandates BOTH must pass |
| Implicit cleanup | Explicit sim reset + ConfigMap restore per scenario | Ensures no scenario pollutes the next |

---

## Open Questions

### 1. SuppressionWindowSeconds=10 vs SnapshotJob interval=15s — STS-04 cannot observe suppression

**What we know:** SuppressionWindowSeconds=10 (fixture). SnapshotJob interval=15s (default). The suppression window expires before the next cycle fires. First command at T=0. Next cycle at T=15s. 15s > 10s → suppression window already expired → second command is SENT, not suppressed.

**What's unclear:** Whether STS-04 should use a different fixture (with larger window) or the CONTEXT.md description of "second cycle within window" implicitly requires a fixture adjustment.

**Recommendation:** Create `tenant-cfg01-suppression.yaml` (copy of cfg01 with `SuppressionWindowSeconds: 30`) for STS-04. The planner should decide whether to create this new fixture or adjust the existing one.

### 2. Log level for Debug-tier logs

**What we know:** Tier 1, 2, 3 logs are all `LogDebug`. Tier 4 is `LogInformation`. The K8s deployment log level configuration is not verified in this research.

**What's unclear:** Whether the deployed snmp-collector pods emit Debug-level logs.

**Recommendation:** The planner should include a step to verify debug log emission OR provide fallback assertion logic (counter-only assertions if Debug logs are not visible). Check `deploy/k8s/snmp-collector/` for log level configuration.

### 3. Exact ConfigMap reload log string

**What we know:** Scenario 28 uses `grep "Tenant vector reload complete"` and `grep "TenantVectorWatcher initial load complete"`. These are the expected log strings.

**What's unclear:** Whether the exact log string from the TenantVectorWatcher matches this pattern exactly in the current codebase.

**Recommendation:** Verify the log string by searching for it in the watcher source, or use a fuzzy pattern like `"reloaded"` combined with `"tenants="` (as in scenario 28 step 28d).

---

## Sources

### Primary (HIGH confidence)

- Read directly from source: `tests/e2e/lib/sim.sh` — all 4 functions, exact signatures
- Read directly from source: `tests/e2e/lib/prometheus.sh` — snapshot_counter, query_counter, poll_until, get_evidence
- Read directly from source: `tests/e2e/lib/common.sh` — record_pass, record_fail, assert_delta_gt
- Read directly from source: `tests/e2e/lib/report.sh` — category format, index scheme
- Read directly from source: `src/SnmpCollector/Jobs/SnapshotJob.cs` — all 4 tier log strings, TierResult enum, staleness/resolved/evaluate logic
- Read directly from source: `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — all metric names, label keys
- Read directly from source: `src/SnmpCollector/Services/CommandWorkerService.cs` — device_name label value for command_sent
- Read directly from source: `src/SnmpCollector/Pipeline/SuppressionCache.cs` — TrySuppress semantics
- Read directly from source: `simulators/e2e-sim/e2e_simulator.py` — all 6 scenarios, OID values
- Read directly from source: `tests/e2e/fixtures/tenant-cfg01-single.yaml` — tenant configuration, SuppressionWindowSeconds=10, TimeSeriesSize=3

### Secondary (MEDIUM confidence)

- Read from Phase 52 VERIFICATION.md — Phase 52 artifacts confirmed complete, all wired
- Read from `tests/e2e/scenarios/28-tenantvector-routing.sh` — ConfigMap save/restore, reload wait pattern

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all functions read from source
- Scenario analysis: HIGH — tier logic read from SnapshotJob.cs directly
- Simulator scenarios: HIGH — verified all 6 scenarios in e2e_simulator.py
- Prometheus metric names: HIGH — verified in PipelineMetricService.cs and Grafana dashboard queries
- Suppression timing gap (STS-04): HIGH — confirmed 10s window < 15s interval; planning decision required
- Log level for Debug logs: MEDIUM — tier log levels confirmed in source but deployment log level not checked

**Research date:** 2026-03-17
**Valid until:** 2026-04-17 (stable codebase, no moving targets)
