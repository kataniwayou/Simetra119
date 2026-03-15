# Feature Landscape: Combined / Synthetic Metrics on Poll Groups

**Domain:** SNMP monitoring agent — poll-group-level aggregate computation
**Researched:** 2026-03-15
**Confidence:** HIGH — derived from full codebase analysis of MetricPollJob, OtelMetricHandler,
SnmpMetricFactory, PipelineMetricService, PollOptions, SnmpOidReceived, OidMapService, and
TelemetryConstants. No external library questions were in scope; all findings are grounded in the
existing implementation.

---

## Scope

A `PollOptions` (poll group) gains two new optional fields:

```json
{
  "MetricNames": ["ifInOctets", "ifOutOctets"],
  "IntervalSeconds": 30,
  "CombinedMetricName": "ifTotalOctets",
  "Action": "sum"
}
```

When `CombinedMetricName` and `Action` are both present, the poll job collects all SNMP responses
for the group, computes the aggregate, and dispatches it as a synthetic metric alongside (not
instead of) the individual per-OID metrics.

---

## Context: How the Current Poll Flow Works

Understanding the dispatch path is required before specifying combined metric behavior.

```
MetricPollJob.Execute()
    → _snmpClient.GetAsync(variables)                        // all OIDs in one GET
    → DispatchResponseAsync()
        → foreach variable in response
            → new SnmpOidReceived { Oid, Value, Source=Poll, TypeCode, PollDurationMs }
            → _sender.Send(msg)                              // MediatR pipeline per varbind
                → ValidationBehavior
                → OidResolutionBehavior      (sets msg.MetricName)
                → ValueExtractionBehavior    (sets msg.ExtractedValue)
                → TenantVectorFanOutBehavior (routes to tenant slots)
                → OtelMetricHandler          (calls ISnmpMetricFactory.RecordGauge)
```

Key observations for combined metrics design:

1. **Dispatch is per-varbind, not per-group.** `DispatchResponseAsync` iterates over the response
   list and sends one `SnmpOidReceived` per variable. There is no post-loop hook in the current
   job.
2. **Numeric values are extracted in the pipeline**, not in the job. `ExtractedValue` is populated
   by `ValueExtractionBehavior`. The job itself does not see the numeric values.
3. **`MetricPollJob` does have access to the full `pollGroup` object** — `device.PollGroups[pollIndex]`
   is in scope at the top of `Execute()`. This is where `CombinedMetricName` and `Action` will live.
4. **`ISnmpMetricFactory.RecordGauge` is the write path** for numeric metrics. Combined metrics
   must call `RecordGauge` on the `LeaderMeterName` meter (via `SnmpMetricFactory`) so they
   participate in leader-gated export correctly.

---

## Table Stakes

Features that MUST exist for combined metrics to be correct and safe.

---

### TS-01: `PollOptions` — `CombinedMetricName` and `Action` Fields

**What:** Two new optional fields on `PollOptions`:

```csharp
public string? CombinedMetricName { get; set; }
public string? Action { get; set; }   // "sum" | "diff" | "mean"
```

Both must be present together or both absent. One without the other is a configuration error (see
TS-05 on validation). When absent, poll group behavior is unchanged.

**Complexity:** Low — two nullable string properties on an existing model.

---

### TS-02: Combined Metric Computation — `sum`, `diff`, `mean`

**What:** After all individual per-varbind dispatches complete, `MetricPollJob.DispatchResponseAsync`
(or a new post-dispatch step in `Execute`) collects the numeric values and computes the aggregate.

#### sum

`result = v₁ + v₂ + … + vₙ`

Applies to all successfully received numeric values. Non-numeric OIDs (OctetString, IPAddress,
ObjectIdentifier) in the group are silently excluded from the sum — they have no `ExtractedValue`
equivalent. An all-string group produces a sum of 0.

**Edge cases:**

