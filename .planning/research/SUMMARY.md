# Research Summary — v1.8 Combined Metrics

**Project:** Simetra119 SNMP Collector
**Domain:** SNMP monitoring agent — poll-group-level aggregate computation
**Researched:** 2026-03-15
**Confidence:** HIGH

---

## Executive Summary

v1.8 adds combined (synthetic) metrics to poll groups: given a set of OIDs already fetched in a
single SNMP GET, the collector computes an aggregate (sum, diff, or mean) and records it as a
named gauge alongside the individual per-OID metrics. The feature is additive and backward-
compatible — poll groups without `CombinedMetricName`/`Action` are completely unaffected.

The research is unambiguous on implementation approach. All required types and infrastructure
already exist. The feature requires changes to five existing files, three new files (config
model, runtime record, aggregation enum), and zero new NuGet packages. The critical path is:
add config/runtime models, resolve combined definitions in DeviceWatcherService, add a bypass
guard to OidResolutionBehavior, then add `DispatchCombinedMetricsAsync` to MetricPollJob. The
existing MediatR pipeline handles synthetic messages completely without a new behavior or
notification type.

The primary risk is the `OidResolutionBehavior` bypass. Without it, every synthetic message
has its pre-set `MetricName` silently overwritten with `OidMapService.Unknown`. This risk is
completely understood and the fix is a three-line guard. Secondary risks are negative Difference
results overflowing `Gauge32` (unsigned) — mitigated by selecting `Integer32` for Difference
aggregations — and exceptions in the aggregation block being misattributed as device
unreachability — mitigated by a dedicated try/catch around the combined metrics block.

---

## Key Findings

### Recommended Stack

**Zero new NuGet packages.** Every type needed is already present: `SnmpType`/`ISnmpData`
hierarchy from SharpSnmpLib 12.5.7, `ISender.Send` from MediatR 12.5.0, `Gauge<double>.Record`
from OpenTelemetry SDK 1.15.0, LINQ aggregation from the .NET 9 BCL.

**Core technologies (unchanged):**

- `Lextm.SharpSnmpLib 12.5.7` — `Gauge32`/`Integer32` wrapping for computed values; `SnmpType`
  enum for TypeCode selection
- `MediatR 12.5.0` — `ISender.Send` dispatches synthetic `SnmpOidReceived` through the existing
  pipeline; no new behaviors or handlers needed
- `OpenTelemetry SDK 1.15.0` — `ISnmpMetricFactory.RecordGauge` records the result under the
  existing `snmp_gauge` instrument on the `LeaderMeterName` meter; empty-string OTel tag values
  are safe (null is the problematic case, fixed in SDK 1.4.0)
- `.NET 9 BCL` — `[DisallowConcurrentExecution]` on `MetricPollJob` provides thread-safety for
  the aggregation at no cost; LINQ `Sum()`/`Average()` on a local `List<double>` is sufficient

**New types required (no new packages):**

- `SnmpSource.Synthetic` — one new enum member; distinguishes intentional empty/sentinel OID
  from a malformed message; enables consistent guard predicate across ValidationBehavior and
  OidResolutionBehavior
- `AggregationKind` enum — replaces magic strings (`"sum"`, `"diff"`, `"mean"`) at runtime
- `CombinedMetricOptions` config model and `CombinedMetricDefinition` runtime record — new
  files; small, no dependencies

**OTel label strategy:** use `oid = "combined"` (not `oid = ""`) as the sentinel label value
on synthetic metrics. FEATURES.md specifies this explicitly; it is Grafana-filterable and
avoids any ambiguity about the empty-string OTel tag behavior.

See `.planning/research/STACK.md` for the full pipeline behavior analysis and type change
summary.

### Expected Features

**Must build — Table Stakes (7 features):**

- **TS-01** `PollOptions.CombinedMetricName` (string?) and `Action` (string?) — two optional
  nullable fields; both present or both absent; backward-compatible; low complexity
- **TS-02** `sum`, `diff`, `mean` computation — sum of all numeric varbinds, diff = index[0]
  minus index[1] (in `MetricNames[]` order), mean = sum/n with `double` arithmetic; all edge
  cases specified (zero response, negative diff, partial response, overflow documentation)
- **TS-03** Prometheus label set: `oid="combined"`, `snmp_type="combined"`, `source="poll"`,
  `metric_name=CombinedMetricName`; recorded via existing `snmp_gauge` Gauge instrument (not
  Counter, not Histogram)
