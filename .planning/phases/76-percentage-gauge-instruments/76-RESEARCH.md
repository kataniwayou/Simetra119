# Phase 76: Percentage Gauge Instruments - Research

**Researched:** 2026-03-23
**Domain:** .NET OTel Metrics — Gauge instruments, TenantMetricService, SnapshotJob
**Confidence:** HIGH — all findings sourced from direct source-code inspection of this repo

---

## Summary

Phase 76 replaces 6 `Counter<long>` instruments in `TenantMetricService` with 6 `Gauge<double>` instruments
that emit percentage values (0.0–100.0). The counter `Increment*` call sites in `SnapshotJob.EvaluateTenant`
are replaced with single-call `Record*Percent` calls that pass pre-computed numerator/denominator values.
`tenant.state` is renamed to `tenant.evaluation.state`. The counting helper methods (7 private static methods
on `SnapshotJob`) are removed and replaced with a single percentage-computation helper used at the call site.

The `Gauge<double>` API already exists in this codebase. `SnmpMetricFactory` calls
`_meter.CreateGauge<double>(name)` and `gauge.Record(value, tagList)` — exactly the pattern to follow.
No new dependencies are required.

**Primary recommendation:** Follow the SnmpMetricFactory pattern exactly — `CreateGauge<double>` in
constructor, `gauge.Record(percentage, tagList)` per call. Compute percentage in SnapshotJob before calling
the service method.

---

## Standard Stack

### Core (already present — no new packages needed)

| Library | Source | Purpose | Notes |
|---------|--------|---------|-------|
| `System.Diagnostics.Metrics.Gauge<T>` | .NET BCL | Push gauge instrument | Used by SnmpMetricFactory today |
| `System.Diagnostics.Metrics.Meter` | .NET BCL | Creates instruments | `_meter.CreateGauge<double>(name)` |
| `System.Diagnostics.Metrics.TagList` | .NET BCL | Struct for tags | Used throughout TenantMetricService |

**Installation:** None — already referenced via `System.Diagnostics.Metrics`.

---

## Architecture Patterns

### Gauge<double> API — verified in SnmpMetricFactory.cs

```csharp
// Source: src/SnmpCollector/Telemetry/SnmpMetricFactory.cs line 106
private Gauge<double> GetOrCreateGauge(string name)
    => (Gauge<double>)_instruments.GetOrAdd(name, n => _meter.CreateGauge<double>(n));

// Recording (line 39):
gauge.Record(value, new TagList
{
    { "tenant_id", tenantId },
    { "priority", priority }
});
```

The existing `_tenantState` gauge in TenantMetricService already follows this pattern:

```csharp
// Source: src/SnmpCollector/Telemetry/TenantMetricService.cs lines 52, 85
_tenantState = _meter.CreateGauge<double>("tenant.state");
_tenantState.Record((double)(int)state, new TagList { { "tenant_id", tenantId }, { "priority", priority } });
```

### Current counter-per-event pattern (to be removed)

SnapshotJob currently calls Increment methods in loops, e.g.:

```csharp
// Source: SnapshotJob.cs lines 163-167
var staleCount = CountStaleHolders(tenant.Holders);
for (var i = 0; i < staleCount; i++)
    _tenantMetrics.IncrementTier1Stale(tenant.Id, tenant.Priority);
```

This fires N OTel measurements per evaluation cycle per tenant. The replacement fires exactly one `Record`
call per gauge per evaluation cycle — a clean reduction in measurement volume.

### Replacement: single Record per gauge at exit

The new pattern fires once per gauge at the RecordAndReturn exit point:

```csharp
// Conceptual replacement at RecordAndReturn site
var staleCount   = CountStaleHolders(tenant.Holders);
var totalMetrics = CountEligibleMetrics(tenant.Holders);  // denominator
_tenantMetrics.RecordStalePercent(tenant.Id, tenant.Priority,
    Percent(staleCount, totalMetrics));
```

### Recommended Project Structure (unchanged)

```
src/SnmpCollector/Telemetry/
├── TenantMetricService.cs       # 6 Gauge<double> fields replacing 6 Counter<long>
├── ITenantMetricService.cs      # 6 Record*Percent methods replacing 6 Increment* methods
└── TelemetryConstants.cs        # No changes needed

src/SnmpCollector/Jobs/
└── SnapshotJob.cs               # Replace all 7 private static counting helpers + loop calls
                                 # Add 1 private static Percent(int numerator, int denominator) helper
```

