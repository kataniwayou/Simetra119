# Domain Pitfalls

**Domain:** E2E test infrastructure — HTTP-controlled simulator and bash evaluation test scripts added to existing SNMP monitoring system
**Researched:** 2026-03-17
**Confidence:** HIGH (verified against source code: SnapshotJob.cs, MetricSlotHolder.cs, SuppressionCache.cs, TenantVectorRegistry.cs, CommandWorkerService.cs, e2e_simulator.py, existing scenario scripts, lib/prometheus.sh, lib/common.sh)

> **Note:** This file covers pitfalls specific to the v2.1 addition: HTTP scenario control endpoint on the pysnmp simulator, scenario registry, bash test scripts for SnapshotJob evaluation, and multi-tenant test configurations. Earlier pitfall files (v1.5–v1.9 additions) are not repeated here. The pitfalls below assume the v2.0 system is already deployed and functional.

---

## Critical Pitfalls

Mistakes that cause silent test passes, timing failures, or misread evaluation results.

---

### Pitfall 1: asyncio Event Loop Blocking — HTTP Handler Stalls SNMP Responses

**What goes wrong:** The existing `e2e_simulator.py` runs one asyncio event loop that drives both pysnmp's `snmpEngine.open_dispatcher()` and the trap send tasks. Adding an HTTP endpoint naively with `http.server.BaseHTTPRequestHandler` (which is synchronous) or with a blocking `asyncio.run()` call inside the scenario switch handler will block the event loop. While the HTTP request is being processed, pysnmp cannot service incoming SNMP GETs from the collector. The collector's `MetricPollJob` gets a timeout, records a consecutive failure, and the `MetricSlotHolder` stops receiving fresh samples — causing the next SnapshotJob cycle to return `TierResult.Stale` rather than evaluating thresholds.

**Why it happens:** pysnmp's asyncio transport integrates with the running event loop via `open_dispatcher()`. Any synchronous I/O in a coroutine or callback on the same loop stalls all I/O, including SNMP UDP reads. The symptom looks like intermittent poll failures during the test window, not a configuration error.

**Consequences:** Test scripts waiting for `TierResult.Commanded` observe only `TierResult.Stale`. Prometheus command counters never increment. The scenario assertion times out. The failure is non-deterministic (depends on whether an HTTP request overlaps with a pysnmp SNMP GET handler).

**Prevention:** Implement the HTTP control endpoint as an async coroutine using `aiohttp` or `asyncio`-native `aiohttp.web` / `asyncio.start_server`. Both integrate into the existing event loop without blocking. The scenario switch (`current_scenario = new_name`) must be a non-blocking in-memory assignment — never call `time.sleep()` or any blocking I/O inside the handler coroutine. Keep HTTP handler code to: validate request, mutate scenario variable, return `200 OK`. No disk I/O, no subprocess calls.

**Detection:** Add a log line in the SNMP GET handler (`DynamicInstance.getValue`) that fires every time a value is read. If GET responses stop arriving in the simulator logs during an HTTP call, the event loop is being blocked. In bash test output: SnapshotJob log shows `tier=1 stale` immediately after the HTTP scenario switch.

**Phase:** HTTP simulator implementation.

---

### Pitfall 2: Sentinel Value (0) Satisfies Equality Thresholds — False Command Dispatch at Startup

**What goes wrong:** `MetricSlotHolder` initializes with a sentinel `MetricSlot(Value=0, Timestamp=UtcNow)` before any real SNMP data arrives. `SnapshotJob.IsViolated()` treats `Min == Max` as an equality check: violated if `value == threshold.Min.Value`. If any test tenant is configured with an equality threshold where `Min = Max = 0` (e.g., testing "trigger when metric value is 0"), the sentinel slot makes the holder appear violated from the moment the pod starts.

**Why it happens:** `MetricSlotHolder` constructor writes the sentinel into the series: `new MetricSlot(0, null, DateTimeOffset.UtcNow)`. The staleness check in Tier 1 (`HasStaleness`) skips holders where `ReadSlot()` returns null — but after the sentinel write, `ReadSlot()` returns the sentinel (non-null). The staleness clock starts from the sentinel timestamp, and `IntervalSeconds * GraceMultiplier` keeps it "fresh" for the first 30s. During that window, Tier 3 sees `Value=0`, which satisfies a `Min=Max=0` equality threshold, and Tier 4 dispatches a command before any real data has been polled.

**Consequences:** A spurious SNMP SET fires within the first 30s of pod startup for any equality-zero threshold tenant. If the suppression cache is empty (new pod), the command is not suppressed. The test script's "before" counter snapshot is taken after the spurious command, making delta assertions unreliable.

**Prevention:** Two mitigations, apply both:
1. Avoid `Min = Max = 0` thresholds in test tenant configs. Use boundary-range thresholds (e.g., `Max = 5` for "trigger when value > 5") so that the sentinel value of 0 is within the safe range and does not violate the threshold.
2. In bash test scripts, add a startup settle wait (at minimum `IntervalSeconds * GraceMultiplier` = 30s) before taking the "before" counter snapshot, ensuring all holders have been overwritten by at least one real poll.

**Detection:** `snmp_command_sent_total` increments during the first 30s of pod startup without any scenario being active. Pod logs show `tier=4 — commands enqueued` with `correlationId` matching a cycle that fired before the first `snmp_poll_executed_total` increment for the test device.

**Phase:** Tenant config design and test script structure.

---

### Pitfall 3: Time Series Fill Requirement — Tests Assert Too Early

**What goes wrong:** `AreAllEvaluateViolated()` requires ALL samples in the time series to be violated, not just the latest. If a tenant metric slot is configured with `TimeSeriesSize = N`, the slot must receive N consecutive violated samples before SnapshotJob returns `TierResult.Commanded`. The bash test script sets a scenario (e.g., "threshold_breach"), waits one SnapshotJob cycle (15s), and checks command counters — but if `TimeSeriesSize > 1`, one cycle is not enough. The series still contains older in-range samples carried over from the pre-breach scenario.

**Why it happens:** The "wait for N cycles" calculation is invisible in the scenario config. The script author sees `TimeSeriesSize = 3` and `IntervalSeconds = 15` but only waits `15 + OTel_export_interval = 30s`. The actual minimum wait before the command can fire is `(TimeSeriesSize * IntervalSeconds) + OTel_export_interval = (3 * 15) + 15 = 60s`.