| Condition | Behavior |
|-----------|----------|
| 1 metric in group | sum = that value. Valid; no special handling needed. |
| 0 metrics returned (all NoSuchObject/NoSuchInstance/EndOfMibView) | sum is not computed; no combined metric emitted. See TS-06. |
| Partial response (some OIDs error, some succeed) | sum of the successfully returned numeric values. Note this in logs if any OID was skipped. |
| Overflow (sum of Counter64 values exceeding double precision) | `double` has 53-bit mantissa (~9×10¹⁵ max exact integer). Counter64 max is ~1.8×10¹⁹. Overflow is possible for large Counter64 sums. Use `double` anyway — same representation already used for Counter64 in `ExtractedValue`. Document the limitation; do not special-case. |
| All values are zero | sum = 0. Emitted normally. Zero is a valid metric value. |

#### diff

`result = v₁ − v₂`

Defined as: first OID value minus second OID value, in the order defined by `MetricNames[]` in config.
Does NOT mean delta over time — it means the arithmetic difference between two simultaneously polled
OID values (e.g., `ifInOctets - ifOutOctets` or `totalMemory - freeMemory`).

**Edge cases:**

| Condition | Behavior |
|-----------|----------|
| Exactly 1 metric in group | diff is undefined. **Emit 0** and log a Warning: "CombinedMetric diff requires at least 2 values; group '{GroupMetricNames}' produced 1 — emitting 0." Do NOT skip silently; emitting 0 surfaces the misconfiguration in Grafana. |
| Exactly 2 metrics | result = MetricNames[0] value − MetricNames[1] value. Canonical case. |
| 3+ metrics in group | result = MetricNames[0] value − MetricNames[1] value. Values at index 2+ are ignored for diff. Log Debug: "diff ignores {N-2} excess values beyond the first two." This is lenient — the group may intentionally contain many metrics but only the first two participate in diff. |
| 0 metrics returned | No combined metric emitted. See TS-06. |
| Negative result | Valid. A diff can be negative (e.g., outbound > inbound). `double` handles this correctly. Emitted as-is. |
| Partial response: first OID missing | Cannot compute. No combined metric emitted. Log Warning. |
| Partial response: second OID missing | Cannot compute. No combined metric emitted. Log Warning. |

#### mean

`result = (v₁ + v₂ + … + vₙ) / n`

where `n` is the count of successfully returned numeric values.

**Edge cases:**

| Condition | Behavior |
|-----------|----------|
| 1 metric in group | mean = that value (n=1). Valid; mathematically correct. |
| Integer SNMP values (Integer32, Counter32, Counter64, Gauge32) | All values are already stored as `double` in `ExtractedValue` (see `SnmpOidReceived.ExtractedValue: double`). Division uses `double` arithmetic. **No integer truncation.** `mean([3, 4])` = 3.5, not 3. This is the correct behavior — callers who want integer results can floor in PromQL. |
| 0 metrics returned | No combined metric emitted. See TS-06. |
| Division by zero | Cannot occur if 0-metrics case is handled by TS-06 (early exit before computing). |
| Mixed SNMP types (some Integer32, some Gauge32) | Valid — all are numeric, all have `ExtractedValue`. The mean is computed over all numeric values regardless of SNMP type. The `snmp_type` label on the synthetic metric will be set to `"combined"` (see TS-03). |

---

### TS-03: Combined Metric — Prometheus Metric Type and Labels

**What:** The synthetic combined metric is recorded via `ISnmpMetricFactory.RecordGauge()`, using
the same `snmp_gauge` instrument already registered on the `LeaderMeterName` meter.

**Justification — why Gauge, not Counter:**

The result of sum/diff/mean over instantaneous SNMP values is itself an instantaneous value. It
represents a point-in-time aggregate (e.g., "total octets across interfaces right now"), not a
monotonically increasing accumulation. The individual constituent OIDs — even Counter32/Counter64 —
are already recorded as gauges in `OtelMetricHandler` per the existing pattern (Counter raw values
are recorded as gauges; Prometheus `rate()` is applied by consumers). The combined metric follows
the same pattern: it is the arithmetic result of values at a moment in time, expressed as a gauge.

