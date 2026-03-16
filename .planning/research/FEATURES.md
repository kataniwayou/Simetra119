# Feature Landscape: SnapshotJob Tenant Evaluation & Command Execution

**Domain:** SNMP monitoring agent — closed-loop tenant evaluation and SNMP SET control
**Researched:** 2026-03-16
**Confidence:** HIGH — derived from full codebase analysis. All infrastructure this milestone
builds on (MetricSlotHolder, Tenant, PriorityGroup, ITenantVectorRegistry, ICommandMapService,
ISnmpClient, PipelineMetricService, LivenessVectorService, SnmpOidReceived, MediatR pipeline)
is already implemented and read directly from source. SET capability verified via SharpSnmpLib
official API docs (Messenger.SetAsync with CancellationToken overload exists at 12.5.7).

---

## Scope

A `SnapshotJob` (Quartz IJob, configurable interval, default 15s) evaluates all tenants by
priority order and issues SNMP SET commands when evaluation criteria are met.

The evaluation is a 4-tier decision tree per tenant:

```
Tier 1: Staleness check
    → any Evaluate or Resolved metric slot has no value, or
      last timestamp is older than IntervalSeconds × GraceMultiplier (Poll/Synthetic source only)
    → result: STALE → skip tenant (no command, no resolved check)

Tier 2: Resolved threshold check
    → all Resolved-role metric slots with a threshold have their current value OUTSIDE threshold bounds
    → result: all violated → skip tenant (system in resolved state, no command needed)
    → result: any not violated → continue to Tier 3

Tier 3: Evaluate threshold check
    → all Evaluate-role metric slots with a threshold have their current value OUTSIDE threshold bounds
    → result: all violated → queue command for this tenant (continue to Tier 4)
    → result: any not violated → skip tenant

Tier 4: Command queueing with suppression
    → for each CommandSlotOptions on the tenant:
        → check suppression cache keyed by (Ip, Port, CommandName)
        → if suppressed: increment snmp.command.suppressed, skip
        → if not suppressed: enqueue command, set suppression window, increment snmp.command.sent
```

Priority group traversal:
- Tenants within a group are evaluated in parallel
- Groups are processed in ascending priority order (lower integer = higher priority)
- A group is fully evaluated before the next group is processed
- Advance to next group ONLY IF ALL tenants in the current group reached Tier 4 (all violated)
- If any tenant in the group did not reach Tier 4, stop — do not evaluate lower-priority groups

---

## Context: What Already Exists

Understanding the existing infrastructure is required before specifying new features.

```
ITenantVectorRegistry.Groups         // IReadOnlyList<PriorityGroup>, ascending priority
PriorityGroup.Tenants                // IReadOnlyList<Tenant>
Tenant.Id / Priority / Holders       // IReadOnlyList<MetricSlotHolder>
MetricSlotHolder
    .Ip / Port / MetricName
    .IntervalSeconds / GraceMultiplier
    .Threshold                       // ThresholdOptions { Min: double?, Max: double? }
    .Source                          // SnmpSource enum (Poll, Trap, Synthetic)
    .TypeCode                        // SnmpType
    .ReadSlot()                      // MetricSlot? { Value, StringValue, Timestamp }
    .ReadSeries()                    // ImmutableArray<MetricSlot>

MetricSlotOptions.Role               // "Evaluate" | "Resolved"
CommandSlotOptions                   // Ip, Port, CommandName, Value, ValueType
ICommandMapService.ResolveCommandOid // commandName → OID string
ISnmpClient.GetAsync                 // already in use for polling
Messenger.SetAsync                   // mirrors GetAsync signature, verified in SharpSnmpLib 12.5.7
ILivenessVectorService.Stamp         // all jobs call this on completion
PipelineMetricService                // 12 counters; snmp.command.* are new
SnmpOidReceived / ISender.Send       // full MediatR pipeline dispatch (used for SET response)
SnmpSource.Poll / Trap / Synthetic   // "Command" is a new fourth value needed
```

Key gap: `MetricSlotHolder` stores `Role` as `MetricSlotOptions.Role` at config load time but
does NOT expose it on the holder itself. The holder only carries `Ip`, `Port`, `MetricName`,
`IntervalSeconds`, `TimeSeriesSize`, `GraceMultiplier`, `Threshold`, `TypeCode`, `Source`.
SnapshotJob evaluation requires knowing which holders are `Evaluate` vs `Resolved`. This means
either (a) `Role` must be added to `MetricSlotHolder`, or (b) `Tenant` must carry two separate
lists. This is a design decision for the implementation plan, flagged here as a structural gap.

---

## Table Stakes

Features that MUST exist for the milestone to be correct and safe.

---

### TS-01: `SnmpSource.Command` — New Enum Value

**What:** Add `Command` to the `SnmpSource` enum.

**Why:** SET responses dispatched through MediatR need a distinct source so:
- `OidResolutionBehavior` knows to bypass OID lookup (same as `Synthetic`)
- `TenantVectorFanOutBehavior` can route or skip as appropriate
- Prometheus `source` label on emitted metrics reads `"command"` not `"poll"` or `"synthetic"`
- Log enrichment can distinguish command-response events from poll events