- **TS-04** Combined metric dispatched after the varbind loop via direct
  `ISnmpMetricFactory.RecordGauge` call; no MediatR dispatch for the combined metric itself
  (keeps pipeline counters clean)
- **TS-05** Config validation: unknown `Action` value causes poll group to be skipped at load
  time with a structured Error log; mismatched presence (one field without the other) is also
  a load-time error; case-insensitive matching for `Action` value
- **TS-06** No combined metric emitted when SNMP response has zero numeric values; absence of
  a data point is more honest than a zero that looks like real data
- **TS-07** Warning (non-fatal) at load time when `CombinedMetricName` conflicts with an OID
  map metric name; poll group is not skipped — naming conflict is a warning, not a fatal error

**Should build — Differentiators (2 low-cost):**

- **D-01** `snmp.combined.computed{device_name, action}` counter — enables alerting when a
  device stops producing combined metrics; distinguishes "computed 0" from "skipped entirely"
- **D-03** Warning at config load time when `diff` group has more than 2 metric names (almost
  always a configuration mistake)

**Evaluate before committing:**

- **D-02** `PollDurationMs` on combined metric — trivial to add but adds histogram cardinality
  proportional to number of combined groups; worthwhile only if Phase 18 (operations dashboard)
  already shows per-metric poll duration

**Explicitly out of scope (6 anti-features):**

- **AF-01** Delta-over-time (rate) computation — Prometheus `rate()` is the right tool
- **AF-02** Cross-device aggregation — PromQL `sum()` is the right tool
- **AF-03** `snmp.combined.skipped` counter — absence of D-01 is sufficient signal
- **AF-04** MediatR notification for combined metric — direct `RecordGauge` is correct
- **AF-05** Combined metric over string OID values — no numeric semantics
- **AF-06** Tenant vector fan-out for combined metrics — combined metrics are not
  tenant-declared; tenants observe them via Prometheus queries if needed

See `.planning/research/FEATURES.md` for the full edge-case tables and dependency graph.

### Architecture Approach

The implementation decomposes cleanly into four sequential phases. Each phase is independently
unit-testable and leaves the system in a valid state. No phase crosses more than one subsystem
boundary at a time.

**New files (3):**

| File | Kind | Purpose |
|------|------|---------|
| `Configuration/CombinedMetricOptions.cs` | Config model | `MetricName`, `Aggregation` (string), `SourceMetricNames` list |
| `Pipeline/CombinedMetricDefinition.cs` | Runtime record | Resolved definition: `MetricName`, `AggregationKind`, `SourceOids` |
| `Pipeline/AggregationKind.cs` | Enum | `Sum`, `Difference`, `Mean` (and `Ratio`, `Max`, `Min` if needed) |

**Modified files (5):**

| File | Change | Size |
|------|--------|------|
| `Configuration/PollOptions.cs` | Add `List<CombinedMetricOptions> CombinedMetrics = []` | 3 lines |
| `Pipeline/MetricPollInfo.cs` | Add `IReadOnlyList<CombinedMetricDefinition> CombinedMetrics` parameter with default | 2 lines |
| `Services/DeviceWatcherService.cs` | Extend `BuildPollGroups`: resolve `SourceMetricNames` to OIDs, validate, build definitions | ~30 lines in existing private static method |
| `Jobs/MetricPollJob.cs` | Add `DispatchCombinedMetricsAsync`; call after `DispatchResponseAsync` | ~40 lines new method + 1 call site |
| `Pipeline/Behaviors/OidResolutionBehavior.cs` | Add bypass guard (see Gap section for Option A vs B decision) | 3 lines |

**Unchanged files (all pipeline behaviors except OidResolutionBehavior):**
`ValidationBehavior`, `ValueExtractionBehavior`, `TenantVectorFanOutBehavior`,
`LoggingBehavior`, `ExceptionBehavior`, `OtelMetricHandler`, `SnmpOidReceived`,
`DynamicPollScheduler`, `TenantVectorRegistry`.

**Data flow:**

```
devices.json CombinedMetricOptions
    -> BuildPollGroups (validates, resolves SourceMetricNames to OIDs)
    -> MetricPollInfo.CombinedMetrics (resolved CombinedMetricDefinition list)
    -> MetricPollJob.Execute (Quartz fires on interval)
        -> DispatchResponseAsync (individual per-varbind loop — unchanged)
        -> DispatchCombinedMetricsAsync (new method)
            -> build { oid -> double } index from response varbinds
            -> for each CombinedMetricDefinition: compute aggregate
            -> new SnmpOidReceived { Oid="0.0", Source=Synthetic, MetricName=pre-set }
            -> ISender.Send -> existing pipeline:
                ValidationBehavior:      "0.0" passes regex; Source guard optional
                OidResolutionBehavior:   bypass guard fires; MetricName preserved
                ValueExtractionBehavior: extracts from Gauge32/Integer32 wrapper
                TenantVectorFanOutBehavior: routes if tenant slot exists
                OtelMetricHandler:       RecordGauge under snmp_gauge
```