### Anti-Patterns to Avoid

- **Keeping the loop-based Increment calls alongside new Record calls:** Remove all counter loops.
  The 6 counter fields must be deleted from `TenantMetricService`, not left dormant.
- **Computing percentage inside TenantMetricService:** The context says percentage calculation
  happens in the caller (SnapshotJob), not in the service. Keep the service method thin.
- **Recording gauge only on certain code paths:** `tenant.state` is currently recorded in
  `RecordAndReturn` on every path. The 6 percentage gauges should also be recorded there
  (single exit point), not at each tier branch.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Push gauge | Custom wrapper | `Gauge<double>` from `System.Diagnostics.Metrics` | Already in BCL and used by this project |
| Tag struct | `Dictionary<string,object>` | `TagList` struct | Stack-allocated, used throughout |

**Key insight:** `Gauge<double>` is a push gauge — `Record(value, tags)` is called imperatively when
the value is known. This is the correct semantic for per-evaluation-cycle percentage values
(as opposed to ObservableGauge which uses a pull callback). The project already uses push gauges in
SnmpMetricFactory and for `_tenantState`.

---

## Common Pitfalls

### Pitfall 1: Denominator categories for stale vs metric-role gauges

**What goes wrong:** Using `tenant.Holders.Count` as the denominator for stale vs. using it for
role-specific denominators (resolved/evaluate) — these are different populations.

**Root cause:**
- Stale denominator = `CountEligibleMetrics(holders)` — holders where Source != Trap, != Command,
  and IntervalSeconds != 0 (mirrors the `HasStaleness` / `CountStaleHolders` exclusion logic).
- Resolved denominator = count of holders where `Role == "Resolved"`.
- Evaluate denominator = count of holders where `Role == "Evaluate"`.
- Command denominators (dispatched/failed/suppressed) = `tenant.Commands.Count`.

**How to avoid:** Keep the denominator derivation aligned with what the existing counting helpers
already filter. The denominator must be computed from config-stable holder/command counts, not from
runtime-filtered counts (e.g. don't exclude empty-series holders from the denominator — the config
count is the stable denominator).

**Warning signs:** A percentage that oscillates between 0% and 100% across cycles with no actual
metric change suggests the denominator varies at runtime.

### Pitfall 2: Resolved direction is violated, not non-violated

**What goes wrong:** `CountResolvedNonViolated` currently counts non-violated holders. The phase
context says the numerator must be VIOLATED resolved holders (consistent with evaluate direction:
higher % = worse). Using the existing `CountResolvedNonViolated` directly would invert the gauge.

**How to avoid:** Add a `CountResolvedViolated` helper (mirror of `CountEvaluateViolated` but for
Role=="Resolved") or inline the violated-count logic. Do NOT reuse `CountResolvedNonViolated`.

**Current code (wrong direction for phase 76):**

```csharp
// Source: SnapshotJob.cs lines 451-483 — counts NON-violated (to be replaced, not reused)
private static int CountResolvedNonViolated(IReadOnlyList<MetricSlotHolder> holders)
```

### Pitfall 3: tenant.state rename — Prometheus label diverges from OTel name

**What goes wrong:** OTel `tenant.state` → Prometheus `tenant_state` (dot-to-underscore). After rename
to `tenant.evaluation.state` → Prometheus `tenant_evaluation_state`. E2E scenarios (107-112), Grafana
dashboard, and TenantMetricService unit tests all reference the old Prometheus name `tenant_state`.

**Scope clarification from REQUIREMENTS.md traceability table:**
- CLN-03 (clean up references in SnapshotJob/CommandWorkerService) → Phase 78
- E2E-01, E2E-02 (update E2E scenarios) → Phase 80
- DSH-01, DSH-02 (update dashboard) → Phase 79
- UTT-01 (SnapshotJobTests) → Phase 77
- **UTT-02 (TenantMetricService unit tests) → Phase 76 (IN SCOPE)**

**How to avoid:** Phase 76 updates `TenantMetricService` + `ITenantMetricService` + the
`TenantMetricServiceTests` only. E2E scenarios, Grafana dashboard, and SnapshotJob counter
reference cleanup are deferred to later phases (78-80). This means the old `tenant_state`
Prometheus name is still referenced in those files at end of phase 76 — that is expected.