Recording as a Counter would be wrong: the sum of two instantaneous values is not a counter.
Recording as a Histogram would be wrong: we have one value per poll, not a distribution of samples.

**Labels for the synthetic metric:**

The `RecordGauge` signature is:
```csharp
void RecordGauge(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double value)
```

For a combined metric:

| Label | Value | Rationale |
|-------|-------|-----------|
| `metric_name` | `CombinedMetricName` from config | The name the operator chose (e.g., `"ifTotalOctets"`) |
| `oid` | `"combined"` | No single OID applies; use a sentinel string. Avoids null. |
| `device_name` | `device.Name` | Same as individual metrics — this is a per-device aggregate |
| `ip` | `device.ResolvedIp` | Same as individual metrics |
| `source` | `"poll"` | Combined metrics only arise from polling, never from traps |
| `snmp_type` | `"combined"` | Distinguishes from any single-OID snmp_type |

**Why `oid = "combined"` is correct:** The `oid` label on `snmp_gauge` currently serves as a
disambiguator when two metric names map to different OIDs. For a combined metric there is no
canonical OID. Using `"combined"` is honest and Grafana-filterable. Using `""` would produce an
empty label, which in some Prometheus exporters is treated as absent. Using a comma-joined OID list
would create high cardinality per label value permutation.

---

### TS-04: Combined Metric Dispatches AFTER Individual Metrics

**What:** The combined metric computation and `RecordGauge` call happen after `DispatchResponseAsync`
completes — not inside the per-varbind loop.

**Why this ordering matters:**

1. `DispatchResponseAsync` iterates the raw `IList<Variable>` response. At this point, numeric
   values are available in `variable.Data` directly (SharpSnmpLib provides `.ToInt64()` or
   type-specific accessors). The MediatR pipeline enriches `ExtractedValue` on `SnmpOidReceived`
   — but that enrichment only exists inside the pipeline message, not in the returned value.
2. Combined metric computation requires values for all OIDs in the group. They must all be
   collected before the aggregate is computed. The per-varbind loop is the only place where
   all values are available together.

**Implementation path:** Collect `(oid → double)` pairs from the `response` list during
`DispatchResponseAsync` (or as a separate pass over `response` after the dispatch loop). Match OIDs
back to `pollGroup.MetricNames` using the reverse map from `IOidMapService.ResolveToOid()` —
which the job already has access to through `device.PollGroups[pollIndex]`. Then compute and emit.

**No new MediatR notification needed.** The combined metric is recorded directly via `ISnmpMetricFactory`
injected into `MetricPollJob`. This avoids MediatR dispatch overhead for a simple arithmetic
operation, and avoids complicating `SnmpOidReceived` with combined-metric semantics it was not
designed for.

---

### TS-05: Configuration Validation — Action Must Be a Known Value

**What:** If `Action` is present but is not one of the known values (`sum`, `diff`, `mean`,
case-insensitive), the poll group must be rejected at configuration load time.

**Behavior:**

- Log at Error level: `"PollGroup[{Index}] for device '{DeviceName}': CombinedMetricName is set
  but Action '{Value}' is not a recognized action. Valid values: sum, diff, mean. Poll group
  will not be scheduled."`
- The poll group is skipped — no Quartz job is created for it.
- Other poll groups on the same device are unaffected.
- Existing "soft degradation with structured logging" pattern (same as `DeviceRegistry.BuildPollGroups()`
  handling for unresolvable OIDs).

**Additionally:** If exactly one of `CombinedMetricName` / `Action` is present without the other,
treat it as a configuration error with the same skip behavior and a distinct log message:
`"PollGroup[{Index}]: CombinedMetricName and Action must be specified together or not at all."`

**Case sensitivity:** `"Sum"`, `"SUM"`, `"sum"` are all valid — normalized with
`StringComparison.OrdinalIgnoreCase` at parse time.

**Complexity:** Low — string comparison at device config load time.

---

