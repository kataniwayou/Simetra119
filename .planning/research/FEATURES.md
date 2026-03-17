# Feature Landscape: E2E Test Scenarios for SnapshotJob 4-Tier Tenant Evaluation

**Domain:** SNMP monitoring agent — E2E test infrastructure for closed-loop tenant evaluation
**Researched:** 2026-03-17
**Confidence:** HIGH — derived from full source analysis of SnapshotJob, MetricSlotHolder,
PriorityGroup, SuppressionCache, PipelineMetricService, existing E2E simulator, and all 28
existing E2E test scenarios.

---

## Scope

This research defines test scenarios for E2E validation of the already-built SnapshotJob 4-tier
evaluation flow. The system under test is running in a K8s cluster. Validation is via pod logs
(structured Debug/Information lines emitted by SnapshotJob) and Prometheus metrics
(`snmp.command.sent`, `snmp.command.failed`, `snmp.command.suppressed`, plus existing counters).

The E2E simulator (`simulators/e2e-sim/e2e_simulator.py`) needs an HTTP control endpoint added
so test scripts can switch OID values between scenarios without restarting the pod. All
scenarios share a single tenant config applied as a K8s ConfigMap.

**What already exists (infrastructure the new tests depend on):**
- SnapshotJob with full 4-tier evaluation, advance gate, liveness stamping
- MetricSlotHolder with cyclic time series (`TimeSeriesSize`), atomic `ReadSeries()`
- PriorityGroup traversal: parallel within group, sequential across groups
- CommandWorkerService executing SNMP SET via `Messenger.SetAsync`
- SuppressionCache keyed by `{tenantId}:{Ip}:{Port}:{CommandName}`
- PipelineMetricService with `snmp.command.sent/failed/suppressed` and
  `snmp.snapshot.cycle_duration_ms`
- E2E simulator with 9 static OIDs, no HTTP control endpoint yet
- 28 existing E2E bash scenarios using Prometheus polling and log grep patterns

**What is new for this milestone:**
- HTTP control endpoint on the E2E simulator (switch scenario state via `POST /scenario/{name}`)
- Tenant ConfigMap definitions for each test scenario
- Bash E2E scenario scripts (following patterns in `tests/e2e/scenarios/`)
- Prometheus and log assertion helpers as needed

---

## Table Stakes

Scenarios that MUST pass for the milestone to be complete. Each scenario corresponds to an
observable, unambiguous contract in the 4-tier evaluation code.

---

### TS-SC-01: Single Tenant — Healthy (No Violations)

**What this validates:** Tier 3 evaluate gate correctly returns `Healthy` when all Evaluate
holders are in range. No commands issued.

**Pre-conditions:**
- One tenant, one Evaluate metric with `Threshold: { Min: 0, Max: 100 }`, one Resolved metric
  with `Threshold: { Min: 0, Max: 1 }` (link-up check pattern)
- Simulator serving Evaluate OID value `50` (in range), Resolved OID value `1` (in range)
- Suppression window: 60s

**Trigger:** Wait two full SnapshotJob cycles (at least 30s for a 15s interval job)

**Expected tier flow:**
1. Tier 1: all holders fresh → pass
2. Tier 2: Resolved value `1` in `[0,1]` → NOT violated → not all Resolved violated → continue
3. Tier 3: Evaluate value `50` in `[0,100]` → NOT violated → not all Evaluate violated → Healthy
4. Tier 4: not reached

**Log assertions (pod logs):**
- Must contain: `tier=3 — not all evaluate metrics violated, no action` for this tenant
- Must NOT contain: `tier=4` for this tenant

**Metric assertions:**
- `snmp_command_sent_total` delta = 0 over test window
- `snmp_command_suppressed_total` delta = 0 over test window

**Edge cases:**
- `TimeSeriesSize: 1` (default) — single sample; result must be identical whether series has one
  or multiple samples
- Boundary value: Evaluate value exactly at `Max=100` — still not violated (strict inequality)

---

### TS-SC-02: Single Tenant — Evaluate Violated (All Samples)

**What this validates:** Tier 3 fires correctly, Tier 4 enqueues command and counter increments.

**Pre-conditions:**
- One tenant, one Evaluate metric with `Threshold: { Max: 95 }` (CPU headroom pattern),
  `TimeSeriesSize: 1`, one Resolved metric with `Threshold: { Min: 0, Max: 1 }`