**TypeCode selection for computed values:**
- `Sum`, `Mean`, `Max`, `Min` → `Gauge32` (unsigned; safe for non-negative counter aggregates)
- `Difference` → `Integer32` (signed; result can be negative — Gauge32 would overflow)
- `Ratio` → deferred pending scale factor decision (see Gaps)

See `.planning/research/ARCHITECTURE.md` for full per-behavior analysis and build-order
rationale.

### Critical Pitfalls

The following pitfalls are specific to v1.8 and are derived from STACK.md, FEATURES.md, and
ARCHITECTURE.md (the existing PITFALLS.md covers v1.6/v1.7 and does not address this milestone).

1. **OidResolutionBehavior silently overwrites MetricName (CRITICAL)** — `msg.MetricName =
   _oidMapService.Resolve(msg.Oid)` has no guard; `Resolve("0.0")` returns
   `OidMapService.Unknown`, overwriting the pre-set name. All synthetic metrics are silently
   recorded under `"unknown"`. Prevention: add bypass guard in Phase 3, before any synthetic
   message is ever dispatched.

2. **Negative Difference overflows Gauge32 (CRITICAL)** — `Gauge32` is `uint` (0..2³²−1).
   A negative arithmetic difference silently wraps to a large positive number. Prevention:
   select `Integer32` (signed) as the `TypeCode` and `ISnmpData` wrapper whenever
   `AggregationKind == Difference`.

3. **Exception in aggregation block misattributed as device unreachability (CRITICAL)** — if
   `DispatchCombinedMetricsAsync` throws an unhandled exception, it propagates to
   `MetricPollJob.Execute()`'s catch block, which records a poll failure and increments
   `snmp_poll_unreachable_total`. Prevention: wrap the combined metrics block in its own
   try/catch with a structured Error log naming combined metric computation as the source.

4. **Empty-response guard omission emits misleading zero (TS-06 omission)** — if the zero-
   response guard is not implemented, a device that returns all-error SNMP sentinels would
   produce a combined metric of 0, which looks like valid data in Grafana. Prevention: TS-06
   is table-stakes and must ship in Phase 4.

5. **Ratio denominator zero produces silent +Inf (if Ratio is implemented)** — IEEE 754
   division by zero does not throw; it returns `double.PositiveInfinity`. OpenTelemetry
   records it; Prometheus dashboards show confusing values. Prevention: explicit zero-denominator
   check before dispatch; skip with a Debug log. Recommendation: exclude Ratio from v1.8
   unless a scale factor strategy is resolved (see Gaps).

---

## Implications for Roadmap

Based on the combined research, the natural phase structure has four phases ordered by
dependency and blast-radius isolation.

### Phase 1: Config and Runtime Models

**Rationale:** Every downstream component depends on the shape of `CombinedMetricOptions` and
`CombinedMetricDefinition`. This phase has no behavior changes — it creates new files and
extends two existing models with backward-compatible defaults. Existing tests are unaffected.

**Delivers:** `CombinedMetricOptions.cs` (new config model), `AggregationKind.cs` (new enum),
`CombinedMetricDefinition.cs` (new runtime record), `PollOptions.CombinedMetrics = []`
(additive field), `MetricPollInfo.CombinedMetrics` (new optional parameter with default).

**Addresses:** TS-01 (prerequisite for all table stakes).

**Avoids:** Type shape instability — all downstream phases must be written against stable types.

**Research flag:** Standard patterns. No additional research needed.

---

### Phase 2: DeviceWatcherService Resolution and Validation

**Rationale:** `BuildPollGroups` is the established resolution point. It already resolves
`MetricNames` to OIDs via `IOidMapService.ResolveToOid`. Extending it to resolve
`CombinedMetricOptions.SourceMetricNames` follows the same pattern. Load-time validation
(TS-05, TS-07) belongs here because `IOidMapService` is available in this method and is not
injectable into `DevicesOptionsValidator`.

