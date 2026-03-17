# Phase 54: Multi-Tenant Scenarios - Research

**Researched:** 2026-03-17
**Domain:** Bash E2E scenario scripting, SnapshotJob advance gate, multi-tenant assertion patterns
**Confidence:** HIGH

---

## Summary

Phase 54 writes two bash scenario scripts (MTS-01 and MTS-02) validating the multi-tenant
evaluation paths of SnapshotJob. All infrastructure is already in place: the tenant fixtures
(tenant-cfg02 and tenant-cfg03), the "healthy" and "command_trigger" simulator scenarios,
the lib functions (sim.sh, prometheus.sh, common.sh), and the Phase 53 script patterns.
This phase is pure authoring — no new fixtures, no new simulator scenarios, no new lib
functions are needed.

The critical design questions concern how to write multi-tenant assertions given that all
tenants watch the **same OIDs on the same simulator**. The advance gate (MTS-02) is the
more complex test: it requires showing that a group-2 tenant's tier logs are **absent** when
group-1 is Commanded/Stale, and **present** when group-1 is Healthy.

The report.sh category "Snapshot Evaluation|28|32" currently covers indices 28-32 (STS-01
through STS-05). Adding MTS-01 (index 33) and MTS-02 (index 34) falls outside this range.
The category upper bound must be extended to 34.

**Primary recommendation:** Two scripts — 34-mts-01-same-priority.sh and 35-mts-02-advance-gate.sh.
MTS-02 covers both the gate-blocked and gate-passed sub-scenarios in one script (two named
assertion windows). Extend report.sh "Snapshot Evaluation" upper bound from 32 to 34.

---

## Standard Stack

### Core (already in repo — no new installs)

| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| bash | system | test scripting | established project pattern |
| sim.sh | Phase 52 | sim_set_scenario, reset_scenario, poll_until_log | Phase 52/53 complete |
| prometheus.sh | existing | snapshot_counter, query_counter, poll_until, get_evidence | established |
| common.sh | existing | record_pass, record_fail, assert_delta_gt | established |
| kubectl.sh | existing | save_configmap, restore_configmap | established |
| kubectl | cluster | ConfigMap apply, pod logs | established |

### Key Function Signatures (HIGH confidence — read from source)

```bash
# sim.sh
sim_set_scenario <name>              # POST /scenario/{name}; returns 1 on failure
reset_scenario                       # convenience: sim_set_scenario default
poll_until_log <timeout_s> <interval_s> <grep_pattern> [since_seconds]
                                     # returns 0 on first match any pod, 1 on timeout
                                     # default since_seconds=60
                                     # uses grep "${pattern}" (supports alternation with \|)

# prometheus.sh
snapshot_counter <metric_name> [label_filter]   # returns integer value string
query_counter    <metric_name> [label_filter]   # alias of snapshot_counter
poll_until  <timeout_s> <interval_s> <metric> <filter> <baseline>  # polls until > baseline
get_evidence <metric_name> [label_filter]       # returns formatted evidence string

# common.sh
record_pass <scenario_name> <evidence>
record_fail <scenario_name> <evidence>
assert_delta_gt <delta> <threshold> <scenario_name> <evidence>

# kubectl.sh
save_configmap <name> <namespace> <output_file>   # saves without resourceVersion/uid/timestamp
restore_configmap <file>                           # kubectl apply -f
```

### Prometheus Metric Names (HIGH confidence — verified in PipelineMetricService.cs)

| Prometheus Name | Label | What it counts |
|----------------|-------|----------------|
| snmp_command_sent_total | device_name="E2E-SIM" | SNMP SET commands executed by CommandWorkerService |
| snmp_command_suppressed_total | device_name="{tenant.Id}" | SET commands suppressed by SuppressionCache |

**Note on multi-tenant counters:** All tenants in cfg02 and cfg03 command the same device
(`e2e-simulator.simetra.svc.cluster.local:161`), so `snmp_command_sent_total{device_name="E2E-SIM"}`
accumulates across all tenants — there is no per-tenant label on the sent counter.
The suppressed counter uses `device_name="{tenant.Id}"` where `tenant.Id` is the string
from the fixture: `"e2e-tenant-A"`, `"e2e-tenant-B"`, `"e2e-tenant-P1"`, `"e2e-tenant-P2"`.

---

## Fixture Analysis (HIGH confidence — read from source)

### tenant-cfg02-two-same-prio.yaml