- Simulator serving Evaluate OID value `97` (above Max=95 → violated), Resolved value `1`
- Suppression window: 300s (long, so suppression does not interfere with first-fire assertion)

**Trigger:** Wait for first SnapshotJob cycle to complete after OID value is set

**Expected tier flow:**
1. Tier 1: fresh → pass
2. Tier 2: Resolved value `1` in `[0,1]` → not violated → continue
3. Tier 3: Evaluate value `97` > Max `95` → violated → all Evaluate violated → proceed to Tier 4
4. Tier 4: command not in suppression cache → enqueue → `snmp.command.sent` incremented

**Log assertions:**
- Must contain: `tier=4 — commands enqueued, count=1` for this tenant
- Must contain: `tier=2 — resolved not all violated, proceeding to evaluate check`
- Must NOT contain: `tier=1 stale`

**Metric assertions:**
- `snmp_command_sent_total` delta >= 1 within 60s of scenario start
- `snmp_command_failed_total` delta = 0
- `snmp_command_suppressed_total` delta = 0

**Edge cases:**
- Boundary: Evaluate value exactly at `Max=95` — NOT violated (strict inequality `>` not `>=`)
  so command must NOT fire when simulator serves `95`

---

### TS-SC-03: Single Tenant — Resolved Gate Fires (ConfirmedBad, No Command)

**What this validates:** Tier 2 resolved gate correctly stops evaluation when ALL Resolved holders
are violated. No command must be issued even though device is clearly in bad state.

**Pre-conditions:**
- One tenant, one Resolved metric with `Threshold: { Min: 1 }` (link-up: value 0 = link down =
  all bad), one Evaluate metric with `Threshold: { Max: 95 }`
- Simulator serving Resolved OID value `0` (below Min=1 → violated), Evaluate OID value `97`
- Suppression window: 300s

**Trigger:** Wait for first SnapshotJob cycle

**Expected tier flow:**
1. Tier 1: fresh → pass
2. Tier 2: Resolved value `0` < Min `1` → violated. All Resolved violated → ConfirmedBad. Stop.
3. Tier 3: NOT reached
4. Tier 4: NOT reached

**Log assertions:**
- Must contain: `tier=2 — all resolved violated, device confirmed bad, no commands`
- Must NOT contain: `tier=4`
- Must NOT contain: `tier=3`

**Metric assertions:**
- `snmp_command_sent_total` delta = 0
- `snmp_command_suppressed_total` delta = 0

**Edge cases:**
- Resolved exactly at Min=1 (boundary): NOT violated, so Tier 2 does not block, Tier 3 fires

---

### TS-SC-04: Single Tenant — Suppression Window Prevents Repeat Command

**What this validates:** After a command fires, the suppression cache prevents it from firing
again within the window. After the window expires, it fires again.

**Pre-conditions:**
- Same as TS-SC-02 (evaluate violated, healthy Resolved)
- Suppression window: 30s (short window, detectable in test)

**Trigger phase A:** Allow one command to fire (verify `snmp.command.sent` delta >= 1)

**Trigger phase B:** Immediately check next 2 cycles while Evaluate is still violated

**Expected behavior phase B:**
- `snmp.command.suppressed` increments on subsequent cycles within the window
- `snmp.command.sent` does NOT increment a second time during the window

**Trigger phase C:** Wait for suppression window to expire (>30s). Evaluate still violated.

**Expected behavior phase C:**
- `snmp.command.sent` increments again (second fire)

**Log assertions phase B:**
- Must contain: `Command {CommandName} suppressed for tenant {TenantId}`

**Metric assertions phase B:**
- `snmp_command_suppressed_total` delta >= 1 while in window
- `snmp_command_sent_total` no additional increment during window

---

### TS-SC-05: Single Tenant — Staleness Detection Blocks All Tiers

**What this validates:** Tier 1 staleness check correctly blocks evaluation when any holder
has data older than `IntervalSeconds * GraceMultiplier`. No command, no threshold check.

**Pre-conditions:**
- One tenant, one Evaluate metric with `IntervalSeconds: 10`, `GraceMultiplier: 2.0`
  (staleness threshold = 20s), `Threshold: { Max: 95 }`
- Simulator serving Evaluate OID value `97` (violated)
- SnapshotJob running normally (Evaluate is violated, command fires)