**Consequences:** The poll_until assertion times out (30s default) before the command fires. Test shows FAIL for `command_sent_total delta > 0`. The system is working correctly; the test wait window is too short.

**Prevention:** Derive the minimum wait time from the tenant config at test design time: `wait_seconds = (TimeSeriesSize * IntervalSeconds) + OTel_export_interval`. For the baseline `TimeSeriesSize=1, IntervalSeconds=15`, the minimum is 30s. The existing 30s poll timeout is sufficient for depth-1 series only. If any test tenant uses depth > 1, the poll timeout must be scaled. Add a comment in each scenario script stating the TimeSeriesSize assumption and the derived wait.

**Detection:** SnapshotJob logs show `tier=3 — not all evaluate metrics violated` on cycles after the scenario switch, even though the OID is returning a breaching value. A second log check a full `TimeSeriesSize * IntervalSeconds` seconds later shows the command fires correctly.

**Phase:** Bash test script design, timeout calculation.

---

### Pitfall 4: Suppression Cache Bleeds Between Scenarios — Second Test Sees "Already Suppressed"

**What goes wrong:** `SuppressionCache` uses lazy TTL expiry with a `ConcurrentDictionary<string, DateTimeOffset>`. The suppression key is `"tenantId:ip:port:commandName"`. After one test scenario triggers a command, the cache entry persists for `SuppressionWindowSeconds` (default 60s). The next scenario in the same test run, testing the same tenant's command, runs within the 60s window. The second scenario's SnapshotJob cycle evaluates Tier 4, calls `TrySuppress()`, finds the unexpired entry, and suppresses the command. The command counter does not increment; the test asserts delta > 0 and fails.

**Why it happens:** The suppression cache is a singleton per pod. It is not cleared between E2E test scenarios. The same pod that ran scenario A is still serving scenario B 20s later. The 60s default window spans multiple scenario cycles.

**Consequences:** Scenario B fails with delta=0 on `snmp_command_sent_total`, appearing to be a SnapshotJob evaluation bug. Debugging wastes time re-checking threshold config and time series fill, when the actual issue is suppression cache state from scenario A.

**Prevention:** Two strategies:
1. Space scenarios far enough apart that the suppression window expires. For 60s default, 75s between scenarios that fire the same command ensures clean state. This makes the full test suite slow.
2. Design scenarios to use distinct tenant IDs (and thus distinct suppression keys). Each test scenario writes its own tenant config to the ConfigMap with a unique name. The suppression key differs per tenant name, so scenario A's cache entry does not affect scenario B. This is the preferred approach.

An alternative: expose a cache-clear endpoint in the application (not recommended for production code) or use a short suppression window (e.g., 5s) in the test tenant config specifically.

**Detection:** Pod logs show `Command {CommandName} suppressed for tenant {TenantId}` on the cycle immediately after a scenario switch where a command should fire. The suppression log appears even though no command fired "just now" from the test's perspective.

**Phase:** Bash test script design, tenant config isolation.

---

### Pitfall 5: Multi-Replica Command Counter — Per-Pod vs. Cluster-Total Mismatch

**What goes wrong:** SnapshotJob runs on all 3 replicas simultaneously. All 3 pods evaluate thresholds. All 3 pods may enqueue a command to their local `CommandChannel`. However, `CommandWorkerService` gates SET execution with `if (!_leaderElection.IsLeader) return` — only the leader pod actually sends the SNMP SET. The `snmp_command_sent_total` counter only increments on the leader. `snmp_command_suppressed_total` increments on all pods for subsequent cycles (each pod has its own `SuppressionCache`). A test script querying `sum(snmp_command_sent_total)` sees the correct total. A test that queries per-pod and expects all pods to show `sent > 0` will fail for the two non-leader pods.

**Why it happens:** SnapshotJob is not leader-gated. The evaluation logic runs on every pod. Only the final SET dispatch step is leader-gated. The `_commandSuppressed` counter increments on all pods for the same command because each pod's `SuppressionCache` is independent.

**Consequences:** After one successful command trigger, the suppression counter on non-leader pods climbs every cycle for the window duration, but `sent` stays at 0 on non-leaders. A Prometheus assertion like `snmp_command_suppressed_total{pod=pod-1} > 0` passes for all pods, but `snmp_command_sent_total{pod=pod-2}` will never be > 0 if pod-2 is not leader.

**Prevention:** Always use `sum(snmp_command_sent_total)` without pod label filter for sent assertions. For per-pod assertions, use `max(snmp_command_sent_total) > 0` to assert at least one pod sent. When testing command suppression, `sum(snmp_command_suppressed_total)` reflects cumulative suppression across all pods — divide by 3 (replica count) to get the per-pod expectation, or use any() semantics.

**Detection:** `sum(snmp_command_sent_total) == 1` while the test expected 3. Pod logs confirm the SET was issued once; only one pod shows the "Command completed" INFO log. The other two pods show "Skipping SET — not leader" DEBUG lines.

**Phase:** Bash validation script design, Prometheus assertion construction.

---

### Pitfall 6: OTel Cumulative Temporality Delay — Delta Baseline Taken Before Export

**What goes wrong:** OTel SDK exports metrics every ~15s (cumulative temporality). The existing `snapshot_counter` utility queries the current Prometheus value at baseline time, then waits and queries again. If the baseline snapshot is taken in the first seconds after a pod restart or after a ConfigMap reload, the OTel export cycle may not have flushed the current counter value to Prometheus yet. The baseline captures a stale value. After the test scenario runs and the delta is computed, the apparent delta includes both the pre-baseline increment and the scenario increment — or the baseline is artificially low, inflating the delta.

**Why it happens:** `query_counter` calls Prometheus HTTP API which returns the last scraped value. The OTel Collector scrapes on its own interval (15s default). If the test baseline is taken immediately after scenario setup, before the OTel Collector has scraped the updated value, the baseline is 1-2 export cycles stale.

**Consequences:** Timing-sensitive tests produce inconsistent results: some runs pass (baseline taken after export), others fail (baseline taken before export with stale value). The failure manifests as delta=0 when a command was actually sent, or delta=2 when only 1 command was expected.