### Pitfall 4: MeterListener callback type in tests

**What goes wrong:** Adding `long` measurement callbacks but the new instruments emit `double`.
The current test file tracks `_measurements` (long) and `_doubleMeasurements` (double). All 6
new percentage gauges are `Gauge<double>`, so they land in `_doubleMeasurements`. Existing long
tests for the 6 counters are removed entirely.

**How to avoid:** In the updated `TenantMetricServiceTests`, remove the `_measurements` list (or
keep it only if needed for other instruments) and write all 6 new gauge tests using
`_doubleMeasurements`. The `tenant.evaluation.state` (renamed from `tenant.state`) is also
`Gauge<double>` and stays in `_doubleMeasurements`.

---

## Code Examples

### Creating 6 Gauge<double> fields in TenantMetricService constructor

```csharp
// Pattern: src/SnmpCollector/Telemetry/SnmpMetricFactory.cs and TenantMetricService.cs
_metricStalePercent     = _meter.CreateGauge<double>("tenant.metric.stale.percent");
_metricResolvedPercent  = _meter.CreateGauge<double>("tenant.metric.resolved.percent");
_metricEvaluatePercent  = _meter.CreateGauge<double>("tenant.metric.evaluate.percent");
_commandDispatchedPercent = _meter.CreateGauge<double>("tenant.command.dispatched.percent");
_commandFailedPercent   = _meter.CreateGauge<double>("tenant.command.failed.percent");
_commandSuppressedPercent = _meter.CreateGauge<double>("tenant.command.suppressed.percent");

_tenantEvaluationState  = _meter.CreateGauge<double>("tenant.evaluation.state");  // renamed
```

### New Record methods on ITenantMetricService (Claude's discretion on signatures)

Cleanest signature — pass percentage directly (pre-computed in caller):

```csharp
void RecordMetricStalePercent(string tenantId, int priority, double percent);
void RecordMetricResolvedPercent(string tenantId, int priority, double percent);
void RecordMetricEvaluatePercent(string tenantId, int priority, double percent);
void RecordCommandDispatchedPercent(string tenantId, int priority, double percent);
void RecordCommandFailedPercent(string tenantId, int priority, double percent);
void RecordCommandSuppressedPercent(string tenantId, int priority, double percent);
```

Alternative: pass numerator/denominator and compute in service. Context says compute in caller,
so prefer the first form.

### Percentage helper in SnapshotJob

```csharp
private static double Percent(int numerator, int denominator)
    => denominator == 0 ? 0.0 : 100.0 * numerator / denominator;
```

### Denominator counts needed per gauge

| Gauge | Numerator | Denominator |
|-------|-----------|-------------|
| stale.percent | CountStaleHolders(holders) | CountEligibleHolders(holders) — Source != Trap/Command, IntervalSeconds != 0 |
| resolved.percent | CountResolvedViolated(holders) (NEW — violated direction) | holders.Count(h => h.Role == "Resolved") |
| evaluate.percent | CountEvaluateViolated(holders) (existing) | holders.Count(h => h.Role == "Evaluate") |
| dispatched.percent | dispatchedCount | tenant.Commands.Count |
| failed.percent | failedCount | tenant.Commands.Count |
| suppressed.percent | suppressedCount | tenant.Commands.Count |

### Unit test pattern (MeterListener + double callback)

```csharp
// Source: existing TenantMetricServiceTests.cs pattern for _doubleMeasurements (lines 46-49)
_listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
{
    _doubleMeasurements.Add((instrument.Name, value, tags.ToArray()));
});

// New test body example:
_service.RecordMetricStalePercent("tenant-a", 1, 50.0);
var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.metric.stale.percent");
Assert.Equal(50.0, match.Value);
```

---

## State of the Art

| Old Approach | Current Approach | Impact for Phase 76 |
|--------------|------------------|----------------------|
| Counter per holder per cycle | One gauge per category per cycle | N OTel measurements → 1 per gauge |
| `tenant.state` instrument name | `tenant.evaluation.state` | Prometheus: `tenant_state` → `tenant_evaluation_state` |
| 7 private static counting helpers in SnapshotJob | 1 `Percent()` helper + denominator-count helpers or inline logic | Loop-based counts replaced with direct counts |