**Trigger:** Stop the poll job for this device (or change device config so no new polls arrive)
and wait 25s (beyond the 20s staleness threshold)

**Expected behavior after staleness onset:**
1. Tier 1: holder age > 20s → Stale → stop evaluation
2. Tier 3: NOT reached
3. Tier 4: NOT reached — no new commands even though Evaluate value is still violated

**Log assertions:**
- Must contain: `tier=1 stale — skipping threshold checks` for this tenant

**Metric assertions:**
- `snmp_command_sent_total` stops incrementing after staleness onset
- Counter plateau confirms no new commands issued

**Staleness recovery:**
- Resume polling. Within one poll cycle + one SnapshotJob cycle, staleness clears.
- Must contain: `tier=2 — resolved not all violated` (back to normal flow)
- `snmp_command_sent_total` resumes incrementing

**Implementation constraint:** This scenario requires the E2E simulator to stop serving responses
for a specific OID (or the poll job to be paused). The HTTP control endpoint on the simulator
should support a `stale` scenario that stops responding, with a timeout.

---

### TS-SC-06: Two Tenants Same Priority — Independent Evaluation

**What this validates:** Within a single priority group, two tenants are evaluated in parallel
and their results are independent. Healthy tenant does not suppress Commanded tenant.

**Pre-conditions:**
- Two tenants at Priority=1 (same group):
  - Tenant A: Evaluate OID in range (healthy) — no command expected
  - Tenant B: Evaluate OID violated — command expected
- Both tenants have Resolved metrics in range

**Trigger:** Wait for one SnapshotJob cycle

**Expected behavior:**
- Tenant A: Tier 3 — Healthy, no command
- Tenant B: Tier 3/4 — Commanded, command enqueued

**Advance gate check:**
- After this group: Tenant A = Healthy (NOT Commanded), so advance gate fails
- No lower-priority group should be evaluated in this cycle

**Log assertions:**
- Tenant A: `tier=3 — not all evaluate metrics violated, no action`
- Tenant B: `tier=4 — commands enqueued, count=1`
- If a priority 2 group exists: must NOT see any tier logs for that group in this cycle

**Metric assertions:**
- `snmp_command_sent_total` delta = 1 (only Tenant B fires)

---

### TS-SC-07: Two Tenants Different Priority — Advance Gate

**What this validates:** Sequential priority group traversal. Lower-priority group is only
evaluated when ALL tenants in higher-priority group are Commanded.

**Sub-scenario A — Higher priority Healthy, lower priority NOT evaluated:**

**Pre-conditions:**
- Tenant A at Priority=1: Evaluate OID in range (Healthy)
- Tenant B at Priority=2: Evaluate OID violated (would command)

**Expected behavior:**
- Group 1 evaluated: Tenant A = Healthy → advance gate fails → Group 2 NOT evaluated
- Tenant B does NOT receive commands

**Log assertions:**
- Tenant A: `tier=3` Healthy log
- Tenant B: NO tier logs (group never evaluated)

**Metric assertions:**
- `snmp_command_sent_total` delta = 0

**Sub-scenario B — Higher priority Commanded, lower priority evaluated:**

**Pre-conditions (switch simulator):**
- Tenant A at Priority=1: Evaluate OID now violated (will command)
- Tenant B at Priority=2: Evaluate OID violated (will command)

**Expected behavior:**
- Group 1: Tenant A = Commanded → advance gate passes
- Group 2: Tenant B = Commanded → command enqueued

**Log assertions:**
- Tenant A: `tier=4 — commands enqueued`
- Tenant B: `tier=4 — commands enqueued`

**Metric assertions:**
- `snmp_command_sent_total` delta = 2 (both tenants)

---

### TS-SC-08: Time Series All-Samples Check — Not All Samples Violated

**What this validates:** With `TimeSeriesSize > 1`, evaluation requires ALL samples in the
series to be violated, not just the most recent. One in-range sample prevents Tier 4 fire.

**Pre-conditions:**
- One tenant, Evaluate metric with `TimeSeriesSize: 3`, `Threshold: { Max: 95 }`
- Simulator serving OID value `97` (violated for 3 full poll cycles — fills series with all-violated)
- Suppression window: 300s

**Trigger phase A:** Verify command fires (all 3 samples in series are violated)
- `snmp_command_sent_total` delta >= 1