Two tenants, both Priority 1:
- `e2e-tenant-A`: Priority 1, SuppressionWindowSeconds 10
- `e2e-tenant-B`: Priority 1, SuppressionWindowSeconds 10
- Both watch the same OIDs: e2e_port_utilization (Evaluate, Max:80), e2e_channel_state (Resolved, Min:1.0), e2e_bypass_status (Resolved, Min:1.0)
- Both command e2e_set_bypass on the same simulator

**Implication for MTS-01:** Since both tenants watch the same OIDs with identical thresholds,
they will always produce the same tier result for any given simulator scenario. Independence
is proven by showing that **both tenant IDs appear in tier log lines** during the same
SnapshotJob cycle — not by showing different outcomes.

### tenant-cfg03-two-diff-prio.yaml

Two tenants, different priorities:
- `e2e-tenant-P1`: Priority 1, SuppressionWindowSeconds 10
- `e2e-tenant-P2`: Priority 2, SuppressionWindowSeconds 10
- Same OIDs and thresholds as cfg02 tenants
- Same commands (e2e_set_bypass on simulator)

**Implication for MTS-02:** The advance gate fires only when a Priority-1 result is
TierResult.Commanded or TierResult.Stale. TierResult.ConfirmedBad and TierResult.Healthy
both allow advance.

---

## SnapshotJob Advance Gate Logic (HIGH confidence — read from SnapshotJob.cs)

```csharp
// Advance gate: block if ANY tenant in group is Stale or Commanded
var shouldAdvance = true;
for (var i = 0; i < results.Length; i++)
{
    if (results[i] == TierResult.Stale || results[i] == TierResult.Commanded)
    {
        shouldAdvance = false;
        break;
    }
}

if (!shouldAdvance)
    break;  // breaks out of the foreach (var group in _registry.Groups) loop
```

**Gate blocking states:** Stale OR Commanded
**Gate passing states:** Healthy OR ConfirmedBad

**Per-scenario simulation outcome with cfg03 tenants:**

| Scenario | P1 Result | Gate | P2 Evaluated? |
|----------|-----------|------|---------------|
| default | ConfirmedBad (Tier 2) | PASS | YES |
| command_trigger (first cmd) | Commanded (Tier 4) | BLOCK | NO |
| stale (after grace expires) | Stale (Tier 1) | BLOCK | NO |
| healthy | Healthy (Tier 3) | PASS | YES |

---

## Exact Log Strings (HIGH confidence — read from SnapshotJob.cs)

All tier log strings use structured logging with tenant ID and priority:

```
Tier 1 stale:      "Tenant {TenantId} priority={Priority} tier=1 stale — skipping threshold checks"
Tier 2 confirmed:  "Tenant {TenantId} priority={Priority} tier=2 — all resolved violated, device confirmed bad, no commands"
Tier 2 pass:       "Tenant {TenantId} priority={Priority} tier=2 — resolved not all violated, proceeding to evaluate check"
Tier 3 healthy:    "Tenant {TenantId} priority={Priority} tier=3 — not all evaluate metrics violated, no action"
Tier 4 commanded:  "Tenant {TenantId} priority={Priority} tier=4 — commands enqueued, count={CommandCount}"
```

**Log levels:**
- Tier 1, 2, 3: LogDebug
- Tier 4: LogInformation (always visible)

**Grep patterns for multi-tenant assertions:**

For checking a specific tenant's tier:
```bash
poll_until_log 90 5 "e2e-tenant-A.*tier=3\|tier=3.*e2e-tenant-A" 60
```

For checking that a tenant ID does NOT appear:
```bash
# Not a poll_until_log — must use direct kubectl logs + negative grep
kubectl logs <pod> -n simetra --since=30s | grep "e2e-tenant-P2"
# If this returns nothing: P2 was not evaluated
```

---

## Architecture Patterns

### Recommended File Structure

```
tests/e2e/scenarios/
├── 34-mts-01-same-priority.sh      # MTS-01: both P1 tenants log tier results
└── 35-mts-02-advance-gate.sh       # MTS-02: gate blocked then gate passed

tests/e2e/lib/
└── report.sh                       # EXTEND — "Snapshot Evaluation|28|34"
```

### Scenario Numbering

- Existing scenarios: 01-33 (indices 0-32)
- MTS-01: file 34, index 33
- MTS-02: file 35, index 34
- report.sh must change "Snapshot Evaluation|28|32" to "Snapshot Evaluation|28|34"

### Standard Script Structure (inherited from Phase 53)