**Existing pattern:** `Synthetic` was added in v1.8 for the same reason (aggregate metrics bypass
OID resolution). `Command` follows the identical extension point.

**Complexity:** Trivial — one enum value plus behavior guard clauses.

---

### TS-02: `MetricSlotHolder.Role` — Expose Role on Holder

**What:** Add `Role` (string or enum) to `MetricSlotHolder` so SnapshotJob can distinguish
Evaluate slots from Resolved slots without external lookups.

**Why:** The 4-tier evaluation logic (Tier 2 checks Resolved slots, Tier 3 checks Evaluate slots)
requires SnapshotJob to partition a tenant's holders by role. The current holder does not carry
this field. Without it, SnapshotJob would need to join against the original config options at
evaluation time, breaking the clean "read from registry" pattern.

**Pattern:** Same as `Threshold` and `GraceMultiplier` — config-derived, stored at load time in
`TenantVectorRegistry.Reload`, not recomputed at evaluation time.

**Valid values:** `"Evaluate"` | `"Resolved"` (matching existing `MetricSlotOptions.Role`
validated values from v1.7).

**Complexity:** Low — one new property, populated in `TenantVectorRegistry.Reload()` alongside
the existing `MetricSlotHolder` constructor.

---

### TS-03: Staleness Detection (Tier 1)

**What:** A holder is stale if:
1. `holder.ReadSlot()` returns null (no value ever written), OR
2. `holder.Source` is `Poll` or `Synthetic` AND
   `(UtcNow - slot.Timestamp).TotalSeconds > holder.IntervalSeconds × holder.GraceMultiplier`

**Why Trap source is excluded from staleness:** Traps are device-initiated. Their arrival rate is
unpredictable — a device that only sends a trap on state change may legitimately have a slot with a
day-old timestamp that is perfectly healthy. Forcing a staleness window on trap-sourced metrics would
produce false positives. Poll and Synthetic sources have known intervals and are the appropriate
candidates for staleness detection.

**Tenant-level staleness rule:** A tenant is stale if ANY of its metric slots (Evaluate or Resolved)
are stale. A single stale slot stops evaluation for that tenant — no threshold check proceeds on
potentially outdated data.

**Zero IntervalSeconds guard:** A holder with `IntervalSeconds = 0` cannot compute a meaningful
staleness threshold (`0 × 2.0 = 0s`). Such holders must be treated as never-stale for evaluation
purposes — SnapshotJob skips them in staleness computation. This matches the existing `MetricSlotOptions`
behavior where `IntervalSeconds` defaults to 0 when not set.

**Complexity:** Medium — straightforward timestamp math, but needs careful handling of the
null-slot, zero-interval, and Trap-source edge cases.

---

### TS-04: Threshold Violation Check (Tiers 2 and 3)

**What:** A holder's current value violates its threshold when:
- `holder.Threshold` is non-null, AND
- `holder.Threshold.Min` is set AND `slot.Value < holder.Threshold.Min`, OR
- `holder.Threshold.Max` is set AND `slot.Value > holder.Threshold.Max`

"All violated" tier logic:
- Tier 2 (Resolved): collect all holders with `Role == "Resolved"` AND `Threshold != null`. All of
  them must be in violation. If there are NO Resolved-role holders with thresholds, Tier 2 passes
  vacuously (no check performed, continue to Tier 3).
- Tier 3 (Evaluate): collect all holders with `Role == "Evaluate"` AND `Threshold != null`. All of
  them must be in violation. If there are NO Evaluate-role holders with thresholds, Tier 3 fails
  vacuously (no command will ever be issued — this is a configuration error, logged at Warning on
  job startup, not at every evaluation tick).

**Holders without thresholds are excluded from tier checks.** A holder with `Role == "Evaluate"`
and `Threshold == null` does not participate in the "all violated" count. This allows tenants to
include informational metrics that are not part of the threshold evaluation.

**Complexity:** Medium — the vacuous-pass/vacuous-fail distinction and the "all violated" (not
"any violated") semantics are easy to get wrong. Implementation plans must have explicit test cases
for both empty-threshold and mixed-threshold scenarios.

---

### TS-05: Priority Group Traversal — Sequential With "All Violated" Advance Gate

**What:** SnapshotJob processes `ITenantVectorRegistry.Groups` in ascending priority order.

Within a group:
- Evaluate all tenants in parallel using `Task.WhenAll` or equivalent
- Collect per-tenant results: `Stale`, `Resolved`, `NoViolation`, `Commanded`

After the group evaluates:
- If ALL tenants in the group produced `Commanded` result → advance to next lower-priority group
- If ANY tenant produced `Stale`, `Resolved`, or `NoViolation` → stop, do not evaluate further groups

**Why this semantics:** The priority model assumes higher-priority tenants are the primary targets.
Lower-priority tenants should only be activated when the primary remediation strategy has been
fully applied (all primary tenants are in violation and commands are being sent). A single
non-violating tenant in the high-priority group means the primary strategy is not yet saturated.