**Prevention:** After any ConfigMap change or pod restart, add a mandatory settle wait of at least `2 * OTel_export_interval` (30s) before taking the baseline snapshot. The existing 30s wait pattern in `28-tenantvector-routing.sh` is the correct model. New evaluation scenario scripts must apply the same pattern. Document this as a required wait after ConfigMap apply.

**Detection:** A scenario that consistently fails on the first run but passes on the second (when counters have stabilized) is showing this symptom. Querying Prometheus twice in succession 5s apart shows the value jumping mid-scenario — the export cycle landed between the two queries.

**Phase:** All bash scenario scripts.

---

## Moderate Pitfalls

Mistakes that cause test confusion, incorrect assertions, or hard-to-diagnose failures.

---

### Pitfall 7: Priority Group Advance Gate — Higher-Priority Tenant Blocks Lower-Priority Test

**What goes wrong:** `SnapshotJob` processes priority groups sequentially. If group 1 (higher priority) returns `TierResult.Stale` or `TierResult.Commanded` for any tenant, the `shouldAdvance` gate breaks the group loop — group 2 and beyond never run in that cycle. A test for a group-2 tenant will never see a command dispatch as long as any group-1 tenant is stale or active.

**Why it happens:** The advance gate logic in `SnapshotJob.Execute()`: `if (results[i] == TierResult.Stale || results[i] == TierResult.Commanded) { shouldAdvance = false; break; }`. This is intentional by design. The gate exists so lower-priority tenants do not issue commands when higher-priority tenants are already acting.

**Consequences:** A test for a same-priority scenario (2 tenants in group 1) that also deploys a second group inadvertently introduces a higher-priority tenant in group 1 with stale data (e.g., the device is not reachable yet). Every SnapshotJob cycle returns early. The group-2 test tenant command never fires.

**Prevention:** Assign the same priority to all tenants in a test that exercises parallel evaluation. For different-priority tests, ensure all higher-priority tenants are in a `TierResult.Healthy` state (metrics within threshold, not stale) before the scenario under test begins. If a higher-priority tenant uses a device that may be unreachable, configure its metrics with `IntervalSeconds = 0` (excluded from staleness checks) or use a device that is guaranteed reachable.

**Detection:** SnapshotJob logs for the cycle show `tier=1 stale` for a group-1 tenant, followed immediately by no log lines for group-2 tenants. The cycle summary log shows `Commanded=0, Stale=1` even though the group-2 threshold is violated.

**Phase:** Multi-tenant test scenario design.

---

### Pitfall 8: ConfigMap Reload Resets Sentinel — Carry-Over Skipped for Scenarios Changing Thresholds

**What goes wrong:** When the test script modifies the tenant ConfigMap to switch from one threshold configuration to another (e.g., changing `Max` from 100 to 5 to simulate a breach), `TenantVectorWatcherService` triggers `TenantVectorRegistry.Reload()`. The reload carries over existing slot values for metrics where `(ip, port, metricName)` matches the old config. However, if the `TimeSeriesSize` changes between the old and new config, `CopyFrom()` truncates the series: `series.RemoveRange(0, series.Length - TimeSeriesSize)`. Reducing `TimeSeriesSize` drops older samples. The new holder may start with a shorter series than expected, delaying the point at which all samples are violated.

**Why it happens:** `MetricSlotHolder.CopyFrom()` line 102-104: `var trimmed = oldSeries.Length > TimeSeriesSize ? oldSeries.RemoveRange(0, oldSeries.Length - TimeSeriesSize) : oldSeries`. This is correct behavior, but test authors who change `TimeSeriesSize` mid-test do not account for the truncation affecting test timing.

**Prevention:** Avoid changing `TimeSeriesSize` between scenario variations in the same test. Set a single `TimeSeriesSize` for each test tenant at deployment time and keep it constant across scenario switches. If a scenario requires a different depth, use a separate tenant with a distinct name.

**Detection:** After a ConfigMap threshold change, SnapshotJob cycles show `tier=3 — not all evaluate metrics violated` more times than `TimeSeriesSize` would predict. Checking the debug log for `TimeSeries holder: ... samples={SampleCount}` after reload shows fewer samples than expected.

**Phase:** Bash scenario design, ConfigMap reload interactions.

---

### Pitfall 9: kubectl Logs Pod Selection — Missing Commands from Non-Queried Replicas

**What goes wrong:** `kubectl logs pod/<pod-name>` only returns logs for one pod. SnapshotJob runs on all 3 replicas in parallel. The tier-4 log line `"Tenant {TenantId} priority={Priority} tier=4 — commands enqueued"` appears on all 3 pods (each pod evaluates independently). The leader pod also logs `"Command {CommandName} completed for {DeviceName}"` from `CommandWorkerService`. If the bash test only checks a single pod for the tier-4 log, it succeeds or fails depending on whether that pod was polled. If it only checks the leader pod for the "completed" log but the leader changed after a restart, the log is on a different pod.

**Why it happens:** The existing E2E scripts already handle this correctly for some scenarios (e.g., `28-tenantvector-routing.sh` iterates all pods). But new evaluation scripts that naively copy a single-pod log check pattern will miss events on the other two pods.

**Consequences:** Flaky tests where the same scenario passes on some runs (checked pod happened to be the one with the log) and fails on others.

**Prevention:** All log-based assertions must iterate all 3 pods with `kubectl get pods -o jsonpath='{.items[*].metadata.name}'`. For "tier-4 commands enqueued" assertions: find the first pod with the log line (any pod suffices — all 3 run SnapshotJob). For "Command completed" assertions: find the pod where the leader is; query `kubectl logs --all-containers` for the INFO log, or rely on `sum(snmp_command_sent_total) > 0` via Prometheus instead of log inspection.

**Detection:** A log assertion returns FAIL on one run and PASS on the next with no config changes. The evidence string shows `pod=pod-0` when the line appeared on `pod-1`.

**Phase:** Bash validation logic.

---

### Pitfall 10: Scenario Switch Timing — OID Value Change Not Yet Seen by Next Poll

**What goes wrong:** The HTTP scenario switch endpoint returns `200 OK` after updating the in-memory `current_scenario` variable in the simulator. The `DynamicInstance.getValue()` callback will return the new value on the next SNMP GET. However, the collector's `MetricPollJob` fires on a Quartz schedule that is not synchronized with the HTTP switch. If the test script switches the scenario and immediately enters the poll-until assertion loop, the first 1-2 Prometheus queries may reflect the pre-switch value (the poll cycle that was already in flight when the switch arrived has not yet completed its OTel export).