```bash
# 1. FIXTURES_DIR path
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# 2. Save original ConfigMap, apply fixture
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
kubectl apply -f "$FIXTURES_DIR/tenant-cfg0X-XXXX.yaml" > /dev/null 2>&1 || true

# 3. Wait for reload (poll_until_log — never fixed sleep for reload)
poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30 || \
    log_warn "reload not detected within 60s; proceeding"

# 4. Set simulator scenario
sim_set_scenario <scenario_name>

# 5. Baseline counters
BEFORE=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')

# 6. Poll for log evidence + assert counters
# 7. Cleanup: reset_scenario + restore_configmap
reset_scenario
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
fi
```

---

## Scenario-by-Scenario Technical Analysis

### MTS-01: Same-Priority Independence

**Fixture:** tenant-cfg02-two-same-prio.yaml (e2e-tenant-A and e2e-tenant-B, both Priority 1)

**Simulator scenario:** command_trigger (.4.1=90, .4.2=2, .4.3=2) — both tenants will reach Tier 4

**Why command_trigger:** Using a tier=4 scenario ensures LogInformation is emitted
(not LogDebug), which is always visible regardless of pod log level. Both tenants reaching
Tier 4 with distinct tenant IDs in their log lines is strong, log-level-safe proof of
independent evaluation.

**Expected log lines (both must appear):**
```
"e2e-tenant-A priority=1 tier=4 — commands enqueued, count=1"
"e2e-tenant-B priority=1 tier=4 — commands enqueued, count=1"
```

**Poll strategy:** `poll_until_log` patterns for each tenant separately:
```bash
poll_until_log 90 5 "e2e-tenant-A.*tier=4 — commands enqueued" 60
poll_until_log 90 5 "e2e-tenant-B.*tier=4 — commands enqueued" 60
```

**Counter assertion:** `snmp_command_sent_total{device_name="E2E-SIM"}` delta >= 2
(one command per tenant — both enqueued the same e2e_set_bypass command)

**Suppression note:** With SuppressionWindowSeconds=10 and both tenants keying their
suppression cache on the same device/port/command triple (only prefixed with tenant ID),
both will send commands on the first cycle. On the second cycle, both will suppress.
For MTS-01, snapshot counters BEFORE the first cycle, wait for both tier=4 logs, then
assert delta >= 2.

**Sub-scenarios (two named assertions per script):**
- 34a: `"MTS-01: e2e-tenant-A tier=4 log detected"` — poll_until_log pass/fail
- 34b: `"MTS-01: e2e-tenant-B tier=4 log detected"` — poll_until_log pass/fail
- 34c: `"MTS-01: Both tenants commanded — sent counter delta >= 2"` — counter delta

### MTS-02: Different-Priority Advance Gate

**Fixture:** tenant-cfg03-two-diff-prio.yaml (e2e-tenant-P1 Priority 1, e2e-tenant-P2 Priority 2)

**Two sequential assertion windows in one script:**

#### MTS-02A: Gate blocked (P1 Commanded, P2 not evaluated)

**Simulator scenario:** command_trigger

**Setup:** Apply cfg03, wait for reload, set command_trigger, wait for P1 tier=4 log.

**Assertion window:**
1. Log assertion (positive): `"e2e-tenant-P1.*tier=4 — commands enqueued"` via poll_until_log
2. Log assertion (negative): P2 tenant ID must NOT appear in tier logs during the same window
3. Counter assertion: snapshot `snmp_command_sent_total{device_name="E2E-SIM"}` delta >= 1 (P1 sent)

**Negative assertion method** (cannot use poll_until_log for absence):
```bash
# After confirming P1's tier=4 log, check raw logs for P2
P2_IN_LOGS=$(kubectl logs $POD -n simetra --since=30s 2>/dev/null \
    | grep "e2e-tenant-P2" || echo "")
if [ -z "$P2_IN_LOGS" ]; then
    record_pass "MTS-02A: P2 not evaluated when gate blocked" "P2 not found in logs"
else
    record_fail "MTS-02A: P2 not evaluated when gate blocked" "P2 found unexpectedly: $P2_IN_LOGS"
fi
```

**Important:** The negative check must target the interval AFTER the scenario switch and
BEFORE any reset. Use `--since=30s` to scope the log window to the assertion period.

#### MTS-02B: Gate passed (P1 ConfirmedBad, P2 evaluated and commanded)

**Simulator scenario:** command_trigger (same — P1 reaches Tier 4 and is Commanded, which
blocks the gate. We need P1 to be Healthy or ConfirmedBad instead.)

**Correct scenario for gate-passing:** Use `default` scenario (.4.1=0, .4.2=0, .4.3=0).
With the default scenario:
- P1: all resolved violated (e2e_channel_state=0 < 1.0, e2e_bypass_status=0 < 1.0) → Tier 2 ConfirmedBad → gate PASSES
- P2: also evaluated → also Tier 2 ConfirmedBad (same OIDs, same scenario)