**Empty group edge case:** A group with zero tenants (all filtered by config reload) produces
vacuous `Commanded` result and allows advance. This is correct — an empty group places no constraint.

**Complexity:** Medium — parallel within group requires careful result collection; the advance-gate
logic is simple but must be codified in a state machine or enum.

---

### TS-06: Suppression Cache — Per-Tenant, Keyed by (Ip, Port, CommandName)

**What:** A thread-safe in-memory cache that records when a command was last sent, preventing
duplicate commands within a suppression window.

Cache key: `(Ip, Port, CommandName)` — not tenant-scoped. If two tenants share the same command
target, sending the command for one tenant suppresses it for the other.

**Why not tenant-scoped:** A device does not know which tenant issued a SET command. If two tenants
both target `(10.0.0.1, 161, obp_set_bypass_L1)`, sending the command twice in rapid succession
would cause the device to see two identical SET requests. The suppression key should represent the
physical command target, not the logical tenant.

Cache behavior:
- On successful command dispatch: record `(key, UtcNow)` in cache
- On evaluation: if `UtcNow - cache[key] < suppressionWindowSeconds` → suppressed
- On cache miss: not suppressed

Suppression window: configurable per-job via `SnapshotJobOptions.SuppressionWindowSeconds`
(default: same as job interval, i.e., 15s). A single value applies to all command targets.

Cache eviction: no explicit eviction needed. Entries expire naturally — on next evaluation, the
age check determines if suppression is still active. The cache grows at most one entry per unique
command target, bounded by the number of distinct `(Ip, Port, CommandName)` tuples in config.

**Thread safety:** `ConcurrentDictionary<string, DateTimeOffset>` with key formatted as
`"{Ip}:{Port}:{CommandName}"`. Same pattern as `LivenessVectorService`.

**Complexity:** Low — ConcurrentDictionary with timestamp comparison.

---

### TS-07: Command OID Resolution at Execution Time

**What:** When a command is dequeued for execution:
1. Call `ICommandMapService.ResolveCommandOid(command.CommandName)` to get the OID
2. If null (command name not in map): log Warning with tenant ID and command name, increment
   `snmp.command.failed`, skip command. Do NOT abort other commands in the same tenant.
3. If found: proceed to SNMP SET

**Why at execution time, not at queue time:** The command map supports hot-reload. A command name
that resolves successfully when queued might have its OID changed by a config reload before
execution. Resolving at execution time ensures the most current mapping is used. The window is
short (sub-second in practice), so this is a correctness preference over a practical concern.

**Complexity:** Low — existing `ICommandMapService.ResolveCommandOid()` method already exists.

---

### TS-08: SNMP SET Execution via `Messenger.SetAsync`

**What:** For each command to execute:

```
oid = ICommandMapService.ResolveCommandOid(command.CommandName)
variable = new Variable(new ObjectIdentifier(oid), BuildSnmpData(command.Value, command.ValueType))
endpoint = new IPEndPoint(IPAddress.Parse(command.Ip), command.Port)
community = new OctetString(communityString)  // resolved from DeviceRegistry by Ip+Port
response = await Messenger.SetAsync(VersionCode.V2, endpoint, community, [variable], ct)
```

`Messenger.SetAsync(VersionCode, IPEndPoint, OctetString, IList<Variable>, CancellationToken)`
is confirmed available in SharpSnmpLib 12.5.7 (same library version already used by the project).