**Why it happens:** The poll cycle, OTel export, and Prometheus scrape are three independent 15s intervals that are not phase-aligned with the test script's wall clock. The scenario switch can land at any point in these cycles.

**Consequences:** The poll-until loop queries Prometheus and finds command counter unchanged. With a 30s timeout, there is a risk that the window closes before two full poll+export+scrape cycles complete, especially if the switch lands near the end of a poll cycle.

**Prevention:** After sending the HTTP scenario switch request, insert a mandatory sleep of one full poll cycle (15s) before starting the poll-until assertion. This ensures at least one complete poll cycle with the new OID values before asserting. The poll-until timeout should be at least `(TimeSeriesSize * IntervalSeconds) + (2 * OTel_export_interval)` = `(1 * 15) + 30 = 45s` minimum for depth-1 tenants.

**Detection:** The first 1-2 polls in the poll-until loop return the old Prometheus value. After ~30s the value updates. If the timeout is 30s, the test times out on the first iteration.

**Phase:** Bash test script timing, poll-until timeout values.

---

### Pitfall 11: Resolved Gate Blocks Command — Test Misreads Tier-2 as Tier-3 Failure

**What goes wrong:** `AreAllResolvedViolated()` returns `true` (all Resolved holders violated) → SnapshotJob returns `TierResult.ConfirmedBad` without dispatching a command. A test author who did not intend to trigger the resolved gate has inadvertently configured a Resolved metric whose threshold is also breached (or has no threshold, which evaluates as always-violated). The tenant stops at Tier 2 and never reaches Tier 4. Prometheus shows no command increment; the test fails.

**Why it happens:** The 4-tier logic has this specific asymmetry: `AreAllResolvedViolated = true` means "confirmed bad, all Evaluate metrics are confirmed bad too, no command needed." If any Resolved metric has no threshold (`threshold is null` → always violated), and the test intends to reach Tier 4, the vacuous violation stops the cascade silently.

**Consequences:** Test shows `snmp_command_sent_total` delta=0 despite the Evaluate threshold being breached. Pod logs show `tier=2 — all resolved violated, device confirmed bad, no commands` — a subtle distinction from `tier=3 — not all evaluate metrics violated`.

**Prevention:** In evaluation test tenant configs, ensure Resolved metrics have thresholds set such that the value returned by the "healthy" scenario is within range (not violated). Only switch the Evaluate metrics to a breaching value in the "breach" scenario. Explicitly validate the Resolved metric threshold in the tenant config comments.

**Detection:** Pod logs show `tier=2` log lines with `"device confirmed bad"` during the breach scenario instead of `tier=4`. SnapshotJob cycle summary logs show `Commanded=0` but `TierResult` for the tenant is `ConfirmedBad` not `Healthy`.

**Phase:** Tenant config design, test scenario logic verification.

---

## Minor Pitfalls

Mistakes that cause confusion but are quickly fixable.

---

### Pitfall 12: HTTP Endpoint Port Conflicts with SNMP Port 161

**What goes wrong:** The simulator already binds UDP port 161 for SNMP. If the HTTP control endpoint binds TCP port 161 on the same container, the bind succeeds (UDP and TCP are separate namespaces) but causes confusion when reading `netstat` output during debugging. More critically, if the HTTP port is chosen as 8080 and the simulator container already exposes 8080 for something else, or if the collector's health endpoint is on 8080 and port-forwards are active, there is a port collision.

**Prevention:** Choose a distinct port for the HTTP control endpoint (e.g., 9191 or 9080). Document the port in the simulator Dockerfile `EXPOSE` directive and in the K8s Service definition. Do not reuse 161, 8080, or any port already in the cluster services.

**Phase:** HTTP simulator implementation.

---

### Pitfall 13: Scenario Registry Default — Simulator Starts in Unknown State

**What goes wrong:** If the simulator starts without a defined default scenario, the `DynamicInstance.getValue()` callback may return a value that happens to breach a threshold. The first poll cycle after deployment triggers an unintended command before any test script has run.

**Prevention:** Define a named `"baseline"` scenario as the default at startup. The baseline scenario returns values that are within all test tenant thresholds. The K8s Deployment sets `INITIAL_SCENARIO=baseline` via env var (or the simulator hardcodes it). Every test script begins with `POST /scenario/baseline` to reset state before proceeding.

**Phase:** HTTP simulator implementation, scenario registry design.

---

### Pitfall 14: GraceMultiplier Range Validation Rejects Test Configs

**What goes wrong:** `MetricSlotOptions.GraceMultiplier` was constrained to range `[2.0, 5.0]` in v1.9. A test tenant config that sets `GraceMultiplier = 1.5` (to tighten the staleness window for faster test iteration) will be rejected by `TenantVectorOptionsValidator` with an error log. The tenant is skipped, the routing index has no entries for the test device, and all poll data is silently dropped with no command dispatch.

**Prevention:** Use `GraceMultiplier >= 2.0` in all test tenant configs. The minimum valid value (2.0) provides a 30s staleness window for a 15s poll interval, which is acceptable for E2E testing. If faster staleness detection is needed for staleness-testing scenarios, use `IntervalSeconds` adjustment instead.

**Detection:** `TenantVectorWatcherService` logs a validation error mentioning `GraceMultiplier` out of range. `TenantVectorRegistry.TenantCount` is lower than expected. No routing fan-out for the test device (no `snmp_tenantvector_routed_total` increment for the device).