### TS-06: No Combined Metric Emitted When Response Is Empty or All-Error

**What:** If the SNMP GET response produces zero successfully received numeric values for the group
(all varbinds were NoSuchObject/NoSuchInstance/EndOfMibView, or all values were non-numeric string
types), the combined metric is NOT emitted.

**Why:** Emitting a combined metric of 0 when the device returned no data would be misleading —
a Grafana panel would show "0 total octets" when the reality is "no data." The absence of a
data point is more honest than a zero.

**Behavior:**
- Log at Debug: `"CombinedMetric '{CombinedMetricName}' not emitted — no numeric values in response."`
- `snmp.event.published` and `snmp.event.handled` counters are NOT incremented (the combined
  metric bypasses the MediatR pipeline entirely, per TS-04).
- No counter for "combined metric skipped" is added in this milestone (see AF-03).

**Contrast with partial response:** If at least one numeric value was received, the combined
metric IS emitted (using whatever values are available). The partial case for `diff` (missing
first or second OID) is an exception — see TS-02.

---

### TS-07: `CombinedMetricName` Must Not Conflict With OID Map Metric Names

**What:** At configuration load time, validate that `CombinedMetricName` does NOT exist as a
metric name in the current OID map (`IOidMapService.ContainsMetricName()`).

**Why this matters:** `snmp_gauge` is partitioned by `{metric_name, oid}` label combination.
If `CombinedMetricName = "ifInOctets"` and `ifInOctets` is also a real OID-mapped metric, both
the real polled value and the combined synthetic value would emit data points under
`metric_name="ifInOctets"` — but the real one has `oid="1.3.6.1.2.1.2.2.1.10"` and the synthetic
one has `oid="combined"`. Prometheus will store both as separate time series (different label sets),
but a naive Grafana query for `snmp_gauge{metric_name="ifInOctets"}` would return two series,
confusing operators.

**Behavior on conflict:**

- Log at Warning: `"PollGroup[{Index}]: CombinedMetricName '{Name}' conflicts with an OID map
  metric name. The synthetic metric will be emitted with oid='combined' and may overlap real
  metrics in queries. Consider renaming."`
- **The poll group is NOT skipped.** A naming conflict is a warning, not a fatal error. The
  operator may intentionally reuse a name. The metrics are technically distinguishable by the
  `oid` label. The Warning is sufficient to surface the issue.

**Timing note:** OID map is hot-reloaded independently. This check runs only at device config
load time. If the OID map is later updated to add a name that conflicts with an existing
`CombinedMetricName`, no retroactive warning fires. This is acceptable — the same gap exists for
all name-based validations in the system.

---

## Differentiators

Features that add operational value without being required for correctness.

---

### D-01: `snmp.combined.computed` Pipeline Counter

**What:** Add a new counter `snmp.combined.computed` to `PipelineMetricService` on the
`SnmpCollector` meter (not the leader meter — this is pipeline health data, not business data).

```
snmp.combined.computed{device_name="NPB-01", action="sum"} 42
```

Tags: `device_name`, `action`.

**Value Proposition:** The operations dashboard can show "how many combined metrics were computed
per device per action type." Alerts can fire if this count drops to zero for a device that should
be producing combined metrics.

**Why a differentiator:** Not required for correctness. The combined metric itself appears in
`snmp_gauge` and can be queried. But without this counter, there is no way to distinguish
"combined metric computed and emitted 0" from "combined metric computation was skipped (TS-06)."

**Complexity:** Low — one new counter field and one `Add` call in `PipelineMetricService`.

---

### D-02: `PollDurationMs` on Combined Metric

**What:** Pass the same `pollDurationMs` to a `RecordGaugeDuration` call for the combined metric,
using `CombinedMetricName` as the `metricName` and `"combined"` as the `oid`.

**Value Proposition:** The operations dashboard already shows poll duration per metric. Including
the combined metric means its latency profile is visible alongside the individual metrics it
was derived from.