**ValueType → ISnmpData mapping:**
- `"Integer32"` → `new Integer32(int.Parse(value))`
- `"IpAddress"` → `new IP(value)` (SharpSnmpLib's `IP` type for IpAddress varbinds)
- `"OctetString"` → `new OctetString(value)`

Invalid `ValueType` was already rejected at config load time (v1.7 validation). This is not a
runtime concern.

**Community string resolution:** The SET uses the community string associated with the device at
`(Ip, Port)` in `DeviceRegistry`. The existing `IDeviceRegistry.TryGetByIpPort()` provides this.
If the device is not found: log Warning, increment `snmp.command.failed`, skip command.

**Timeout:** Same pattern as `MetricPollJob` — `CancellationTokenSource.CreateLinkedTokenSource`
with `CancelAfter(TimeSpan.FromSeconds(intervalSeconds * timeoutMultiplier))`. Use
`SnapshotJobOptions.TimeoutMultiplier` (default: 0.5, meaning 7.5s timeout on a 15s interval).

**Complexity:** Medium — mirrors `MetricPollJob` SNMP call pattern, but writes instead of reads.
The `ValueType → ISnmpData` dispatch is new code.

---

### TS-09: SET Response Dispatched Through Full MediatR Pipeline

**What:** The SNMP SET response returns one or more `Variable` objects (the current value of the
OID after SET, per SNMP protocol). Each returned variable is dispatched as:

```csharp
new SnmpOidReceived
{
    Oid        = variable.Id.ToString(),
    AgentIp    = IPAddress.Parse(command.Ip),
    DeviceName = device.Name,
    Value      = variable.Data,
    Source     = SnmpSource.Command,     // new enum value from TS-01
    TypeCode   = variable.Data.TypeCode,
    PollDurationMs = null
}
```

sent via `ISender.Send(msg, ct)` — the full Logging → Exception → Validation → OidResolution →
ValueExtraction → TenantVectorFanOut → OtelMetricHandler pipeline.

**Why full pipeline:** The response value shows what the device actually SET (may differ from
requested value if device clamps or rejects). Recording this through the standard metric path
means the response appears in Prometheus under `snmp_gauge{source="command"}`, queryable in
Grafana. The full pipeline also provides logging, error handling, and tenant vector fan-out
(the SET response may update a tenant's metric slot if the OID is in its routing table).

**OidResolution bypass for Command source:** `OidResolutionBehavior` must guard `Command` source
identically to `Synthetic`:

```csharp
if (msg.Source == SnmpSource.Synthetic || msg.Source == SnmpSource.Command)
    return await next();
```

`MetricName` is pre-set on the message (resolved from command map) so the bypass is safe.

**TenantVectorFanOut behavior for Command source:** No special guard needed. Fan-out routes by
`(ip, port, metricName)`. If a tenant's slot matches the returned OID's metric name, the fan-out
writes the confirmed-SET value into the slot — this is the correct behavior (the slot now reflects
the device's confirmed state, not stale poll data).

**Complexity:** Low — follows the Synthetic dispatch pattern exactly. Main addition is pre-setting
`MetricName` on the message.

---

### TS-10: `snmp.command.*` Pipeline Counters

**What:** Three new counters in `PipelineMetricService` on the `SnmpCollector` meter (same meter
as `snmp.poll.*`, exported by ALL instances):

| Counter | When Incremented | Tags |
|---------|-----------------|------|
| `snmp.command.sent` | After `Messenger.SetAsync` returns without exception | `device_name` |
| `snmp.command.failed` | On any command execution failure (OID unresolved, device not found, SetAsync throws, timeout) | `device_name` |
| `snmp.command.suppressed` | When a command is skipped due to active suppression window | `device_name` |

**Why `device_name` tag:** Matches existing counter taxonomy. `snmp.poll.executed` uses
`device_name`. Command counters target a device (resolved from `CommandSlotOptions.Ip/Port`) so
the same label applies.

**Symmetry with snmp.poll.*:** `sent` ≈ `poll.executed`, `failed` ≈ `poll.unreachable`,
`suppressed` has no poll analog (suppression is command-specific). This symmetry makes dashboard
construction natural.

**Complexity:** Low — three new counter fields and Add calls in PipelineMetricService.

---

### TS-11: SnapshotJob Stamps Liveness Vector

**What:** SnapshotJob calls `ILivenessVectorService.Stamp(jobKey)` in its `finally` block,
unconditionally (same as `CorrelationJob`, `MetricPollJob`, `HeartbeatJob`).

**Why:** `LivenessHealthCheck` iterates all registered job keys and declares the pod unhealthy
if any registered job's stamp is stale beyond `intervalSeconds × graceMultiplier`. A SnapshotJob
that is frozen or crashing repeatedly will be detected by liveness probes and the pod will be
restarted.

**JobIntervalRegistry registration:** SnapshotJob registers its interval in `IJobIntervalRegistry`
on startup (same as other jobs). `LivenessHealthCheck.CheckHealthAsync` already reads from this
registry to compute thresholds.

**Complexity:** Trivial — two lines in the job's `finally` block.

---

### TS-12: `SnapshotJobOptions` Configuration Model

**What:** New configuration section `SnapshotJob` in `appsettings.json` / ConfigMap:

```json
{
  "SnapshotJob": {
    "IntervalSeconds": 15,
    "SuppressionWindowSeconds": 15,
    "TimeoutMultiplier": 0.5
  }
}
```

| Field | Default | Meaning |
|-------|---------|---------|
| `IntervalSeconds` | 15 | Quartz trigger interval |
| `SuppressionWindowSeconds` | 15 | How long after a sent command to suppress retries |
| `TimeoutMultiplier` | 0.5 | `IntervalSeconds × TimeoutMultiplier` = SET request timeout |

**Validation:**
- `IntervalSeconds` must be > 0
- `SuppressionWindowSeconds` must be >= 0 (0 means no suppression — always send)
- `TimeoutMultiplier` must be in (0, 1.0] — must leave positive margin before next trigger

**Complexity:** Low — same pattern as `HeartbeatJobOptions`, `PollOptions`.

---

### TS-13: Background Worker for Command Execution

**What:** A `Channel<CommandRequest>`-backed background worker decouples evaluation from
execution. SnapshotJob enqueues commands; the worker dequeues and executes them.

**Why not execute inline in SnapshotJob:** SnapshotJob must complete within its interval (15s).
If multiple tenants across multiple groups each queue 2-3 commands, and each SET takes 1-2s,
inline execution blocks the job from completing on time. A background worker with its own
concurrency limit prevents job timeout from affecting command delivery.

**Worker behavior:**
- Single background consumer (`IHostedService`) that processes commands serially (not parallel)
- Commands are processed in FIFO order
- Worker shutdown drains the channel gracefully (same `GracefulShutdownService` pattern)
- Channel capacity: bounded (e.g., 100) with `DropOldest` on overflow — same backpressure
  pattern as `TrapChannel`. Log a Warning when a command is dropped due to backpressure.

**Complexity:** Medium — follows `ChannelConsumerService` / `TrapChannel` pattern. The key
difference is the channel carries typed `CommandRequest` objects rather than varbind envelopes.

---

## Differentiators

Features that add operational visibility without being required for correctness.

---

### D-01: Structured Evaluation Logs per Tenant per Job Run

**What:** At each job execution, emit a structured log entry per tenant summarizing the evaluation
outcome:

```
SnapshotJob tenant {TenantId} priority={Priority}: result={Stale|Resolved|NoViolation|Commanded}
[stale_slots={N}] [resolved_violated={N}/{Total}] [evaluate_violated={N}/{Total}] [commands_queued={N}]
```

Log at `Debug` for `Stale`, `Resolved`, `NoViolation`; log at `Information` for `Commanded`.

**Why:** Without per-tenant logs, diagnosing "why didn't tenant X issue a command?" requires
adding breakpoints or temporary log lines. These structured logs let operators run `kubectl logs`
and immediately see the evaluation state for each tenant on each cycle.

**Complexity:** Low — structured log emission during evaluation loop.

---

### D-02: Evaluation Outcome Counter per Result Type

**What:** Four counters on the `SnmpCollector` meter tracking how often each evaluation outcome
fires per tenant:

| Counter | Meaning |
|---------|---------|
| `snmp.snapshot.stale` | Tenant evaluation stopped at Tier 1 (staleness) |
| `snmp.snapshot.resolved` | Tenant evaluation stopped at Tier 2 (resolved state) |
| `snmp.snapshot.no_violation` | Tenant evaluation reached Tier 3 but no threshold was violated |
| `snmp.snapshot.commanded` | Tenant reached Tier 4 and commands were queued |

Tags: `tenant_id`.

**Value Proposition:** Operations dashboard can show evaluation state per tenant over time.
A tenant that is stuck in `stale` for an extended period indicates a polling problem upstream.
A tenant that never reaches `commanded` despite expected threshold violations indicates a config
problem (thresholds not set, or `Role` assignments wrong).

**Complexity:** Low — four new counters.

---

### D-03: Command Execution Log With Round-Trip Duration

**What:** When a command is executed, log at `Information`:

```
SnapshotJob SET command sent: tenant={TenantId} device={DeviceName} command={CommandName}
oid={Oid} value={Value} duration_ms={N}
```

When a command is suppressed, log at `Debug`:

```
SnapshotJob SET suppressed: tenant={TenantId} command={CommandName} age_ms={N} window_ms={N}
```

**Why duration_ms:** Network round-trip for SET requests to real hardware can vary widely.
Outlier durations surface device responsiveness problems that don't trigger the unreachability
tracker (single SET is not tracked for consecutive failures).

**Complexity:** Trivial — `Stopwatch` around `Messenger.SetAsync`.

---

### D-04: Suppression Cache Diagnostics in Health Endpoint

**What:** Expose current suppression cache state in the liveness or readiness health check
response data:

```json
{
  "suppressionCache": {
    "entryCount": 3,
    "oldestEntryAgeSeconds": 42.1
  }
}
```

**Why:** If suppression is preventing commands from being sent (misconfigured window), operators
need a way to diagnose this without looking at logs. The health endpoint is always accessible.

**Complexity:** Low — the suppression cache is a `ConcurrentDictionary`; count and age are O(n)
reads.

---

### D-05: `snmp.command.sent` Panel on Operations Dashboard

**What:** Add a new time-series panel to the existing operations dashboard JSON for
`snmp.command.sent`, `snmp.command.failed`, and `snmp.command.suppressed`.

**Why:** The operations dashboard (Phase 18) already shows all `snmp.poll.*` and `snmp.trap.*`
counters. Command counters belong in the same dashboard for operational completeness.

**Complexity:** Low — adding a panel to an existing dashboard JSON.

---

## Anti-Features

Things to deliberately NOT build.

---

### AF-01: Retry Logic on Failed SET Commands

**What:** Do NOT retry a failed SNMP SET automatically.

**Why Avoid:** SET commands have side effects. A command that timed out (no response received)
may or may not have been applied by the device — the agent cannot know. Retrying automatically
could double-apply a command on a slow device, causing oscillation (bypass enabled, then disabled,
then enabled again within one evaluation cycle). The correct behavior is: command failed, log it,
increment `snmp.command.failed`, let the next SnapshotJob cycle re-evaluate and re-queue if
criteria are still met.

**What to Do Instead:** The next SnapshotJob cycle is the retry mechanism. With `IntervalSeconds = 15`,
the retry interval is at most 15 seconds, which is acceptable for control-plane operations.

---

### AF-02: Threshold Hysteresis or Debouncing

**What:** Do NOT implement a "must be violated for N consecutive cycles before commanding" logic.

**Why Avoid:** This is premature generalization. The current spec is: violated on this cycle → command.
Hysteresis adds state per tenant (violation streak counter), complicates configuration (new options),
and creates a new failure mode (streak counter not reset on config reload). If oscillation is a
problem in practice, the suppression window (TS-06) already provides a damping effect: a command
sent on cycle N cannot be resent until the suppression window expires, even if the tenant remains
in violation.

**What to Do Instead:** Operators tune `SuppressionWindowSeconds` to control command frequency.
Hysteresis is an explicit future milestone scope item if it becomes necessary.

---

### AF-03: Cross-Tenant Command Coordination

**What:** Do NOT implement logic to prevent two tenants from issuing conflicting commands to
the same device (e.g., tenant A sends "bypass=1" while tenant B sends "bypass=0").

**Why Avoid:** The priority group model is the coordination mechanism. Higher-priority tenants
are processed first; lower-priority tenants only receive commands when all higher-priority tenants
are in violation. If two tenants in the same priority group both target the same command, the
suppression cache (TS-06) prevents the second from firing within the window. Cross-tenant conflict
resolution beyond this is an application-level concern that belongs in the tenant configuration,
not in the agent.

**What to Do Instead:** Operators design tenant priority groups to avoid conflicting commands.
The suppression cache handles same-target deduplication within a window.

---

### AF-04: SNMP GET to Verify SET Result

**What:** Do NOT issue a follow-up SNMP GET after each SET to verify the device accepted the value.

**Why Avoid:** The SET response from the device already includes the current value of the OID after
the SET (per SNMPv2c protocol). Dispatching this response through the MediatR pipeline (TS-09)
records the confirmed value. A follow-up GET would be a redundant round-trip. If the device rejects
the SET with an SNMP error, `Messenger.SetAsync` throws an exception, which is caught as a failure.

**What to Do Instead:** The MediatR pipeline dispatch of the SET response (TS-09) is the
verification mechanism. The confirmed value appears in `snmp_gauge{source="command"}` in Prometheus.

---

### AF-05: Command Queue Persistence

**What:** Do NOT persist the command queue to durable storage (database, Kubernetes ConfigMap, etc.).

**Why Avoid:** SnapshotJob re-evaluates all tenants every 15 seconds. Any command that was queued
but not executed before a pod restart will be re-queued within one evaluation cycle after restart.
Command state is ephemeral by design — the evaluation criteria (metric slots) survive restart because
the tenant vector registry is rebuilt from config, but all metric slot values will be re-populated
within one poll cycle. A 15-30 second gap in command dispatch after pod restart is acceptable.

**What to Do Instead:** The in-memory `Channel<CommandRequest>` with `[DisallowConcurrentExecution]`
on `SnapshotJob` is sufficient. Pod restarts are handled by the K8s replica model; one of the three
replicas will resume evaluation within seconds.

---

### AF-06: Parallel Command Execution

**What:** Do NOT execute commands for multiple tenants in parallel.

**Why Avoid:** The background worker (TS-13) processes commands serially to simplify failure
handling and avoid concurrent SNMP SET floods to devices. SNMP devices can typically handle one
SET at a time on a given OID. Parallel execution would require per-device concurrency control
(the same pattern as the poll scheduler) which adds substantial complexity for minimal gain —
SnapshotJob issues at most a handful of commands per cycle.

**What to Do Instead:** Serial background worker with bounded channel. If throughput becomes
a bottleneck, per-device concurrency control is a future milestone.

---

## Feature Dependencies

```
TS-01 (SnmpSource.Command)
    |
    +--> TS-09 (SET response dispatch uses Source=Command)
    +--> OidResolutionBehavior guard (bypass Command same as Synthetic)

TS-02 (MetricSlotHolder.Role)
    |
    +--> TS-03 (Tier 1 staleness must iterate all holders)
    +--> TS-04 (Tier 2/3 partition by Role — requires Role on holder)
    +--> TenantVectorRegistry.Reload (must populate Role from MetricSlotOptions.Role)

TS-03 (Staleness — Tier 1)
    +--> TS-04 (Tier 2/3 only run if Tier 1 passes)

TS-04 (Threshold check — Tiers 2 and 3)
    +--> TS-05 (group traversal consumes per-tenant results)
    +--> TS-06 (Tier 4 suppression check follows Tier 3 violation)

TS-05 (Priority group traversal)
    +--> TS-06 (suppression cache is consulted inside traversal)

TS-06 (Suppression cache)
    +--> TS-10 (snmp.command.suppressed counter incremented here)

TS-07 (Command OID resolution)
    +--> TS-08 (OID required before SetAsync call)

TS-08 (Messenger.SetAsync)
    +--> TS-09 (response dispatched through MediatR)
    +--> TS-10 (sent/failed counters incremented here)

TS-12 (SnapshotJobOptions)
    +--> TS-11 (IntervalSeconds used for liveness registration)
    +--> TS-13 (IntervalSeconds used for timeout calculation)

TS-13 (Background worker)
    +--> TS-07, TS-08, TS-09 (execution path lives in worker)
```

### Critical Path

```
TS-01  →  TS-09  (OidResolution bypass for Command source)
TS-02  →  TS-04  (Role on holder enables tier partitioning)
TS-12  →  TS-03  →  TS-04  →  TS-05  →  TS-06  →  TS-07  →  TS-08  →  TS-09  →  TS-10
```

---

## Tier Edge Case Matrix

The 4-tier evaluation has several non-obvious edge cases. Documenting them here so implementation
plans can derive explicit test cases.

### Tier 1: Staleness

| Condition | Expected Result |
|-----------|-----------------|
| No metric slots on tenant | Not stale (vacuous pass — no slots to be stale) |
| All slots have values, all fresh | Not stale |
| One slot has no value yet (null from ReadSlot) | Stale |
| One slot is Trap-sourced with old timestamp | NOT stale (Trap excluded from staleness) |
| All slots are Trap-sourced | Not stale (all excluded — vacuous pass) |
| One Poll slot with IntervalSeconds=0 | Not stale (zero-interval excluded) |
| One Poll slot expired beyond grace threshold | Stale |

### Tier 2: Resolved Check

| Condition | Expected Result |
|-----------|-----------------|
| No Resolved-role holders | Vacuous pass → continue to Tier 3 |
| No Resolved-role holders WITH thresholds | Vacuous pass → continue to Tier 3 |
| 2 Resolved holders, both violated | ALL violated → stop, no command |
| 2 Resolved holders, 1 violated, 1 not | Not all violated → continue to Tier 3 |
| 2 Resolved holders, neither violated | Not all violated → continue to Tier 3 |

### Tier 3: Evaluate Check

| Condition | Expected Result |
|-----------|-----------------|
| No Evaluate-role holders | Vacuous fail → no command (log Warning on startup) |
| No Evaluate-role holders WITH thresholds | Vacuous fail → no command |
| 2 Evaluate holders, both violated | ALL violated → queue command |
| 2 Evaluate holders, 1 violated, 1 not | Not all violated → no command |
| Evaluate holder with null Threshold | Excluded from count — does not affect "all violated" |

### Priority Group Advance

| Group 1 Result | Action |
|----------------|--------|
| All Commanded | Advance to Group 2 |
| Any Stale | Stop, do not advance |
| Any Resolved | Stop, do not advance |
| Any NoViolation | Stop, do not advance |
| Group 1 empty | Vacuous Commanded → advance to Group 2 |

---

## Pipeline Counter Impact Analysis

How the new `snmp.command.*` counters relate to existing counters.

| Counter | Incremented By | Notes |
|---------|---------------|-------|
| `snmp.command.sent` | Background worker, after successful SetAsync | Per device |
| `snmp.command.failed` | Background worker, on OID unresolved, device not found, SetAsync throws | Per device |
| `snmp.command.suppressed` | SnapshotJob evaluation loop, in Tier 4 | Per device |
| `snmp.event.published` | MediatR pipeline, existing behavior | SET response goes through pipeline, increments this |
| `snmp.event.handled` | OtelMetricHandler, existing behavior | SET response handler fires, increments this |
| `snmp.tenantvector.routed` | TenantVectorFanOutBehavior, existing behavior | SET response fan-out may write to matching tenant slots |

The SET response dispatched through MediatR (TS-09) WILL increment `snmp.event.published` and
`snmp.event.handled`. This is intentional — the command response is a legitimate pipeline event.
It will be distinguishable from poll events by `source="command"` on `snmp_gauge`.

---

## MVP Recommendation

**Must build (13 — all table stakes):**

1. **TS-01** `SnmpSource.Command` enum value + OidResolutionBehavior guard
2. **TS-02** `MetricSlotHolder.Role` property populated at registry reload
3. **TS-03** Tier 1 staleness detection with trap exclusion and zero-interval guard
4. **TS-04** Tier 2/3 threshold violation check with vacuous-pass/fail semantics
5. **TS-05** Priority group traversal: parallel within group, sequential across, "all violated" advance gate
6. **TS-06** Suppression cache: ConcurrentDictionary keyed by `(Ip:Port:CommandName)`
7. **TS-07** Command OID resolution at execution time via ICommandMapService
8. **TS-08** SNMP SET execution via `Messenger.SetAsync` with ValueType dispatch
9. **TS-09** SET response dispatched through full MediatR pipeline with Source=Command
10. **TS-10** `snmp.command.sent`, `snmp.command.failed`, `snmp.command.suppressed` counters
11. **TS-11** SnapshotJob stamps liveness vector in finally block
12. **TS-12** `SnapshotJobOptions` config model with validation
13. **TS-13** Background worker for command execution (Channel-backed, bounded, serial)

**Should build (differentiators — low cost, high operational value):**

1. **D-01** Structured evaluation log per tenant per job run (Debug/Information)
2. **D-03** Command execution log with round-trip duration
3. **D-05** `snmp.command.*` panel on operations dashboard

**Evaluate before committing:**

- **D-02** Evaluation outcome counters per tenant — adds cardinality by `tenant_id` label. Worthwhile
  if there are few tenants (< 20); risks cardinality explosion at scale.
- **D-04** Suppression cache diagnostics in health endpoint — useful for debugging, adds minor
  complexity to `ReadinessHealthCheck` or `LivenessHealthCheck`.

**Explicitly do NOT build:**

- AF-01: Retry on failed SET — creates double-apply risk
- AF-02: Hysteresis / debouncing — suppression window is the damping mechanism
- AF-03: Cross-tenant command conflict prevention — use priority groups
- AF-04: Follow-up GET after SET — protocol response is the verification
- AF-05: Command queue persistence — evaluation re-queues on next cycle
- AF-06: Parallel command execution — serial worker is sufficient

---

## Structural Gaps to Resolve in Implementation Plans

The following design decisions are flagged for the implementation planner because they are
consequential and not yet resolved by this feature research:

1. **`MetricSlotHolder.Role` type:** string constant ("Evaluate"/"Resolved") or new enum
   `MetricSlotRole`. The enum is cleaner (no stringly-typed comparisons at evaluation time) but
   requires a new type. The implementation plan should decide and be consistent with the existing
   `MetricSlotOptions.Role` string field.

2. **`Tenant.Commands` property:** `Tenant` (the runtime domain type) currently holds only
   `IReadOnlyList<MetricSlotHolder>`. To dispatch commands, SnapshotJob needs access to
   `CommandSlotOptions` per tenant. Either `Tenant` grows a `Commands` property (list of
   `CommandSlotOptions`), or `TenantVectorRegistry.Reload` must carry command config through
   separately. The `Tenant` property approach is cleaner.

3. **Community string for SET:** The target device's community string must be resolved from
   `IDeviceRegistry.TryGetByIpPort(command.Ip, command.Port)`. However, `CommandSlotOptions`
   uses `Ip` (not a DNS name), and `DeviceRegistry` may be keyed by config address (DNS or IP)
   not resolved IP. This needs verification against `DeviceRegistry.TryGetByIpPort` implementation
   and may require a new lookup method (e.g., by resolved IP).

4. **Background worker vs inline execution trade-off:** TS-13 recommends a Channel-backed background
   worker. If the number of commands per cycle is guaranteed to be small (e.g., at most 3-4 total),
   inline execution within SnapshotJob is simpler. The implementation plan should assess the
   concrete max commands per cycle before committing to a background worker.

---

## Sources

- Codebase: `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — `IntervalSeconds`, `GraceMultiplier`,
  `Threshold`, `Source`, `ReadSlot()`, no `Role` field (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/Tenant.cs` — `Id`, `Priority`, `Holders`, no `Commands`
  field (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/PriorityGroup.cs` — `Priority`, `Tenants` (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `Groups`, `Reload` pattern
  for atomic swap and carry-over (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — `Role: string`,
  `Threshold: ThresholdOptions?`, `IntervalSeconds`, `GraceMultiplier` (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — `Ip`, `Port`, `CommandName`,
  `Value`, `ValueType` (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/ThresholdOptions.cs` — `Min: double?`, `Max: double?`
  (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/SnmpSource.cs` — `Poll`, `Trap`, `Synthetic`; no `Command`
  yet (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/ICommandMapService.cs` — `ResolveCommandOid` method
  signature (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/ISnmpClient.cs` — `GetAsync` signature mirrored for SET
  (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/SharpSnmpClient.cs` — `Messenger.GetAsync` wrapper pattern
  (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/LivenessVectorService.cs` — `ConcurrentDictionary` stamp
  pattern for suppression cache model (HIGH confidence)
- Codebase: `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — counter taxonomy, meter
  names, `device_name` tag convention (HIGH confidence)
- Codebase: `src/SnmpCollector/Jobs/MetricPollJob.cs` — liveness stamp pattern, correlation
  pattern, SNMP call pattern, dispatch loop (HIGH confidence)
- Codebase: `src/SnmpCollector/Jobs/CorrelationJob.cs` — `DisallowConcurrentExecution`,
  `finally { _liveness.Stamp(jobKey) }` pattern (HIGH confidence)
- Codebase: `src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs` — how `IJobIntervalRegistry`
  is used for staleness threshold computation (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — Synthetic bypass
  pattern to extend for Command (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — fan-out routing
  by (ip, port, metricName) — Command responses naturally participate (HIGH confidence)
- SharpSnmpLib 12.5.7 API docs: `Messenger.SetAsync(VersionCode, IPEndPoint, OctetString,
  IList<Variable>, CancellationToken)` confirmed available — mirrors GetAsync signature
  ([Messenger Methods](https://help.sharpsnmp.com/html/Methods_T_Lextm_SharpSnmpLib_Messaging_Messenger.htm),
  MEDIUM confidence — API doc page verified, method list confirmed)
- PROJECT.md v2.0 spec — `SnapshotJob` description, 4-tier evaluation, priority group semantics,
  suppression cache, `snmp.command.*` counter spec, liveness stamping (HIGH confidence — user-authored)

---

*Feature research for: SnapshotJob Tenant Evaluation and Command Execution*
*Researched: 2026-03-16*