**Delivers:** Populated `MetricPollInfo.CombinedMetrics` at runtime; structured Error log for
unknown `Action` values; structured Error log for mismatched field presence (one without the
other); Warning log for `CombinedMetricName` conflicting with OID map metric names; Warning
log when `diff` group has more than 2 metric names (D-03).

**Addresses:** TS-05, TS-07, D-03.

**Avoids:** Resolution at poll time (would require injecting `IOidMapService` into
`MetricPollJob` and would execute per-poll instead of once at load).

**Research flag:** Standard patterns — mirrors existing MetricNames resolution logic. No deeper
research needed.

---

### Phase 3: OidResolutionBehavior Bypass Guard

**Rationale:** This is a three-line change with critical consequence if omitted. Implementing
it as a dedicated phase (or at minimum a dedicated commit) before `DispatchCombinedMetricsAsync`
exists ensures the pipeline is ready to handle synthetic messages correctly before any are ever
dispatched. The unit test for this phase can be written without a full poll job execution.

**Delivers:** `OidResolutionBehavior` guard that bypasses OID lookup when the bypass condition
is met (see Gaps for Option A vs B decision); unit test confirming `_oidMapService.Resolve` is
not called and `MetricName` is preserved.

**Addresses:** Critical pitfall #1 (MetricName overwrite).

**Avoids:** Silent `"unknown"` metric name on all combined metrics after Phase 4 ships.

**Research flag:** Standard patterns. Guard is 3 lines. Decision between Option A and B is a
plan-time decision (see Gaps), not a research gap.

---

### Phase 4: MetricPollJob Dispatch

**Rationale:** All models, resolution, and pipeline guards must be in place before
`DispatchCombinedMetricsAsync` is added. This is the terminal implementation phase. The method
is approximately 40 lines and is independently unit-testable by mocking `ISender` and asserting
the synthetic `SnmpOidReceived` properties.

**Delivers:** `DispatchCombinedMetricsAsync` called after `DispatchResponseAsync`; correct
TypeCode selection (Integer32 for Difference, Gauge32 for others); zero-response guard (TS-06);
missing-source-OID per-cycle skip with Warning log; dedicated try/catch to prevent
misattribution as device unreachability; `snmp.combined.computed{device_name, action}` counter
increment (D-01); `source="synthetic"` label in Prometheus (via `SnmpSource.Synthetic` on the
dispatched message).

**Addresses:** TS-02, TS-03, TS-04, TS-06, D-01, and critical pitfalls #2, #3, #4, #5.

**Research flag:** Standard patterns. One plan-time decision needed on Ratio (see Gaps).

---

### Phase Ordering Rationale

- Models must exist before any code references their types (Phase 1 first, unblocks all others).
- Resolution and validation populate `MetricPollInfo.CombinedMetrics`; the job reads it (Phase 2
  before Phase 4).
- The pipeline bypass guard must exist before any synthetic message is dispatched — Phase 3
  before Phase 4 ensures no window where broken behavior is observable.
- Phase 4 is last: all prerequisites in place; observable behavior change is isolated to a
  single phase, making rollback straightforward.

After Phases 1 and 2 land, existing behavior is completely unchanged. After Phase 3, the
pipeline can accept synthetic messages but none are produced yet. Phase 4 is the only phase
that changes observable metric output.

### Research Flags

All four phases use standard, well-documented patterns. No `/gsd:research-phase` runs are
needed. The open questions below are plan-time decisions, not research gaps.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All findings from direct source file reads; zero new NuGet packages confirmed; OTel empty-string safety MEDIUM (spec-confirmed, SDK source not read) but moot since `oid="combined"` not `""` is used |
| Features | HIGH | All findings from direct codebase analysis of MetricPollJob, OtelMetricHandler, SnmpMetricFactory, PipelineMetricService, PollOptions, OidMapService; no external sources needed |
| Architecture | HIGH | All behavior interactions verified by direct code read; build order derived from explicit dependency analysis; `MetricPollInfo` positional record optional-parameter extension verified as source-compatible |
| Pitfalls (v1.8) | HIGH | All pitfalls derived from direct code reads of ValidationBehavior, OidResolutionBehavior, ValueExtractionBehavior, MetricPollJob catch block; Gauge32 overflow is a well-known C# type constraint |

**Overall confidence: HIGH**

### Gaps to Address