**Phase:** Tenant config authoring.

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| HTTP simulator endpoint | Event loop blocking (Pitfall 1) | Use aiohttp or asyncio.start_server; never blocking I/O in handler |
| Simulator startup scenario | Unknown default state (Pitfall 13) | Hardcode "baseline" scenario, emit startup log |
| Tenant threshold config design | Sentinel triggers equality threshold (Pitfall 2) | Avoid Min=Max=0; use range thresholds with safe baseline values |
| Tenant threshold config design | Resolved gate stops cascade (Pitfall 11) | Verify Resolved metrics have in-range thresholds for baseline scenario |
| Time series depth > 1 | Tests assert before series fills (Pitfall 3) | wait = TimeSeriesSize * IntervalSeconds + OTel interval |
| Test script sequencing | Suppression bleeds between scenarios (Pitfall 4) | Use distinct tenant names per scenario or space 75s+ apart |
| Multi-tenant same-priority tests | Priority gate blocks test tenant (Pitfall 7) | Set all test tenants to same Priority; verify no higher-priority stale tenants |
| Prometheus assertions | Per-pod sent vs. cluster total mismatch (Pitfall 5) | Use sum() without pod filter for sent assertions |
| Baseline snapshot timing | OTel export delay inflates delta (Pitfall 6) | 30s settle wait after ConfigMap apply before snapshot |
| Scenario switch timing | OID change not yet polled (Pitfall 10) | 15s sleep after HTTP switch before poll-until |
| Log-based assertions | Single pod check misses other replicas (Pitfall 9) | Iterate all pods; prefer Prometheus for command assertions |
| ConfigMap threshold change | Series truncation if TimeSeriesSize changes (Pitfall 8) | Fix TimeSeriesSize per tenant, do not change mid-test |
| GraceMultiplier in test config | Validation rejects < 2.0 (Pitfall 14) | Keep GraceMultiplier >= 2.0 in all test configs |

## Sources

- Source code verified: `src/SnmpCollector/Jobs/SnapshotJob.cs` (4-tier evaluation logic, advance gate)
- Source code verified: `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` (sentinel value, CopyFrom truncation)
- Source code verified: `src/SnmpCollector/Pipeline/SuppressionCache.cs` (lazy TTL, ConcurrentDictionary key structure)
- Source code verified: `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` (Reload, carry-over, priority bucket ordering)
- Source code verified: `src/SnmpCollector/Services/CommandWorkerService.cs` (leader gate, command sent counter placement)
- Source code verified: `simulators/e2e-sim/e2e_simulator.py` (asyncio event loop structure, open_dispatcher)
- Source code verified: `tests/e2e/lib/prometheus.sh` (snapshot_counter, poll_until, 30s timeout, 3s interval)
- Source code verified: `tests/e2e/scenarios/28-tenantvector-routing.sh` (multi-pod log iteration pattern)
- Source code verified: `src/SnmpCollector/Configuration/MetricSlotOptions.cs` (GraceMultiplier field, IntervalSeconds default)

---

---

# Pitfalls Research — Tenant Vector Metrics Milestone

**Domain:** Adding tenant-level per-evaluation-cycle metrics to existing OTel/Prometheus monitoring system (3-replica K8s)
**Researched:** 2026-03-22
**Confidence:** HIGH — all pitfalls grounded directly in codebase: MetricRoleGatedExporter.cs, PipelineMetricService.cs, TelemetryConstants.cs, SnapshotJob.cs, ServiceCollectionExtensions.cs, TenantVectorRegistry.cs, CardinalityAuditService.cs.

---

## Critical Pitfalls

---

### Pitfall TV-1: Counter Double-Counting from All-Instances Export

**What goes wrong:**
All 3 replicas run `SnapshotJob` independently and each increments tenant counters on every 15s cycle. If Grafana queries use a raw counter value or a `rate()` expression without cross-instance aggregation, the displayed rate is 3x the true system rate. A dashboard showing "commands dispatched per minute" reads 30/min when the actual rate is 10/min.

**Why it happens:**
The new tenant counters belong on `TelemetryConstants.MeterName = "SnmpCollector"` (all-instances export, same as `snmp.command.dispatched` and all existing pipeline counters). Engineers writing PromQL apply the same patterns used for single-instance services and omit `sum by (tenant_id, priority)`. The mistake is invisible until someone notices the numbers are exactly 3x the values logged in application logs.

**How to avoid:**
All PromQL for tenant counters must aggregate across instances before presenting a per-tenant total:

```promql
# Correct: rate of commands dispatched, per tenant
sum by (tenant_id, priority) (
  rate(snmp_tenant_commands_dispatched_total[30s])
)

# Wrong: triple-counts without aggregation
rate(snmp_tenant_commands_dispatched_total[30s])
```

Establish this as a standard at the start of the dashboard build phase, not after panels are written.

**Warning signs:**
- Dashboard numbers are exactly 3x values seen in application logs for a known single-tenant test.
- Any Grafana panel on a tenant counter lacks `sum by (tenant_id)` in its PromQL.
- `rate()` window is shorter than twice the SnapshotJob cycle interval (see Pitfall TV-7).

**Phase to address:**
Instrument definition phase — document the PromQL aggregation requirement before any dashboard panel is written.

---

### Pitfall TV-2: Tenant State Gauge Registered on the Wrong Meter

**What goes wrong:**
The `snmp_tenant_state` enum gauge is placed on `TelemetryConstants.MeterName` ("SnmpCollector") instead of `TelemetryConstants.LeaderMeterName` ("SnmpCollector.Leader"). All 3 replicas export the gauge. Since each replica independently evaluates tenant state via `SnapshotJob.EvaluateTenant()`, followers may export a different state integer than the leader at the same timestamp. Prometheus receives 3 conflicting series. Any aggregation over them (`max`, `min`, `avg`) produces either the wrong value or a meaningless fractional result.

**Why it happens:**
`PipelineMetricService` uses `TelemetryConstants.MeterName` for all 15 instruments. The path of least resistance when adding a new instrument is to add it to `PipelineMetricService`, which silently puts it on the wrong meter. There is no compile-time check that enforces meter assignment per instrument type.

**How to avoid:**
Tenant state gauge must be created on `LeaderMeterName`:
- Do NOT add it to `PipelineMetricService`.
- Create a separate `TenantMetricService` (or similar) using `meterFactory.Create(TelemetryConstants.LeaderMeterName)` for the state gauge.
- `MetricRoleGatedExporter` will then suppress it on followers automatically — no exporter code changes needed (see Pitfall TV-3).

PromQL for the leader-gated gauge is then clean:
```promql
snmp_tenant_state{tenant_id="alpha"}
```

**Warning signs:**
- Prometheus shows 3 time series for `snmp_tenant_state{tenant_id="..."}` with potentially different values at the same timestamp.
- Any `avg by (tenant_id)` on the state gauge (produces fractional values like `1.67`).
- Dashboard state column shows unexpected values during failover (leader changes, state series briefly conflicts).