**Complexity:** Low — same `pollDurationMs` value already available in `Execute()` is forwarded
to the combined metric recording.

---

### D-03: Warn When `diff` Has More Than 2 Values

**What:** When `Action = "diff"` and `MetricNames[]` contains more than 2 entries, log a Warning
(not just Debug) at configuration load time:

`"PollGroup[{Index}]: Action 'diff' with {N} MetricNames uses only the first two. The remaining
{N-2} will be polled but not included in the diff. If this is intentional, consider moving them
to a separate poll group."`

**Value Proposition:** Diff with 3+ metrics is almost always a configuration mistake. The Warning
at load time surfaces this before the operator wonders why the diff seems wrong.

**Complexity:** Trivial — count check at load time.

---

## Anti-Features

Things to deliberately NOT build for combined metrics.

---

### AF-01: Delta-Over-Time Computation

**What:** Do NOT compute the change in a metric between consecutive polls (e.g., "rate of counter
increase over 30 seconds").

**Why Avoid:** Prometheus `rate()` and `increase()` already do this on the raw Counter32/Counter64
gauges that are already emitted. Building delta-over-time in the agent requires state (previous
value per OID per device), restart handling (what's the baseline after restart?), rollover handling
(Counter32 wraps at 2³²). This is non-trivial stateful logic for a computation Prometheus handles
correctly.

**What to Do Instead:** Operators write `rate(snmp_gauge{metric_name="ifInOctets"}[5m])` in
Grafana. `Action = "sum"` over two instantaneous values is sufficient for additive aggregates.

---

### AF-02: Cross-Device Aggregation

**What:** Do NOT aggregate values across multiple devices within a single combined metric (e.g.,
"total CPU across all OBP devices").

**Why Avoid:** `MetricPollJob` operates on a single device. A cross-device aggregate would require
reading the current value of another device's metric from Prometheus or from a shared in-memory
store — both of which break the clean isolation of the poll job. Cross-device aggregation belongs
in Grafana or a recording rule.

**What to Do Instead:** Operators use PromQL `sum(snmp_gauge{metric_name="hrProcessorLoad"})` to
aggregate across devices.

---

### AF-03: `snmp.combined.skipped` Counter

**What:** Do NOT add a counter for "combined metric was not emitted due to empty response."

**Why Avoid:** The TS-06 skip scenario (all OIDs returned NoSuchObject) is already captured by
the individual per-OID Debug logs. Adding a separate counter for the skip case creates a parallel
counting mechanism. If the combined metric is not appearing in Grafana, the operator looks at the
individual OID metrics to understand why the group has no data — the diagnostic is already there.

**What to Do Instead:** `snmp.combined.computed` (D-01) implicitly shows skips via absence: if
`snmp.combined.computed` for a device drops to zero when it should be non-zero, that surfaces
the issue.

---

### AF-04: MediatR Notification for Combined Metric

**What:** Do NOT create a new `CombinedMetricComputed` MediatR notification or push the combined
metric through the existing `SnmpOidReceived` pipeline.

**Why Avoid:**
- The existing `SnmpOidReceived` message is shaped around a single OID with a raw `ISnmpData`
  value and a `TypeCode`. A combined metric has no OID, no raw SNMP value, and no SNMP type.
  Forcing it into the existing shape requires nullable workarounds or a sentinel SNMP type.
- A new notification type would require new pipeline behaviors, a new terminal handler, and new
  test coverage — disproportionate for an arithmetic operation.
- `TenantVectorFanOutBehavior` currently routes based on `(AgentIp, Port, MetricName)`. Combined
  metrics are poll-group-level aggregates and are not tenant-routed. Running them through the
  fan-out behavior silently (by having no matching slot) is confusing.

**What to Do Instead:** Direct `ISnmpMetricFactory.RecordGauge()` call in `MetricPollJob` after
the dispatch loop (per TS-04).

---

### AF-05: `snmp_info` Combined Metric

**What:** Do NOT compute combined metrics on string OID values (OctetString, IPAddress,
ObjectIdentifier) — `diff` or `sum` of strings is meaningless.

**Why Avoid:** SNMP string values carry no numeric semantics. A "sum" or "mean" of interface
descriptions is not a useful metric. A "diff" of two IP addresses is not defined.

**What to Do Instead:** Poll groups that mix string and numeric OIDs can still have a combined
metric — the string OIDs are excluded from computation (TS-02 specifies that only numeric values
participate). But you cannot define a combined metric over exclusively string OIDs.

---

### AF-06: Combined Metric Participates in Tenant Vector Fan-Out

**What:** Do NOT route the combined metric through `TenantVectorFanOutBehavior` and into tenant
`MetricSlotHolder` time series.

**Why Avoid:** Tenant vector routing is driven by `(ip, port, metricName)` tuples registered in
`TenantVectorRegistry`. A combined metric produced by a poll group is a device-level construct,
not a tenant-declared metric. A tenant that wants to observe a combined metric should declare it
explicitly in `tenants.json` (future capability). Implicitly routing combined metrics through
the tenant vector would bypass the explicit tenant declaration model.

**What to Do Instead:** Combined metrics appear in `snmp_gauge` on the leader exporter. Tenants
observe them via Prometheus queries if needed. Explicit tenant-level combined metric slots are
a future milestone concern.

---

## Feature Dependencies

```
TS-05 (Action validation)
    |
    +--> TS-01 (PollOptions fields — required before validation can run)

TS-02 (sum/diff/mean computation)
    |
    +--> TS-01 (fields exist)
    +--> TS-04 (dispatch ordering — computation happens after individual dispatch)
    +--> TS-06 (empty-response guard — prerequisite for safe division in mean)

TS-03 (Prometheus type and labels)
    |
    +--> TS-02 (computed value exists before it can be recorded)
    +--> TS-04 (direct RecordGauge call, not MediatR)

TS-07 (OidMap name conflict warning)
    |
    +--> TS-01 (CombinedMetricName field exists)

D-01 (snmp.combined.computed counter)
    |
    +--> TS-02 (computation happens before counter increment)

D-02 (PollDurationMs on combined metric)
    |
    +--> TS-03 (RecordGaugeDuration shares the label structure of RecordGauge)
```

### Critical Path

```
TS-01  →  TS-05 (validation at load time)
TS-01  →  TS-07 (OidMap conflict check at load time)
TS-01  →  TS-02  →  TS-03  →  emit snmp_gauge
TS-02  →  TS-04 (ordering: after dispatch loop)
TS-02  →  TS-06 (guard: only emit when values exist)
```

---

## Pipeline Counter Impact Analysis

The question: how do combined metrics interact with the operations dashboard (`snmp.pipeline.*`
and `snmp.event.*` counters in `PipelineMetricService`)?

**Existing counters and their relationship to combined metrics:**

| Counter | Current Meaning | Impact from Combined Metrics |
|---------|-----------------|------------------------------|
| `snmp.event.published` | Every `SnmpOidReceived` notification sent into MediatR | **No change.** Combined metric bypasses MediatR (AF-04). |
| `snmp.event.handled` | Every notification reaching `OtelMetricHandler` successfully | **No change.** Combined metric calls `RecordGauge` directly. |
| `snmp.event.errors` | Exceptions in pipeline behaviors or handlers | **No change.** Errors in combined metric computation are caught in `MetricPollJob.Execute()` and don't use the pipeline error path. |
| `snmp.event.rejected` | Notifications discarded before reaching a handler | **No change.** |
| `snmp.poll.executed` | Every completed poll attempt (success or failure) | **No change.** This fires once per job execution regardless of combined metrics. |
| `snmp.poll.unreachable` | Device transitions to unreachable | **No change.** |
| `snmp.poll.recovered` | Device transitions back to healthy | **No change.** |
| `snmp.tenantvector.routed` | Fan-out writes to tenant metric slots | **No change.** Combined metrics do not fan-out (AF-06). |

**Net conclusion:** No existing pipeline counter is affected by combined metric implementation.
The combined metric path is a direct call to `ISnmpMetricFactory.RecordGauge()` inside
`MetricPollJob`, fully outside the MediatR pipeline. The only new counter added is `snmp.combined.computed`
(D-01), which is explicitly a new instrument.

**Exception handling:** If the combined metric computation throws (e.g., `IOidMapService` is null
for some reason, or a bug in the aggregation logic), the exception will propagate into
`MetricPollJob.Execute()`'s catch block, which logs the error and calls `RecordFailure`. This means
a bug in combined metric computation would incorrectly attribute the failure to device
unreachability rather than computation error. **Mitigation:** The combined metric computation block
should be wrapped in its own try/catch that logs at Error and increments a distinct counter (or at
minimum logs a distinguishable message) before re-throwing or swallowing. This is a code-level
implementation concern, not a feature boundary concern, but it must be addressed in the
implementation plan.

---

## MVP Recommendation

**Must build (7 — all table stakes):**

1. **TS-01** `PollOptions.CombinedMetricName` and `Action` fields
2. **TS-05** Configuration validation — Action must be a known value; fields must be paired
3. **TS-07** Warning when `CombinedMetricName` conflicts with OID map metric name
4. **TS-02** `sum`, `diff`, `mean` computation with all edge cases specified above
5. **TS-06** No combined metric emitted on empty/all-error response
6. **TS-04** Combined metric dispatched directly via `ISnmpMetricFactory`, after the dispatch loop
7. **TS-03** Combined metric recorded as `snmp_gauge` with `oid="combined"`, `snmp_type="combined"`, `source="poll"`

**Should build (2 differentiators — low cost, high dashboard value):**

1. **D-01** `snmp.combined.computed{device_name, action}` counter
2. **D-03** Warning at load time when `diff` group has more than 2 metrics

**Evaluate before committing (1 differentiator):**

- **D-02** `PollDurationMs` on combined metric — trivial to add, but adds one histogram data point
  per combined metric per poll. Adds cardinality proportional to number of combined groups.
  Worthwhile if the operations dashboard phases (Phase 18) already show per-metric duration.

**Explicitly do NOT build (6 anti-features):**

- AF-01: Delta-over-time (rate) computation — use Prometheus `rate()` instead
- AF-02: Cross-device aggregation — use PromQL `sum()` instead
- AF-03: `snmp.combined.skipped` counter — absence of `snmp.combined.computed` is the signal
- AF-04: MediatR notification for combined metric
- AF-05: Combined metric over string OID values
- AF-06: Tenant vector fan-out for combined metrics

---

## Sources

- Codebase: `src/SnmpCollector/Jobs/MetricPollJob.cs` — full dispatch flow, variable collection,
  response loop, exception handling structure (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — `ExtractedValue: double`,
  `Source: SnmpSource`, `PollDurationMs: double?` (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — Counter32/Counter64
  recorded as gauge (not counter), `RecordGauge` call signature (HIGH confidence)
- Codebase: `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — `snmp_gauge` as `Gauge<double>`,
  `LeaderMeterName` meter, `ConcurrentDictionary` instrument cache (HIGH confidence)
- Codebase: `src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs` — `RecordGauge` and
  `RecordGaugeDuration` signatures including all 6 label parameters (HIGH confidence)
- Codebase: `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — `MeterName` (pipeline) vs
  `LeaderMeterName` (business metrics) split (HIGH confidence)
- Codebase: `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — all 11 existing counters,
  `SnmpCollector` meter (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/PollOptions.cs` — current shape: MetricNames[],
  IntervalSeconds, TimeoutMultiplier (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/OidMapService.cs` — `ContainsMetricName()` on
  FrozenSet<string>, `ResolveToOid()` reverse map (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` — soft-degradation
  pattern: log Warning + increment rejected + return default, continue for other entries (HIGH confidence)

---

*Feature research for: Combined / Synthetic Metrics on Poll Groups*
*Researched: 2026-03-15*