**Removed instruments (Prometheus names):**
- `tenant_tier1_stale_total` (Counter)
- `tenant_tier2_resolved_total` (Counter)
- `tenant_tier3_evaluate_total` (Counter)
- `tenant_command_dispatched_total` (Counter)
- `tenant_command_failed_total` (Counter)
- `tenant_command_suppressed_total` (Counter)

**New instruments (Prometheus names):**
- `tenant_metric_stale_percent` (Gauge)
- `tenant_metric_resolved_percent` (Gauge)
- `tenant_metric_evaluate_percent` (Gauge)
- `tenant_command_dispatched_percent` (Gauge)
- `tenant_command_failed_percent` (Gauge)
- `tenant_command_suppressed_percent` (Gauge)

**Renamed instrument:**
- `tenant_state` → `tenant_evaluation_state`

---

## Open Questions

1. **How many counting helpers to keep vs inline**
   - What we know: SnapshotJob currently has 7 static helpers (`AreAllReady`, `HasStaleness`,
     `AreAllResolvedViolated`, `AreAllEvaluateViolated`, `CountStaleHolders`,
     `CountResolvedNonViolated`, `CountEvaluateViolated`).
   - What's unclear: Phase 76 says "remove counting helper methods and replace with percentage
     calculation logic" (CLN-02). Whether CLN-02 means all 7 helpers go or only the count-only
     ones (last 3) is a planning decision.
   - Recommendation: `AreAll*` boolean helpers are still needed for tier gate logic (they drive
     whether to reach tier 4). Only the `Count*` helpers are replaced. `AreAllReady` stays.
     `HasStaleness` stays. `AreAllResolvedViolated` and `AreAllEvaluateViolated` stay (they gate
     tier transitions, independent of metrics).

2. **Where to add CountResolvedViolated**
   - The phase needs a new helper counting violated resolved holders (inverse of current
     `CountResolvedNonViolated`).
   - Recommendation: Add `CountResolvedViolated` alongside `CountEvaluateViolated` in SnapshotJob
     (same pattern, same structure). Remove `CountResolvedNonViolated`.

3. **Eligible-metric denominator for stale**
   - The stale denominator must exclude Trap, Command, and IntervalSeconds==0 holders (same filter
     as the stale detection loop). No `CountEligibleHolders` exists yet.
   - Recommendation: Add a `CountEligibleHolders(holders)` static helper that mirrors the
     exclusion filter in `CountStaleHolders`.

---

## Sources

### Primary (HIGH confidence)

All findings directly from source code inspection.

- `src/SnmpCollector/Telemetry/TenantMetricService.cs` — 8 instruments, constructor pattern,
  existing `Gauge<double>` usage for `_tenantState`
- `src/SnmpCollector/Telemetry/ITenantMetricService.cs` — 8 current method signatures
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — `CreateGauge<double>`, `gauge.Record()`
  pattern, `TagList` usage
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — `TenantMeterName` constant
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — all 7 private static helpers, all 6 Increment loop
  call sites, `RecordAndReturn` exit point
- `src/SnmpCollector/Pipeline/Tenant.cs` — `Holders: IReadOnlyList<MetricSlotHolder>`,
  `Commands: IReadOnlyList<CommandSlotOptions>`
- `tests/SnmpCollector.Tests/Telemetry/TenantMetricServiceTests.cs` — MeterListener pattern,
  `_doubleMeasurements` and `_measurements` lists, 8 existing tests
- `.planning/REQUIREMENTS.md` — phase 76 requirements PGA-01-06, RMD-01, CLN-01-02, UCH-01-02,
  UTT-02; traceability table showing what is and is not in scope for phase 76
- `.planning/phases/76-percentage-gauge-instruments/76-CONTEXT.md` — locked decisions on naming,
  edge cases, resolved direction, and method signature discretion

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — Gauge<double> API directly observed in SnmpMetricFactory
- Architecture: HIGH — Instrument creation and Record patterns directly read from source
- Pitfalls: HIGH — denominator categories and resolved direction from CONTEXT.md + source logic
- Scope boundary: HIGH — traceability table in REQUIREMENTS.md explicitly assigns CLN-03/E2E/DSH
  to later phases

**Research date:** 2026-03-23
**Valid until:** No external dependencies — valid until source changes