**Phase to address:**
Instrument definition phase — meter assignment per instrument type must be decided and documented before `CreateGauge` is called.

---

### Pitfall TV-3: Attempting to Bypass MetricRoleGatedExporter by Modifying It

**What goes wrong:**
A developer sees that new tenant instruments need to bypass gating (counters must export from all replicas) and concludes the exporter needs modification — adding an instrument-name whitelist, a second gating condition, or a flag. They change `MetricRoleGatedExporter.cs`, introducing complexity and a regression risk for all existing instruments.

**Why it happens:**
The exporter's design is meter-name-based, not instrument-name-based. The gating check at line 56 is:
```csharp
if (!string.Equals(metric.MeterName, _gatedMeterName, StringComparison.Ordinal))
```
Any instrument on `"SnmpCollector"` passes through on followers regardless of its name. Any instrument on `"SnmpCollector.Leader"` is suppressed on followers. Meter selection at instrument creation time is the complete control surface — no exporter changes are needed or desired.

**How to avoid:**
Understand the two-meter model before writing any instrument code:

| Meter | Exported by | Use for |
|-------|-------------|---------|
| `"SnmpCollector"` | All instances | Counters, histograms where per-instance values are valid and summed in PromQL |
| `"SnmpCollector.Leader"` | Leader only | State gauges, any instrument with a single authoritative value |

New tenant instruments map as follows:
- Counters (`commands_dispatched`, `commands_suppressed`, `commands_failed`, `cycle_count`, `stale_count`, `resolved_count`, `healthy_count`) → `MeterName`
- Evaluation duration histogram → `MeterName` (per-replica timing is valid; aggregate with `sum by (le, tenant_id)` for P99)
- State gauge (`tenant_state`) → `LeaderMeterName`

A PR that modifies `MetricRoleGatedExporter.cs` for this milestone is a red flag.

**Warning signs:**
- `MetricRoleGatedExporter.cs` appears in the PR diff.
- New instrument name appears in an `if` condition inside `Export()`.
- State gauge shows 3 series in Prometheus.

**Phase to address:**
Instrument definition phase — class and meter assignment design, before any instrument creation code is written.

---

### Pitfall TV-4: Histogram Bucket Boundaries Don't Cover the Actual Duration Range

**What goes wrong:**
The OTel .NET SDK default histogram bucket boundaries are `[0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000]` ms. For a per-tenant evaluation duration histogram, where the evaluation loop (grace check, staleness check, threshold evaluation, command dispatch) typically completes in 1–20ms, nearly all observations land in the `[0, 5]` bucket. `histogram_quantile(0.99, ...)` then returns exactly `5.0` — the bucket boundary — not a useful percentile.

The existing `snmp.snapshot.cycle_duration_ms` uses default buckets (adequate for a full multi-tenant cycle that may span 1–500ms). A per-tenant evaluation histogram needs explicit fine-grained boundaries.

**Why it happens:**
`_meter.CreateHistogram<double>(name, unit: "ms")` uses defaults unless an `AddView` override is registered. Developers copy the existing histogram pattern without checking whether default buckets are appropriate for the measurement range.

**How to avoid:**
Register an `AddView` in `ServiceCollectionExtensions.AddSnmpTelemetry` before the `AddMeter` calls:

```csharp
metrics.AddView(
    instrumentName: "snmp.tenant.eval_duration_ms",
    new ExplicitBucketHistogramConfiguration
    {
        Boundaries = [0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 50.0, 100.0, 250.0, 500.0]
    });
```

Rationale for these boundaries:
- 0.5–2ms: grace check + staleness iteration only (no command)
- 2–10ms: full tier 1–3 evaluation
- 10–50ms: tier 4 command dispatch, including suppression cache write
- 50–500ms: anomalously slow (worth surfacing; triggers investigation)

For the histogram PromQL in Grafana to aggregate across instances correctly:
```promql
histogram_quantile(0.99,
  sum by (le, tenant_id, priority) (
    rate(snmp_tenant_eval_duration_ms_bucket[2m])
  )
)
```

**Warning signs:**
- Prometheus `le` label values for `snmp_tenant_eval_duration_ms_bucket` show `5`, `10`, `25` — these are default boundaries, not the custom ones.
- `histogram_quantile(0.99, ...)` returns exactly `5.0` or `10.0` (boundary snapping, not interpolation).
- Grafana heatmap shows all observations concentrated in one row.

**Phase to address:**
Instrument definition phase — `AddView` registration in `AddSnmpTelemetry` must be in the same PR as the histogram instrument creation.

---

### Pitfall TV-5: Enum Gauge — Integer Values Not Mapped to Labels in Grafana

**What goes wrong:**
Prometheus stores gauge values as floats. `snmp_tenant_state{tenant_id="alpha"}` returns `2.0`, not `"Unresolved"`. A Grafana table showing raw numeric values is operationally useless for an on-call engineer. The correct rendering requires Grafana Value Mappings, but these are configured per panel and are easy to omit or misconfigure (wrong range, missing value).

A secondary mistake: using `avg by (tenant_id)` instead of `max by (tenant_id)` on the state gauge. If the series briefly has multiple values due to export timing, `avg` returns a non-integer float.

**Why it happens:**
Two patterns for enum-as-gauge exist in the Prometheus ecosystem, and they are frequently confused:

- **Approach A (recommended):** Single gauge per `(tenant_id, priority)` with integer value encoding (0=Healthy, 1=Resolved, 2=Unresolved). Grafana Value Mappings render the integer as labelled text with colour.
- **Approach B (not recommended here):** State-set pattern — one series per possible state value, with `value=1` for the active state. Triples series count for no benefit given the state gauge is leader-gated.

**How to avoid:**
Use Approach A. Define state encoding as constants to prevent integer literals scattered through the codebase:

```csharp
// In TelemetryConstants.cs or a dedicated TenantStateConstants class
public const double TenantStateHealthy    = 0.0;
public const double TenantStateResolved   = 1.0;
public const double TenantStateUnresolved = 2.0;
```

Grafana table panel configuration:
- PromQL: `max by (tenant_id, priority) (snmp_tenant_state)` (leader-only export, `max` collapses cleanly)
- Field override for `snmp_tenant_state`: `Unit = none`
- Value Mappings: `0 → Healthy` (green), `1 → Resolved` (orange), `2 → Unresolved` (red)