1. **OidResolutionBehavior bypass: Option A vs Option B** — STACK.md recommends guarding on
   `msg.Source == SnmpSource.Synthetic` (Option B); ARCHITECTURE.md recommends guarding on
   `msg.MetricName` already being non-null/non-Unknown (Option A). Both are correct. This
   synthesis recommends **Option B** (`SnmpSource.Synthetic`) because: (a) it is consistent
   with the `ValidationBehavior` guard that will also be needed if `Oid = ""` is used, (b) it
   produces a useful `source="synthetic"` Prometheus label via `SnmpSource.Synthetic.ToString()`,
   and (c) it is unambiguous — a future message that pre-sets MetricName for a legitimate
   non-synthetic reason would not accidentally bypass OID resolution. Record this as a named
   decision in the Phase 3 plan.

2. **Sentinel OID value: `""` vs `"0.0"` vs `"combined"`** — Three options appear across the
   research files. If Option B is chosen for the bypass, `ValidationBehavior` also needs a
   Source guard, which allows `Oid = ""` to pass. If ValidationBehavior is NOT guarded,
   `Oid = "0.0"` (ARCHITECTURE.md) passes the existing regex without any behavior change.
   FEATURES.md specifies the Prometheus `oid` label as `"combined"` — this is the label value
   seen in Prometheus, set by either pre-populating `Oid="combined"` (requires ValidationBehavior
   guard) or by `OtelMetricHandler` using a separate field. The simplest resolution: use
   `Oid = "0.0"` as the wire sentinel (passes ValidationBehavior without changes) and let
   `OtelMetricHandler` emit `oid="0.0"` in Prometheus. If `oid="combined"` is important for
   dashboard clarity, add `Source == Synthetic` guards to both ValidationBehavior and
   OidResolutionBehavior and use `Oid = "combined"`. Record as a named decision in Phase 3.

3. **Ratio aggregation: include in v1.8 or defer?** — ARCHITECTURE.md flags that `Ratio(a, b)`
   produces a value in `[0.0, 1.0]` for typical counter ratios, which rounds to 0 in `Gauge32`.
   A configurable scale factor is needed. Ratio is listed in `AggregationKind` but is absent
   from FEATURES.md's table-stakes list. Recommendation: **exclude Ratio from v1.8** unless the
   scale factor design is resolved in Phase 4 planning. `AggregationKind.Ratio` can exist in the
   enum but `BuildPollGroups` should treat it as an invalid `Action` value (TS-05 error) until
   the scaling design is settled. Record as a named decision in the Phase 2 plan.

4. **PITFALLS.md is for v1.6/v1.7** — this document covers OID map duplicate validation,
   human-name device config, and command map infrastructure. Those pitfalls are not applicable
   to v1.8. If a dedicated v1.8 pitfalls research pass were run, it might surface interaction
   issues between combined metric definitions and OID map hot-reload (a rename after combined
   metrics are configured would silently break source OID lookup in `DispatchCombinedMetricsAsync`).
   This is low risk given the additive nature of the change but should be noted in Phase 4
   planning.

---

## Sources

### Primary — HIGH confidence (direct codebase reads)

- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — unconditional MetricName overwrite confirmed; no existing guard
- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` — OID regex `^\d+(\.\d+){1,}$` confirmed; rejection path confirmed
- `src/SnmpCollector/Pipeline/Behaviors/ValueExtractionBehavior.cs` — TypeCode switch, ExtractedValue assignment; Integer32/Gauge32 cases confirmed
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — MetricName-based routing; no SnmpSource check confirmed
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — RecordGauge path for Integer32/Gauge32; source label format confirmed
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — `[DisallowConcurrentExecution]` confirmed; `DispatchResponseAsync` structure; catch block behavior
- `src/SnmpCollector/Configuration/PollOptions.cs` — current shape (MetricNames[], IntervalSeconds, TimeoutMultiplier) confirmed
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — all required fields confirmed; `MetricName` is mutable (`set`)
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — only `Poll` and `Trap` exist; `Synthetic` absent confirmed
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — `RecordGauge` signature; `LeaderMeterName` meter; `oid` tag composition
- `src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs` — `RecordGauge` and `RecordGaugeDuration` signatures
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — all 11 existing counters; none affected by combined metrics path
- `src/SnmpCollector/SnmpCollector.csproj` — OpenTelemetry SDK 1.15.0 confirmed

### Secondary — MEDIUM confidence

- OTel specification (via WebSearch in STACK.md): empty string is a valid, meaningful attribute value that MUST be stored and passed on
- opentelemetry-dotnet issues #3451 and #3846 (via WebSearch in STACK.md): null tag value bug fixed in SDK 1.4.0; empty string is not the problematic case

---

*Research completed: 2026-03-15*
*Ready for roadmap: yes*