**Trigger phase B:** Change simulator OID to `50` (in range). Wait 2 poll cycles.
- Series now contains: `[97, 97, 50]` (most recent in-range)

**Expected behavior phase B:**
- Tier 3: series has one in-range sample → NOT all violated → Healthy
- No command fires

**Log assertions phase B:**
- `tier=3 — not all evaluate metrics violated, no action`

**Trigger phase C:** Change simulator OID back to `97`. Wait 3 poll cycles.
- Series now refilled: `[97, 97, 97]`

**Expected behavior phase C:**
- Tier 3: all 3 samples violated → proceed to Tier 4
- Command fires again (after suppression window if still active)

**Why this matters:** The all-samples check prevents transient single-cycle violations from
triggering commands. It is the primary false-positive suppression mechanism at the series level.

---

### TS-SC-09: Aggregate Metric as Evaluate Holder

**What this validates:** Aggregated metrics (sum/mean computed from multiple OIDs, written to a
synthetic MetricSlot via `SnmpSource.Synthetic`) work correctly as Evaluate holders in the
4-tier flow. Aggregated metrics are not polled directly — their slot is written by the
`AggregatedMetricDefinition` pipeline after raw OIDs are resolved.

**Pre-conditions:**
- One tenant, Evaluate metric referencing an aggregated metric name (e.g., `npb_total_rx_octets`
  — sum of per-port octets), `Threshold: { Max: 1000000 }`
- Simulator serving constituent OID values that produce an aggregate above the threshold

**Expected behavior:**
- Aggregate slot is written by the pipeline as `SnmpSource.Synthetic`
- Tier 1 staleness: `Synthetic` source is NOT excluded from staleness check (unlike `Trap`).
  If constituent polls stop, the aggregate goes stale within `IntervalSeconds * GraceMultiplier`.
- Tier 3: aggregate value > Max → violated → command fires

**Log assertions:**
- Normal Tier 3/4 logs, no special aggregate-specific lines expected

**Note:** This scenario confirms that the `Trap` staleness exclusion does not incorrectly apply
to `Synthetic` sources. The staleness code explicitly checks `Source == SnmpSource.Trap` only.

---

## Differentiators

Features that strengthen test coverage without being strictly required for correctness.

---

### D-SC-01: Cycle Duration Histogram Observable

**What:** Verify `snmp.snapshot.cycle_duration_ms` histogram is populated and reports sane values.

**Why:** The cycle duration is the only observable measure of SnapshotJob performance. If the job
is unexpectedly slow (blocking on I/O), this metric surfaces it.

**Validation:** Query Prometheus `histogram_quantile(0.99, snmp_snapshot_cycle_duration_ms_bucket)`
and assert value < 1000ms (1 second) for a cluster with 2 tenants. This is a sanity check, not
a strict SLA assertion.

---

### D-SC-02: Multiple Commands per Tenant (Partial Suppression)

**What:** One tenant with two commands. First command is in suppression window (from a prior
cycle), second command is not. Verify partial suppression: one fires, one is suppressed.

**Pre-conditions:** Manually set up suppression state by running one cycle, then verify that on
the next cycle within the suppression window only the second (not-yet-suppressed) command fires.

**Why differentiator:** This is covered by unit test `Execute_MultipleCommands_EachCheckedIndependently`
in `SnapshotJobTests.cs`. The E2E scenario adds confidence that the suppression key composition
(`{tenantId}:{Ip}:{Port}:{CommandName}`) is correctly formed end-to-end.

---

### D-SC-03: Liveness Not Broken by Evaluation Errors

**What:** Verify that the pod's liveness health check remains healthy across multiple SnapshotJob
cycles, including cycles where tenants are stale or ConfirmedBad.

**Why:** SnapshotJob stamps liveness in its `finally` block regardless of evaluation outcome.
If an exception escapes the evaluation loop without being caught, liveness would go stale and
K8s would restart the pod. This scenario confirms the `try/catch/finally` structure is intact.

**Validation:** Pod stays Running (not CrashLoopBackOff) after running scenarios 01-08 in
sequence. Liveness endpoint returns `200 OK` throughout.

---

## Anti-Features

Things to explicitly NOT include in the E2E test suite.

---

### AF-SC-01: Timing-Based Assertions Without Retry Loops

**What:** Do NOT assert that a command fires within a fixed `sleep N` seconds.