Do not use `avg` — use `max` or `last_over_time` for the state display query.

**Warning signs:**
- Grafana state column shows `0`, `1`, `2` as raw numbers.
- State cell shows blank/null for some tenants — Value Mapping range does not cover a returned value.
- `avg by (tenant_id)` in the state query PromQL.
- Integer literals `0.0`, `1.0`, `2.0` appear directly in `gauge.Record()` call sites.

**Phase to address:**
Instrument definition phase (define constants) and dashboard implementation phase (configure Value Mappings).

---

### Pitfall TV-6: Cardinality Estimate Does Not Include Tenant Instruments

**What goes wrong:**
`CardinalityAuditService` computes `devices × oidDimension × 2 instruments × 2 sources` and warns if the estimate exceeds 10,000 series. It does not account for tenant instruments. With 4 tenants × 8 instruments, the tenant dimension adds 32 series — negligible now. However, the audit log gives a false sense of completeness: operations teams relying on it to size Prometheus storage do not see the tenant series contribution. If tenant count grows (production may have 20–50 tenants) and more instruments are added, the omission becomes material.

Separately: if someone accidentally uses a high-cardinality value as a label dimension alongside `tenant_id` (e.g., adding `device_name` or `metric_name` to tenant counters), the audit will not catch it because the formula does not include tenant instruments at all.

**Why it happens:**
`CardinalityAuditService` was written before tenant instruments existed. It is not automatically updated when new instrument families are added.

**How to avoid:**
Update the cardinality formula in `CardinalityAuditService.AuditCardinality()` to include the tenant instrument contribution:

```csharp
var tenantInstrumentCount = 8;  // 6 counters + 1 histogram + 1 state gauge
var tenantSeriesEstimate = _tenantRegistry.TenantCount * tenantInstrumentCount;
var totalEstimate = existingEstimate + tenantSeriesEstimate;
```

Keep `tenant_id` and `priority` as the only label dimensions on tenant instruments. Do not add `device_name`, `ip`, or `metric_name` — those are already captured on `snmp_gauge`/`snmp_info`.

**Warning signs:**
- Startup cardinality log shows only the `devices × OIDs × 2 × 2` formula with no mention of tenant instruments.
- A PR proposes adding `device_name` as a label on a tenant counter.
- `tenant_id` values in Prometheus include dynamic strings (IPs, OID names) rather than stable configured names.

**Phase to address:**
Instrument definition phase — update `CardinalityAuditService` in the same PR as the new instrument creation.

---

### Pitfall TV-7: Per-Cycle Counter Rate() Window Too Narrow

**What goes wrong:**
Tenant cycle counters increment once per SnapshotJob cycle per tenant — every 15 seconds. A `rate()` window shorter than 2× the cycle interval may contain zero or one increment, producing a near-zero or spiky rate that looks like the metric is broken rather than healthy.

`snmp_tenant_commands_dispatched_total` increments only when Tier 4 is reached. On a healthy system it may not increment for hours. A dashboard showing `rate(snmp_tenant_commands_dispatched_total[30s]) == 0` during normal operation will be misread as "metrics not working."

**Why it happens:**
Engineers calibrate `rate()` windows for high-frequency counters (events per second). A 2-minute window on a 15-second-cycle counter is correct; a 30-second window on the same counter may catch 0–2 increments, producing a volatile, misleading result. The mistake is applying a rate window that was right for `snmp.event.published` (high-frequency) without adjusting for the much lower frequency of per-cycle counters.

**How to avoid:**
Use `rate()` windows of at least `4 × cycle_interval` (60s for a 15s cycle) for tenant cycle counters. For cumulative totals, use the raw counter with `sum by (tenant_id)`. For increment-rate panels, document the expected zero-rate:

```promql
# Commands dispatched rate — zero on healthy system (Tier 4 only reached when threshold violated)
sum by (tenant_id, priority) (
  rate(snmp_tenant_commands_dispatched_total[2m])
)
```

Add panel description: "Expected to be 0 on a healthy system. Non-zero values indicate threshold violations triggering command dispatch."

**Warning signs:**
- `rate()` window in any tenant counter panel is shorter than `2 × SnapshotJobOptions.IntervalSeconds`.
- Dashboard shows constant zero for tenant counters and engineer concludes the metric is not recording.
- Dashboard shows extreme spikes alternating with zero — characteristic of a rate window that catches exactly 1 increment sometimes and 0 increments other times.

**Phase to address:**
Dashboard implementation phase — PromQL window sizing and panel descriptions must account for the 15s cycle before panels are finalised.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Add tenant counters to existing `PipelineMetricService` | No new class, faster to implement | Mixes pipeline metrics (no tenant label) with tenant metrics (tenant label); `PipelineMetricService` already owns 15 instruments and grows unwieldy; state gauge cannot be added here (wrong meter) | Never — create `TenantMetricService` |
| Register state gauge on `MeterName` | Avoids meter decision | 3 conflicting series per tenant in Prometheus; PromQL requires instance filtering permanently | Never |
| Use default histogram bucket boundaries | Zero configuration | P99 resolves to bucket boundary (5.0 or 10.0); histogram useless for percentile analysis in sub-50ms range | Never for per-tenant evaluation histogram |
| Hardcode integer state values in `SnapshotJob` | 2 minutes saved | State encoding scattered through codebase; silent mismatch if Grafana Value Mappings are configured from a different reference | Never |
| Skip updating `CardinalityAuditService` | ~10 lines saved | Tenant series invisible in startup audit; first indication of cardinality problem is Prometheus performance degradation | Acceptable if tenant count is documented as ≤10 and a follow-up task is filed |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| OTel `AddView` for histogram buckets | Calling `AddView` after `AddMeter` in the `WithMetrics` lambda — OTel .NET may not apply the view to an already-registered instrument | Register `AddView` calls before the `AddMeter` calls in the same `WithMetrics` lambda |
| Prometheus `histogram_quantile` with multi-instance histogram | `histogram_quantile(0.99, rate(bucket[2m]))` without `sum by (le, tenant_id)` — mixes buckets from 3 instances, produces incorrect percentile | Always: `histogram_quantile(0.99, sum by (le, tenant_id, priority) (rate(...bucket[2m])))` |
| Grafana table for leader-gated state gauge | `max by (tenant_id, priority)` fails to find any series if the leader pod is not yet elected | Add `or vector(0)` fallback, or document expected blank behaviour during leader election startup |
| `MetricRoleGatedExporter` `ParentProvider` reflection | The `_parentProviderPropagated` flag is set once per exporter instance; if any new meter is added after the flag is set, the inner exporter already has the parent provider | No action needed — the flag pattern is correct; do not attempt to reset it |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| `TagList` allocation per counter increment | `TagList` with ≤8 tags is stack-allocated in OTel .NET; >8 tags falls back to heap allocation | Keep tag count on all tenant instruments to `(tenant_id, priority)` = 2 tags; do not add more | Breaks allocation budget if tag count exceeds 8 |
| Per-tenant histogram `Record` inside `Task.WhenAll` parallel evaluation | `SnapshotJob` runs tenant evaluation in parallel; histogram `Record` must be thread-safe | OTel .NET histogram instruments are designed for concurrent use (confirmed in SDK source); safe as-is | Not a risk with current SDK |
| `priority` label as a proxy for per-tenant unique values | If each tenant has a unique priority integer, `priority` becomes an alias for `tenant_id` and provides no grouping benefit while doubling series count | Document that `priority` reflects shared priority levels, not per-tenant unique identifiers; validate that operators do not assign unique priorities to every tenant | Breaks grouping utility as tenant count grows |