**PROBLEM with default for MTS-02B:** If P2 reaches Tier 2 ConfirmedBad (LogDebug), this
is only visible if Debug logs are enabled. Both tenants will produce Tier 2 ConfirmedBad,
which is LogDebug.

**Correct scenario for MTS-02B (gate passed, P2 commanded):** Use `command_trigger`
scenario BUT allow the suppression window to expire first so P1 does not block again...
but this requires waiting out the suppression window (10s).

**Alternative approach:** Use `healthy` scenario for MTS-02B:
- P1: Healthy (Tier 3) → gate PASSES
- P2: also Healthy (Tier 3) — but Tier 3 is LogDebug

**Best approach for MTS-02B:** Use `command_trigger` in a fresh session where suppression
window has expired. After MTS-02A cleanup (reset_scenario), wait ~15s for suppression to
expire (P1's window is 10s), then re-set command_trigger. On the next cycle:
- P1: Tier 4 Commanded → gate BLOCKS AGAIN

This still blocks P2. The gate-passed assertion requires P1 to NOT be Commanded or Stale.

**Revised MTS-02B approach:** Switch to `default` scenario. Both P1 and P2 reach Tier 2
ConfirmedBad. The evidence for gate-passed is:
- P1 logs tier=2 (LogDebug — may not be visible)
- P2 logs tier=2 (LogDebug — may not be visible)

Since the CONTEXT.md decision says "both groups in command_trigger scenario; assert sent
counters increment for both groups", the intended MTS-02B uses command_trigger. This means:
- P1 reaches Tier 4 Commanded (first command in fresh window)
- Gate blocks on P1 Commanded
- P2 is STILL not evaluated in MTS-02B

**Resolution:** Re-read CONTEXT.md carefully. The MTS-02B decision states:
> "both groups in command_trigger scenario; assert sent counters increment for both groups"

This means P1 is in command_trigger AND P2 is also set up to trigger. For P2 to reach
Tier 4 and have its commands sent, P1 must NOT be in a Commanded state — i.e., P1's
first command must have fired (P1 becomes Commanded, blocking gate), and then the
**suppression window must expire** so P1's second command is suppressed, leaving P1 in
TierResult.ConfirmedBad... but that's not right either.

**Correct reading of CONTEXT.md MTS-02B:** The intent is to show the gate PASSES when P1
is in a non-blocking state. In command_trigger scenario, after P1's first command is sent,
P1 returns `TierResult.Commanded`. On the NEXT cycle (after SuppressionWindowSeconds=10
expires, i.e., next cycle at T=15s), P1 reaches Tier 4 again but now the suppression
window has expired — P1 sends again, P1 is Commanded again. P2 is still never reached.

**The only way P2 gets evaluated** is if P1 returns Healthy or ConfirmedBad.
Use `default` scenario for MTS-02B where P1 (and P2) both reach ConfirmedBad:
- Evidence: P2 tier=2 log appears (LogDebug — if debug logs are on)
- Counter evidence: `snmp_command_sent_total{device_name="E2E-SIM"}` delta == 0 for BOTH
  (both are ConfirmedBad, no commands)

**ALTERNATIVE resolution aligned with CONTEXT.md intent:** MTS-02B should demonstrate
P2 being evaluated and commanded. For this, P1 must be non-blocking. Use `stale` scenario
for P1 to trigger Tier 1 Stale → gate BLOCKS (stale is blocking, not passing). This also
blocks P2.

**THE CORRECT scenario for P2 to be commanded:**

Looking at SnapshotJob.cs again: `TierResult.Healthy` or `TierResult.ConfirmedBad` → advance.
With `command_trigger` scenario, P1 is Commanded → blocks. With `default`, P1 is ConfirmedBad → passes BUT P2 is also ConfirmedBad (no command). With `healthy`, P1 is Healthy → passes, P2 is also Healthy (no command).

**The ONLY way P2 gets commanded** in the current configuration is if P1 is Healthy or
ConfirmedBad AND the evaluate threshold for P2 is violated. Since all tenants watch the
same OIDs, both P1 and P2 will have the same tier result for any scenario.

**Conclusion about MTS-02B:** CONTEXT.md says "both groups in command_trigger scenario;
assert sent counters increment for both groups". The only reading that works is:
- Use `command_trigger` scenario
- P1 first cycle: Tier 4 Commanded → gate blocks → P2 not evaluated (MTS-02A)
- Wait for P1's suppression window to expire (10s)
- P1 second cycle: Tier 4, suppression triggered → command suppressed → `enqueueCount = 0`
  → `return enqueueCount > 0 ? TierResult.Commanded : TierResult.ConfirmedBad`
  → P1 returns **ConfirmedBad** (because no commands actually enqueued)
- Gate passes on ConfirmedBad
- P2 is evaluated: also reaches Tier 4 → P2's suppression key is distinct → P2 sends command

**This is the key insight from SnapshotJob.cs line 196:**
```csharp
return enqueueCount > 0 ? TierResult.Commanded : TierResult.ConfirmedBad;
```
When all commands are suppressed (enqueueCount == 0), the result is **ConfirmedBad**, not
Commanded. So after one suppression cycle, P1 returns ConfirmedBad, the gate passes, and
P2 gets evaluated and commands.

**MTS-02B timing:** With SuppressionWindowSeconds=10 and SnapshotJob interval=15s:
- T=0: command_trigger set, P1 first cycle → Commanded (sent=1), gate blocks
- T=15s: next cycle, 15s > 10s so suppression expired → P1 Commanded AGAIN (sent again)
  → gate still blocks

**TIMING PROBLEM AGAIN:** SuppressionWindowSeconds=10 < SnapshotJob interval=15s.
P1's suppression window expires BEFORE the next cycle. Every cycle sends a fresh command.
P1 is always Commanded, never ConfirmedBad-via-suppression. P2 is never evaluated.

**Resolution for MTS-02B:** The cfg03 fixture needs `SuppressionWindowSeconds` for
e2e-tenant-P1 to be **greater than** the SnapshotJob interval (15s). If P1's window is
30s: T=0 send (Commanded), T=15s suppress (ConfirmedBad-via-suppression), gate passes,
P2 evaluated and commanded.

**Recommendation:** Create a new fixture `tenant-cfg03-two-diff-prio-mts.yaml` as a
modified copy of cfg03 with `e2e-tenant-P1: SuppressionWindowSeconds: 30`. This is the
only clean solution. Alternatively, re-use the existing suppression insight from Phase 53
(tenant-cfg01-suppression.yaml used 30s for the same reason).

**OR:** Use `healthy` scenario for MTS-02B (gate passes) + separate `command_trigger`
assertion only for P2. But this requires P2 to independently reach Tier 4 while P1 is
Healthy. Since all OIDs are shared, if the scenario makes P1 Healthy, P2 is also Healthy
(same OIDs → same result). So P2 would not be commanded in this case either.

**Final determination for MTS-02B:** The only viable path is:
1. Create `tenant-cfg03-two-diff-prio-mts.yaml` with P1 having SuppressionWindowSeconds=30
2. MTS-02B: P1 first cycle at T=0 → Commanded (gate blocks)
3. Wait for second cycle at T=15s where P1's 30s window is still active → P1 suppressed → ConfirmedBad → gate passes → P2 commanded
4. Assert P2's tier=4 log appears AND sent counter delta >= 2 total (P1 at T=0 + P2 at T=15)

**OR simpler:** Per CONTEXT.md decision, both P1 and P2 are in command_trigger.
MTS-02B just asserts that sent counters increment for BOTH groups (meaning P2's command
is also dispatched). This requires the timing above with a 30s suppression window fixture.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Waiting for reload | `sleep 30` | `poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30` | Phase 53 established this pattern |
| Log absence assertion | custom kubectl exec + grep pipe | direct `kubectl logs ... \| grep "pattern"` + check empty string | Simple and correct |
| Per-tenant counter | new metric label | Use `snmp_command_suppressed_total` with `device_name="{tenant.Id}"` for suppression; `snmp_command_sent_total` aggregates all tenants under `device_name="E2E-SIM"` | This is the actual label semantics |
| Multi-tenant log assertion | single grep with both IDs | two separate `poll_until_log` calls, one per tenant | poll_until_log checks one pattern; use separate calls |

---

## Common Pitfalls

### Pitfall 1: SuppressionWindowSeconds=10 < SnapshotJob interval=15s prevents P2 from ever being commanded

**What goes wrong:** In tenant-cfg03, both tenants have SuppressionWindowSeconds=10.
With a 15s SnapshotJob interval, P1's suppression window expires before the next cycle.
Every cycle, P1 sends a fresh command (Commanded), the gate blocks, P2 is never evaluated.
MTS-02B can never demonstrate P2 being commanded with the stock cfg03 fixture.

**How to resolve:** Create `tenant-cfg03-two-diff-prio-mts.yaml` with P1's
SuppressionWindowSeconds changed to 30. Then the cycle sequence is:
- T=0: P1 commanded (gate blocks, P2 skipped)
- T=15s: P1 suppressed (ConfirmedBad, gate passes, P2 commanded)

**Warning signs:** If P2's tier=4 log never appears in MTS-02B, the suppression window
timing is the cause.

### Pitfall 2: Tier 2 and Tier 3 are LogDebug — not visible if pod log level is above Debug

**What goes wrong:** MTS-02B evidence via P2's tier=2 or tier=3 log is LogDebug. If the
pod does not emit Debug logs, these patterns will never match in poll_until_log. Only Tier
4 (LogInformation) is guaranteed to be visible.

**How to resolve:** Structure MTS-02 so that both MTS-02A and MTS-02B use Tier 4 assertions
(command_trigger scenario). This is achievable with the timing fix above (suppression fixture).

**Mitigation:** If Debug logs are needed, assert absence of P2 tier logs using counter
delta == 0 for the suppressed counter as proxy evidence.

### Pitfall 3: Negative log assertion timing — what window to check

**What goes wrong:** Checking "P2 not in logs" with `--since=300s` may find P2 log lines
from earlier scenario runs or from the initial boot sequence.

**How to resolve:** Take a timestamp BEFORE switching to the gate-blocking scenario, then
use `--since=<small_window>s` to scope the check to only the relevant observation window.
Use `--since=30s` after the P1 tier=4 log is confirmed.

**Alternative:** Record the time before switching scenarios with `T_START=$(date +%s)` and
compute `--since=$(($(date +%s) - T_START + 5))s` dynamically.

### Pitfall 4: poll_until_log finds the first pod match — in a multi-replica cluster, P2 may log in a different pod

**What goes wrong:** poll_until_log iterates over all pods but checks for a single pattern.
For MTS-01, you need BOTH tenant-A AND tenant-B to appear. A single log check confirms one
tenant's tier log; a second, separate call confirms the other's.

**How to resolve:** Two separate poll_until_log calls (one per tenant). Both must return 0
for the sub-scenario to pass. The second call uses a shorter timeout since the scenario is
already active and evaluation should be ongoing.

### Pitfall 5: ConfigMap reload not instantaneous — apply then immediately checking logs

**What goes wrong:** `kubectl apply` returns immediately. The watcher fires asynchronously.
If assertions run before the reload completes, the test evaluates the old tenant set.

**How to resolve:** Always `poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30`
immediately after kubectl apply, before any scenario switch. This is the Phase 53 standard.

### Pitfall 6: Both tenants in cfg02 share the same suppression key pattern

**What goes wrong:** e2e-tenant-A and e2e-tenant-B both command the same
`e2e-simulator.simetra.svc.cluster.local:161:e2e_set_bypass`. Their suppression keys are:
- `e2e-tenant-A:e2e-simulator.simetra.svc.cluster.local:161:e2e_set_bypass`
- `e2e-tenant-B:e2e-simulator.simetra.svc.cluster.local:161:e2e_set_bypass`

These are DISTINCT keys (prefixed with tenant ID). Both will send on the first cycle. No
cross-tenant suppression interference.

### Pitfall 7: Advance gate log is not emitted

**What goes wrong:** SnapshotJob does not log "gate blocked" or "gate passed" explicitly.
The only evidence of gate behavior is the presence or absence of group-2 tenant tier logs.

**How to assert gate behavior:**
- Gate blocked: P1 tier=4 log present + P2 tier logs absent
- Gate passed: P1 tier logs present + P2 tier logs present (both reach their tier)

---

## Code Examples

### MTS-01 pattern: polling for both tenant IDs

```bash
# Source: Pattern from sim.sh poll_until_log and Phase 53 scripts
sim_set_scenario command_trigger

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')

# Poll for tenant-A tier=4 log
if poll_until_log 90 5 "e2e-tenant-A.*tier=4 — commands enqueued" 60; then
    record_pass "MTS-01: e2e-tenant-A tier=4 log detected" "log=tier4_tenantA_found"
else
    record_fail "MTS-01: e2e-tenant-A tier=4 log detected" "tier4 log for e2e-tenant-A not found within 90s"
fi

# Poll for tenant-B tier=4 log (shorter timeout — scenario already active)
if poll_until_log 60 5 "e2e-tenant-B.*tier=4 — commands enqueued" 30; then
    record_pass "MTS-01: e2e-tenant-B tier=4 log detected" "log=tier4_tenantB_found"
else
    record_fail "MTS-01: e2e-tenant-B tier=4 log detected" "tier4 log for e2e-tenant-B not found within 60s"
fi

# Both tenants commanded → at least 2 commands sent
AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
DELTA=$((AFTER_SENT - BEFORE_SENT))
assert_delta_gt "$DELTA" 1 "MTS-01: Both tenants commanded — sent delta >= 2" \
    "sent_delta=${DELTA} $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
```

### MTS-02A pattern: gate-blocked negative assertion

```bash
# Source: kubectl.sh and sim.sh patterns, Phase 53 absence assertion
sim_set_scenario command_trigger
BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')

# Confirm P1 reached Tier 4 (gate-blocking state)
if poll_until_log 90 5 "e2e-tenant-P1.*tier=4 — commands enqueued" 60; then
    record_pass "MTS-02A: P1 tier=4 log detected (gate blocker)" "log=tier4_P1_found"
else
    record_fail "MTS-02A: P1 tier=4 log detected (gate blocker)" "tier4 log for P1 not found"
fi

# Negative assertion: P2 must not appear in tier logs during this window
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
P2_FOUND=0
for POD in $PODS; do
    P2_LOGS=$(kubectl logs "$POD" -n simetra --since=30s 2>/dev/null \
        | grep "e2e-tenant-P2.*tier=" || echo "") || true
    if [ -n "$P2_LOGS" ]; then
        P2_FOUND=1
        break
    fi
done

if [ "$P2_FOUND" -eq 0 ]; then
    record_pass "MTS-02A: P2 not evaluated when gate blocked" \
        "e2e-tenant-P2 tier log absent in 30s window"
else
    record_fail "MTS-02A: P2 not evaluated when gate blocked" \
        "e2e-tenant-P2 tier log found unexpectedly"
fi

# Counter: P2 sent delta == 0
AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
DELTA=$((AFTER_SENT - BEFORE_SENT))
# Note: DELTA > 0 here is expected (P1 sent) — the point is P2 was not evaluated
# For MTS-02A counter evidence, snapshot suppressed for P2 directly:
P2_SUPP_AFTER=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-P2"')
# (baseline taken before scenario switch should be 0 if P2 never evaluated)
```

### MTS-02B pattern: gate-passed, P2 commanded

```bash
# Wait for P1 suppression window to expire and gate to pass
# (requires tenant-cfg03-two-diff-prio-mts.yaml with P1 SuppressionWindowSeconds=30)
# P1 suppression fires at T=15s cycle, returns ConfirmedBad, gate passes, P2 commanded at same cycle

BEFORE_SENT_2=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
BEFORE_P2_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-P2"')

# Wait for P2's tier=4 log (P2 will be commanded after gate passes)
if poll_until_log 60 5 "e2e-tenant-P2.*tier=4 — commands enqueued" 30; then
    record_pass "MTS-02B: P2 tier=4 log detected (gate passed)" "log=tier4_P2_found"
else
    record_fail "MTS-02B: P2 tier=4 log detected (gate passed)" "tier4 log for P2 not found within 60s"
fi

AFTER_SENT_2=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
DELTA_2=$((AFTER_SENT_2 - BEFORE_SENT_2))
assert_delta_gt "$DELTA_2" 0 "MTS-02B: P2 sent counter incremented" \
    "sent_delta=${DELTA_2} $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
```

### Report category extension

```bash
# In tests/e2e/lib/report.sh, change:
# "Snapshot Evaluation|28|32"
# to:
# "Snapshot Evaluation|28|34"
```

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|-----------------|--------|
| Single-tenant scenarios only | Multi-tenant with per-tenant log pattern checks | Requires two separate poll_until_log calls per tenant pair |
| Positive assertions only | Positive + negative (absence) assertions | Absence check via direct kubectl logs + empty string test |
| Fixed sleep for reload | poll_until_log for reload | Phase 53 mandated, Phase 54 inherits |
| No suppression timing awareness | Suppression window must exceed SnapshotJob interval for gate-pass test | cfg03-mts fixture with 30s window required |

**Deprecated / not applicable:**
- `poll_until` (prometheus.sh) for Tier 3/Tier 4 log waiting — use `poll_until_log` instead

---

## Open Questions

### 1. Whether to create tenant-cfg03-two-diff-prio-mts.yaml or reuse cfg03 and adapt the assertion

**What we know:** With P1 SuppressionWindowSeconds=10 and SnapshotJob interval=15s, P1's
suppression window expires before the next cycle fires. P1 sends every cycle → always
Commanded → gate always blocks → P2 never evaluated.

**Resolution options:**
1. Create new fixture `tenant-cfg03-two-diff-prio-mts.yaml` with P1 SuppressionWindowSeconds=30 (recommended — cleanest, same pattern as tenant-cfg01-suppression.yaml)
2. Modify tenant-cfg03 in place (risky — could break other tests that depend on default cfg03 state)
3. Accept that MTS-02B proves only "P2 sent counter does NOT increment" and use negative counter assertion as MTS-02B (gate blocked → no P2 commands)

**Recommendation:** Option 1 (new fixture). The planner should include a task to create
`tests/e2e/fixtures/tenant-cfg03-two-diff-prio-mts.yaml` as a copy of cfg03 with P1's
SuppressionWindowSeconds changed to 30.

### 2. Whether MTS-02A and MTS-02B should be one script or two

**What we know:** CONTEXT.md says this is Claude's Discretion. One script is simpler (no
state sharing needed between scripts since they're `source`d sequentially). The two
windows (blocked then passed) are causally linked in one script via the suppression timing.

**Recommendation:** One script (35-mts-02-advance-gate.sh) with two named assertion
windows: "02A" and "02B" in sub-scenario names. This matches the STS-04 suppression
window precedent (one script, multiple assertion windows).

### 3. MTS-01 counter assertion threshold: delta >= 2 or delta > 0

**What we know:** Both tenants command the same device (e2e-simulator). Each sends exactly
one command on the first unsuppressed cycle. So delta should be exactly 2 if both tenants
fire on the same cycle, or could be 1 if only one cycle has run.

**Recommendation:** Use `assert_delta_gt "$DELTA" 1` (delta > 1, i.e., >= 2) to require
both tenants to have sent. This is the strongest assertion of independence.

### 4. Log level for Tier 2/3 logs — whether Debug logs are emitted by pods

**What we know:** Tier 4 is LogInformation (always visible). Tier 1/2/3 are LogDebug.
If pods emit Debug logs, tier=2 and tier=3 are available for assertions. If not, only
Tier 4 log lines are reliable.

**From Phase 53 research:** This was an open question then. The Phase 53 scenarios (29-33)
were written using tier=3 assertions (29-sts-01-healthy.sh uses `poll_until_log ... "tier=3 — not all evaluate metrics violated"`). Since Phase 53 was completed and the scripts exist as-is, Debug logs ARE being emitted by the pods.

**Confidence:** MEDIUM — Phase 53 scenarios were written assuming Debug logs are visible,
and Phase 53 is complete. If they passed, Debug logs are on.

---

## Sources

### Primary (HIGH confidence)

- Read directly from source: `src/SnmpCollector/Jobs/SnapshotJob.cs` — all tier log strings, TierResult enum, advance gate logic (lines 67-93), suppression-path ConfirmedBad return (line 196)
- Read directly from source: `tests/e2e/fixtures/tenant-cfg02-two-same-prio.yaml` — two Priority-1 tenants, SuppressionWindowSeconds=10
- Read directly from source: `tests/e2e/fixtures/tenant-cfg03-two-diff-prio.yaml` — Priority 1 + Priority 2 tenants, SuppressionWindowSeconds=10 for both
- Read directly from source: `tests/e2e/lib/sim.sh` — poll_until_log exact implementation, all function signatures
- Read directly from source: `tests/e2e/lib/prometheus.sh` — snapshot_counter, query_counter, get_evidence signatures
- Read directly from source: `tests/e2e/lib/common.sh` — record_pass, record_fail, assert_delta_gt
- Read directly from source: `tests/e2e/lib/kubectl.sh` — save_configmap, restore_configmap
- Read directly from source: `tests/e2e/lib/report.sh` — category array format, current "Snapshot Evaluation|28|32"
- Read directly from source: `simulators/e2e-sim/e2e_simulator.py` — "healthy" and "command_trigger" scenarios confirmed present, OID values
- Read directly from source: `tests/e2e/scenarios/29-33` (all Phase 53 scripts) — exact scripting patterns

### Secondary (MEDIUM confidence)

- Phase 53 RESEARCH.md — pitfall analysis for SuppressionWindowSeconds=10 < SnapshotJob interval=15s (same issue applies to MTS-02B)
- Phase 53 CONTEXT.md — assertion strategy decisions that MTS follows

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all functions read from source
- Fixture analysis: HIGH — both fixtures read from source
- Advance gate logic: HIGH — read directly from SnapshotJob.cs
- Log strings: HIGH — exact strings extracted from SnapshotJob.cs structured log calls
- Suppression timing analysis: HIGH — confirmed 10s < 15s again for cfg03 context
- MTS-02B viability with stock fixture: HIGH — confirmed impossible without suppression fix
- Debug log visibility in pods: MEDIUM — inferred from Phase 53 scripts assuming it works

**Research date:** 2026-03-17
**Valid until:** 2026-04-17 (stable codebase, no external dependencies)