**Why Avoid:** The existing 28 E2E scenarios all use `poll_until` retry loops (checking
Prometheus every 5s for up to 90s). Sleeping a fixed duration (e.g., `sleep 20`) makes tests
brittle on slow clusters and unnecessarily slow on fast ones.

**What to Do Instead:** Use the `poll_until` helper from `tests/e2e/lib/common.sh` for all
counter-based assertions. For log assertions, use a polling grep loop with a timeout.

---

### AF-SC-02: SNMP SET Side-Effect Verification on Real Device

**What:** Do NOT attempt to verify that the SNMP SET command was actually applied on the
simulator (i.e., do not poll the OID after SET to confirm the value changed).

**Why Avoid:** The simulator's OID values are controlled by the HTTP control endpoint (test
pre-conditions), not by incoming SET commands. Verifying SET application would require the
simulator to act on SET requests, coupling test assertions to simulator behavior rather than
to the system under test (the SnapshotJob evaluation and dispatch path).

**What to Do Instead:** Validate via `snmp.command.sent` counter increment and `tier=4` log
presence. This confirms the full pipeline through command dispatch. The SNMP SET execution path
is separately validated by the existing `CommandWorkerService` unit tests.

---

### AF-SC-03: Testing With Production Tenant Config

**What:** Do NOT run E2E snapshot scenarios using the production `simetra-tenants.yaml` tenant
config (which references `obp-simulator` and `npb-simulator`).

**Why Avoid:** The production config includes thresholds tuned for specific simulator-generated
values (e.g., `obp_mean_power_L1` with `Threshold: { Min: -10, Max: 3 }`). Changing simulator
values to exercise tier transitions would disrupt other E2E scenarios running in the same cluster.

**What to Do Instead:** Deploy a separate `simetra-tenants-e2e-snapshot.yaml` ConfigMap that
references E2E simulator OIDs exclusively. Apply and restore around each snapshot scenario using
the same save/restore pattern as scenario 28.

---

### AF-SC-04: Validating ConfirmedBad Cascade (Multi-Group When Group-1 Is ConfirmedBad)

**What:** Do NOT build a test scenario where a ConfirmedBad tenant in Group 1 is expected to
allow cascade to Group 2.

**Why Avoid:** The advance gate blocks on `Stale OR Commanded`. `ConfirmedBad` is NOT `Commanded`,
so a group with any `ConfirmedBad` result does NOT advance to Group 2. This is correct behavior
and is covered by the unit tests. The E2E test for priority groups (TS-SC-07) is sufficient;
adding a ConfirmedBad cascade E2E adds complexity and misrepresents the intended semantics.

**What to Do Instead:** Document this as a code comment in TS-SC-07. The unit test
`Execute_CommandNotSuppressed_TryWriteWithCorrectFields` and related tests cover ConfirmedBad
return paths exhaustively.

---

## Feature Dependencies

```
Existing E2E infrastructure (tests/e2e/scenarios/, common.sh, prometheus.sh)
    |
    +--> All snapshot E2E scenarios (naming conventions, save/restore pattern, poll_until)

E2E simulator HTTP control endpoint (new)
    |
    +--> TS-SC-04 (suppression window timing requires switching OID values mid-test)
    +--> TS-SC-05 (staleness requires stopping OID responses)
    +--> TS-SC-07 (advance gate sub-scenario B requires switching values)
    +--> TS-SC-08 (all-samples check requires switching values between phases)
    +--> TS-SC-09 (aggregate verify requires controlling constituent OID values)

simetra-tenants-e2e-snapshot.yaml ConfigMap (new)
    |
    +--> All TS-SC-* scenarios

New Prometheus counters (already built in v2.0)
    |
    snmp.command.sent/failed/suppressed
    +--> TS-SC-02, 04, 05, 06, 07, 08 (counter delta assertions)

SnapshotJob tier debug logs (already built in v2.0)
    |
    tier=1/2/3/4 structured log lines
    +--> All TS-SC-* scenarios (log grep validation)
```

---

## Scenario Ordering Rationale

The implementation plan should build scenarios in this order:

1. **TS-SC-01 first** — establishes the baseline (no commands ever fire) and validates the full
   E2E scaffold (ConfigMap deploy, simulator control endpoint, log grep pattern) works before
   any positive assertion.

2. **TS-SC-02 second** — first positive assertion (command fires). Builds on SC-01 scaffolding.
   Validates the most direct path through the 4-tier tree.