---

## "Looks Done But Isn't" Checklist

- [ ] **State gauge meter assignment:** Confirm `snmp_tenant_state` is created on `LeaderMeterName`. Check: query Prometheus and verify `snmp_tenant_state` shows only 1 series per `(tenant_id, priority)` pair (not 3).
- [ ] **Counter PromQL aggregation:** Every Grafana panel querying tenant counters uses `sum by (tenant_id, priority)`. Check: raw counter query returns `3 × tenant_count` series; post-aggregation returns `tenant_count` series.
- [ ] **Histogram bucket boundaries applied:** Confirm custom boundaries are active. Check: `snmp_tenant_eval_duration_ms_bucket` in Prometheus has `le` label values matching the `AddView` boundaries (e.g., `0.5`, `1.0`), not default values (`5.0`, `10.0`, `25.0`).
- [ ] **State encoding constants defined:** No numeric literals in `gauge.Record()` call sites. Check: grep for `gauge.Record` invocations in `TenantMetricService` — all use named constants.
- [ ] **`CardinalityAuditService` updated:** Startup log shows tenant instrument series estimate alongside existing device/OID estimate. Check: `kubectl logs` on fresh start and grep for "tenant" in cardinality log line.
- [ ] **MetricRoleGatedExporter unchanged:** `MetricRoleGatedExporter.cs` not modified in this milestone's PR. Check: `git diff HEAD~1 -- src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` shows no changes.
- [ ] **Rate window documented on dashboard panels:** Each counter panel PromQL uses `[2m]` or longer window; panel description states "zero rate = healthy system" for dispatch/failure counters.

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Counter double-counting (TV-1) | LOW | Update Grafana dashboard PromQL to add `sum by (tenant_id, priority)`; no code or redeploy needed |
| State gauge on wrong meter (TV-2) | MEDIUM | Move gauge creation to `LeaderMeterName` service; redeploy; old 3-series data persists in Prometheus until retention expires |
| Wrong histogram buckets (TV-4) | MEDIUM | Add `AddView` with correct boundaries; redeploy; historical histogram data is incompatible with new bucket layout — old percentiles are incorrect until data rolls off |
| State encoding undocumented (TV-5) | LOW | Define constants in `TelemetryConstants.cs`; replace literals; Prometheus history unaffected |
| Cardinality estimate incomplete (TV-6) | LOW | Update `CardinalityAuditService` formula; redeploy; no data loss |
| Cardinality explosion from unbounded label (TV-6 variant) | HIGH | Remove high-cardinality label; redeploy; Prometheus TSDB may require manual series deletion or compaction to recover memory |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Counter double-counting (TV-1) | Instrument definition — PromQL standards doc | Raw counter query returns 3× tenant count; aggregated query returns tenant count |
| State gauge on wrong meter (TV-2) | Instrument definition — meter assignment decision | Prometheus shows exactly 1 series per tenant for state gauge |
| MetricRoleGatedExporter unchanged (TV-3) | Instrument definition — no exporter code changes | `git diff` shows no changes to `MetricRoleGatedExporter.cs` |
| Histogram bucket sizing (TV-4) | Instrument definition — `AddView` in `AddSnmpTelemetry` | Prometheus `le` values for eval histogram match custom boundaries |
| Enum gauge integer mapping (TV-5) | Instrument definition (constants) + dashboard (Value Mappings) | Grafana state column shows text labels with correct colours |
| Cardinality audit update (TV-6) | Instrument definition — same PR as instrument creation | Startup log includes tenant series count |
| Rate window sizing (TV-7) | Dashboard implementation — window and description per panel | All tenant counter panels use `[2m]` or longer; no panel uses window < 30s |

---

## Sources (Tenant Vector Metrics section)

- `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` — meter-name gating logic (line 56), leader-only vs. all-instances design
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — `MeterName` and `LeaderMeterName` constants
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — existing 15 instruments all on `MeterName`; counter tag pattern (`device_name` tag, `Add(1, tagList)`)
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — `TierResult` enum (Healthy/Resolved/Unresolved), 15s cycle, parallel tenant evaluation via `Task.WhenAll`
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — `PeriodicExportingMetricReader` at 15s; `MetricReaderTemporalityPreference.Cumulative` (required for Prometheus `rate()`); `AddView` hook point in `WithMetrics` lambda
- `src/SnmpCollector/Pipeline/CardinalityAuditService.cs` — existing cardinality formula (devices × OIDs × 2 × 2); does not include tenant instruments
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `Tenant.Id` source (configured name or `"tenant-{i}"` fallback); tenant count bounded by configuration
- OTel .NET SDK default histogram boundaries: `[0,5,10,25,50,75,100,250,500,750,1000,2500,5000,7500,10000]` — HIGH confidence, cross-referenced with OTel specification and Prometheus default bucket behaviour