3. **TS-SC-03 third** — validates the Resolved gate negative path. Confirms TS-SC-02 did not
   accidentally pass due to missing Resolved check.

4. **TS-SC-04** — requires SC-02 infrastructure. Adds suppression window timing.

5. **TS-SC-05** — requires simulator control endpoint to support stopping responses.
   Most complex timing scenario; build after simpler ones are stable.

6. **TS-SC-06 then TS-SC-07** — multi-tenant scenarios; require ConfigMap with 2 tenants.
   Build after single-tenant scenarios are stable.

7. **TS-SC-08** — requires `TimeSeriesSize: 3` tenant config and 3+ poll cycles per phase.
   Slower to validate; schedule last among table stakes.

8. **TS-SC-09** — aggregate metric; depends on `AggregatedMetricDefinition` pipeline already
   working (verified by existing scenario 28's routing counter). Build after SC-01 to SC-08.

---

## Simulator HTTP Control Endpoint Design

The existing simulator has no HTTP server. A minimal control endpoint must be added.

**Recommended interface:**

```
POST /scenario/{scenario_name}
```

Scenarios the endpoint must support:

| Scenario Name | OID Values Changed | Description |
|---------------|--------------------|-------------|
| `healthy` | Evaluate OIDs → in-range values | Default healthy state |
| `evaluate-violated` | Evaluate OIDs → out-of-range values | All Evaluate violated |
| `resolved-violated` | Resolved OIDs → out-of-range values | All Resolved violated |
| `stale-start` | Simulate stopped responses (accept SNMP GETs but delay > GraceWindow) | Begin staleness scenario |
| `stale-end` | Resume normal responses | End staleness scenario |
| `all-violated` | All OIDs → out-of-range (for advance gate sub-scenario B) | Tier 4 fires for all tenants |

The endpoint uses a shared in-memory state dict mapping OID → value. The `DynamicInstance` class
already exists in the simulator and reads values via a callback. The control endpoint simply
updates the dict that the callbacks read from.

**Implementation approach:**
- Add `aiohttp` HTTP server running alongside the existing asyncio SNMP engine
- Single Python module; no production dependencies beyond `pysnmp` and `aiohttp`
- Port: `8080` (HTTP control); `161` (SNMP, unchanged)
- K8s deployment updated to expose port `8080` and add readiness probe on `GET /health`

---

## Validation Point Matrix

| Scenario | Log Pattern (must contain) | Counter Asserted | Counter Direction |
|----------|---------------------------|-----------------|-------------------|
| TS-SC-01 | `tier=3 — not all evaluate` | `snmp_command_sent_total` | delta=0 |
| TS-SC-02 | `tier=4 — commands enqueued` | `snmp_command_sent_total` | delta>=1 |
| TS-SC-03 | `tier=2 — all resolved violated` | `snmp_command_sent_total` | delta=0 |
| TS-SC-04 (B) | `Command suppressed for tenant` | `snmp_command_suppressed_total` | delta>=1 |
| TS-SC-04 (C) | `tier=4 — commands enqueued` | `snmp_command_sent_total` | delta+1 after window |
| TS-SC-05 | `tier=1 stale` | `snmp_command_sent_total` | plateau (stops) |
| TS-SC-06 | both `tier=3` and `tier=4` | `snmp_command_sent_total` | delta=1 only |
| TS-SC-07A | tenant A `tier=3`, no tenant B logs | `snmp_command_sent_total` | delta=0 |
| TS-SC-07B | both `tier=4` | `snmp_command_sent_total` | delta=2 |
| TS-SC-08 (B) | `tier=3 — not all evaluate` | `snmp_command_sent_total` | no increment |
| TS-SC-08 (C) | `tier=4 — commands enqueued` | `snmp_command_sent_total` | delta>=1 |
| TS-SC-09 | `tier=4 — commands enqueued` | `snmp_command_sent_total` | delta>=1 |

---

## Time Series All-Samples Check: Implications for Test Design

The `AreAllEvaluateViolated` implementation checks every sample in `ReadSeries()`, not just
the most recent. This has concrete test design implications:

1. **Wait for series to fill before asserting.** With `TimeSeriesSize: 3` and a 10s poll
   interval, a full series takes 30s to fill. Tests must wait for this before asserting
   "all violated" triggers a command. Use `poll_until` with a 90s timeout.

2. **The sentinel participates.** `MetricSlotHolder` is constructed with a sentinel sample
   (`Value=0`, `Timestamp=UtcNow`). Until the first real poll overwrites it, the series
   contains the sentinel. If `Threshold: { Max: 95 }`, sentinel value `0` is NOT violated
   (0 < 95, which is in range). This means a freshly started tenant with a violated OID
   will NOT fire on the very first snapshot cycle if `TimeSeriesSize > 1` — the sentinel
   keeps the series "not all violated."

   Test design consequence: always wait for at least `TimeSeriesSize` poll cycles before
   asserting that a command fires for a `TimeSeriesSize > 1` tenant.

3. **Recovery is fast (single in-range sample).** One in-range poll clears the "all violated"
   condition regardless of prior series state. TS-SC-08 phase B tests this — the series need
   not be fully re-filled for the tenant to recover to Healthy.

4. **Partial series is also fully checked.** If a tenant just reloaded (series is shorter
   than `TimeSeriesSize`), all present samples are still checked. A 2-sample series `[97, 97]`
   in a `TimeSeriesSize: 5` holder is fully violated and triggers Tier 4. This is correct
   behavior documented in the unit tests (`Execute_EvaluatePartialSeriesFill_AllViolated_ProceedsToTier4`).

---

## MVP Recommendation

**Must build (9 table stakes scenarios):**
1. TS-SC-01: Single tenant healthy
2. TS-SC-02: Single tenant evaluate violated
3. TS-SC-03: Resolved gate (ConfirmedBad)
4. TS-SC-04: Suppression window
5. TS-SC-05: Staleness detection
6. TS-SC-06: Two tenants same priority
7. TS-SC-07: Two tenants different priority (both sub-scenarios A and B)
8. TS-SC-08: Time series all-samples check
9. TS-SC-09: Aggregate metric as evaluate holder

**Should build (differentiators):**
1. D-SC-01: Cycle duration histogram sanity check (trivial Prometheus query, low effort)
2. D-SC-02: Partial suppression (two commands, one suppressed) — extends SC-04 scaffolding
3. D-SC-03: Liveness not broken across all scenarios (run-all validation)

**Explicitly do NOT build:**
- AF-SC-01: Fixed sleep assertions
- AF-SC-02: SET side-effect verification on simulator
- AF-SC-03: Using production tenant config
- AF-SC-04: ConfirmedBad cascade multi-group scenario

---

## Sources

- Codebase: `src/SnmpCollector/Jobs/SnapshotJob.cs` — full 4-tier logic, `HasStaleness`,
  `AreAllResolvedViolated`, `AreAllEvaluateViolated`, `IsViolated`, advance gate, log lines
  (HIGH confidence — read directly)
- Codebase: `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — `ReadSeries()`,
  `TimeSeriesSize`, sentinel construction, `WriteValue` cyclic append (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/SuppressionCache.cs` — key format
  `{tenantId}:{Ip}:{Port}:{CommandName}`, window check behavior (HIGH confidence)
- Codebase: `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — counter names
  `snmp.command.sent/failed/suppressed`, `snmp.snapshot.cycle_duration_ms` (HIGH confidence)
- Codebase: `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` — 40+ unit tests documenting
  exact edge case behavior for all tiers, series checks, suppression, advance gate (HIGH confidence)
- Codebase: `simulators/e2e-sim/e2e_simulator.py` — `DynamicInstance` pattern for mutable OID
  values; existing community strings; no HTTP control endpoint yet (HIGH confidence)
- Codebase: `tests/e2e/scenarios/28-tenantvector-routing.sh` — save/restore ConfigMap pattern,
  `poll_until` usage, log grep pattern for validation (HIGH confidence)
- Codebase: `tests/e2e/lib/common.sh` — `record_pass/fail`, `assert_delta_gt`, `poll_until`
  utilities (HIGH confidence)
- Codebase: `src/SnmpCollector/config/tenants.json` — existing tenant config structure, OID
  names, command names for reference (HIGH confidence)
- Codebase: `deploy/k8s/simulators/e2e-sim-deployment.yaml` — current simulator K8s deployment,
  port structure, probe patterns (HIGH confidence)

---

*Feature research for: E2E Test Scenarios for SnapshotJob 4-Tier Tenant Evaluation*
*Researched: 2026-03-17*
