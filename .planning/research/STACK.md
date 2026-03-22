# Stack Research

**Domain:** Tenant-level observability metrics — OTel SDK instrument patterns and Grafana dashboard
**Researched:** 2026-03-22
**Confidence:** HIGH

---

## Context

This is a targeted stack addendum for the tenant vector metrics milestone. The project already runs
OpenTelemetry .NET SDK 1.15 with OTLP gRPC exporter, `System.Diagnostics.Metrics`, Prometheus remote-write,
and Grafana. No new dependencies are needed. All findings below are based on codebase inspection and
verified against official OTel documentation.

---

## Recommended Stack

### Core Technologies

All technologies are already in the project. No additions required.

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| `System.Diagnostics.Metrics` (BCL) | .NET 9 | Counter, Gauge, Histogram instruments | Built into .NET; OTel SDK maps it to OTLP |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.0 | OTLP gRPC export | Already in csproj; supports all instrument types |
| `OpenTelemetry.Extensions.Hosting` | 1.15.0 | `AddMeter`, `AddView`, SDK wiring | Already in csproj |
| Prometheus + Grafana | existing | Metric storage and dashboards | Already deployed |

### No New Dependencies

Confirmed by inspecting `SnmpCollector.csproj`. OTel 1.15 shipped `Gauge<T>` support (added in 1.10
per issue #4805, merged PR #5867). `SnmpMetricFactory` already calls `_meter.CreateGauge<double>()`,
proving `Gauge<T>` works in this project today.

---

## OTel SDK Instrument Patterns

### Counter — per-cycle tenant increments

The 6 tenant counters follow the same pattern as existing counters in `PipelineMetricService`.
Use `Meter.CreateCounter<long>` on the **`SnmpCollector` meter** (not the leader-gated
`SnmpCollector.Leader` meter), so they export from all instances.

```csharp
// In TenantMetricService constructor
_tier1Stale      = _meter.CreateCounter<long>("snmp.tenant.tier1_stale");
_tier2Resolved   = _meter.CreateCounter<long>("snmp.tenant.tier2_resolved");
_tier3Evaluate   = _meter.CreateCounter<long>("snmp.tenant.tier3_evaluate");
_cmdDispatched   = _meter.CreateCounter<long>("snmp.tenant.command_dispatched");
_cmdFailed       = _meter.CreateCounter<long>("snmp.tenant.command_failed");
_cmdSuppressed   = _meter.CreateCounter<long>("snmp.tenant.command_suppressed");

// Call site — labels: tenantId and priority only
_tier1Stale.Add(1, new TagList { { "tenant_id", tenant.Id }, { "priority", tenant.Priority } });
```

`TagList` is a stack-allocated struct; passing inline avoids heap allocation on the hot path.
The existing `IncrementCommandSuppressed(string deviceName)` call in `SnapshotJob` uses the same
pattern — the tenant-scoped counters are additive, not replacing it.

### Gauge — enum state reporting

**Decision: use synchronous `Gauge<T>` (not `ObservableGauge<T>`).**

Rationale:
- `EvaluateTenant()` already computes the state synchronously in `SnapshotJob`. The state is known at
  evaluation time, so push-model `Gauge<T>.Record()` is the correct fit.
- `ObservableGauge<T>` requires registering a callback that the SDK polls on the export interval.
  That pattern requires storing state externally (e.g., `ConcurrentDictionary<tenantId, int>`) so the
  callback can read it. This is more indirection than needed when the value is already computed.
- `Gauge<T>` was added in OTel .NET 1.10 (PR #5867). The project is on 1.15. `SnmpMetricFactory`
  already uses `CreateGauge<double>()` — no compatibility risk.

Enum mapping (integer values stored in Prometheus, mapped in Grafana):

| State | Value |
|-------|-------|
| NotReady | 0 |
| Healthy | 1 |
| Resolved | 2 |
| Unresolved | 3 |

```csharp
// Instrument creation (on SnmpCollector meter — all-instance export)
_tenantState = _meter.CreateGauge<int>("snmp.tenant.state",
    description: "Tenant evaluation state: 0=NotReady, 1=Healthy, 2=Resolved, 3=Unresolved");

// Call site — record after EvaluateTenant() returns
var stateValue = result switch
{
    TierResult.Healthy    => 1,
    TierResult.Resolved   => 2,
    TierResult.Unresolved => 3,
    _                     => 0   // NotReady (pre-tier skip)
};
_tenantState.Record(stateValue,
    new TagList { { "tenant_id", tenant.Id }, { "priority", tenant.Priority } });
```

The gauge reports the **last-written value** until overwritten on the next cycle. This is correct
behavior for a state enum: Prometheus will show the most recent evaluation result.

### Histogram — per-tenant evaluation duration

Use `Meter.CreateHistogram<double>` for `snmp.tenant.gauge_duration_milliseconds`. This mirrors the
existing `_snapshotCycleDuration` histogram but scoped to individual tenant evaluation time.

**Bucket boundaries.** Default OTel SDK buckets are
`[0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000]`. These are ms-range
boundaries, which is exactly what tenant evaluation duration needs. The existing
`snmp.snapshot.cycle_duration_ms` histogram uses default buckets and the business dashboard
successfully queries `histogram_quantile(0.99, ...)` against them. Use the same defaults.

If a custom narrower range is needed later (e.g., to reduce bucket count for cardinality), configure
it in the `WithMetrics` builder chain using `AddView`:

```csharp
// Optional: override buckets if default range is too wide for sub-10ms evaluations
metrics.AddView(
    instrumentName: "snmp.tenant.gauge_duration_milliseconds",
    new ExplicitBucketHistogramConfiguration
    {
        Boundaries = new double[] { 1, 2, 5, 10, 25, 50, 100, 250, 500 }
    });
```

Call site:

```csharp
_tenantGaugeDuration = _meter.CreateHistogram<double>(
    "snmp.tenant.gauge_duration_milliseconds",
    unit: "ms",
    description: "Duration of per-tenant evaluation in SnapshotJob");

// In EvaluateTenant — wrap with Stopwatch
var sw = Stopwatch.StartNew();
// ... evaluation logic ...
sw.Stop();
_tenantGaugeDuration.Record(sw.Elapsed.TotalMilliseconds,
    new TagList { { "tenant_id", tenant.Id }, { "priority", tenant.Priority } });
```

---

## MetricRoleGatedExporter Bypass

**The problem.** `MetricRoleGatedExporter` filters out all metrics from `TelemetryConstants.LeaderMeterName`
("SnmpCollector.Leader") on follower instances. Metrics from `TelemetryConstants.MeterName`
("SnmpCollector") pass through on all instances.

**The solution.** Create all new tenant instruments on the `SnmpCollector` meter (not the leader meter).
This is the same meter used by `PipelineMetricService` for pipeline counters. The gate logic in
`MetricRoleGatedExporter.Export()` is:

```csharp
if (!string.Equals(metric.MeterName, _gatedMeterName, StringComparison.Ordinal))
{
    ungated.Add(metric);  // passes through on followers
}
```

Any instrument created via `meterFactory.Create(TelemetryConstants.MeterName)` has
`metric.MeterName == "SnmpCollector"`, which does **not** match `_gatedMeterName` ("SnmpCollector.Leader"),
so it is never filtered. No code changes to `MetricRoleGatedExporter` are required.

**Where to create the instruments.** Two options:

1. **Extend `PipelineMetricService`** — add the 8 new instruments alongside the existing 15. Simple,
   no new class. Downside: the class grows larger (23 instruments total).

2. **New `TenantMetricService` singleton** — inject `IMeterFactory`, call
   `meterFactory.Create(TelemetryConstants.MeterName)` to get the same meter, create instruments there.
   This is a clean separation: pipeline metrics stay in `PipelineMetricService`, tenant vector metrics
   in `TenantMetricService`. Register as `AddSingleton<TenantMetricService>()` alongside the existing
   `AddSingleton<PipelineMetricService>()`.

**Recommended: new `TenantMetricService`** — the scope is cleanly bounded (all instruments tagged with
`tenant_id` and `priority`), and injection into `SnapshotJob` is explicit.

---

## Grafana Dashboard Patterns

### Prometheus metric names (OTel to Prometheus translation)

OTel SDK metric names use `.` as separator. The OTLP to Prometheus pipeline converts them:

| OTel name | Prometheus name |
|-----------|-----------------|
| `snmp.tenant.tier1_stale` | `snmp_tenant_tier1_stale_total` |
| `snmp.tenant.tier2_resolved` | `snmp_tenant_tier2_resolved_total` |
| `snmp.tenant.tier3_evaluate` | `snmp_tenant_tier3_evaluate_total` |
| `snmp.tenant.command_dispatched` | `snmp_tenant_command_dispatched_total` |
| `snmp.tenant.command_failed` | `snmp_tenant_command_failed_total` |
| `snmp.tenant.command_suppressed` | `snmp_tenant_command_suppressed_total` |
| `snmp.tenant.state` | `snmp_tenant_state` |
| `snmp.tenant.gauge_duration_milliseconds` | `snmp_tenant_gauge_duration_milliseconds` (histogram: `_bucket`, `_count`, `_sum`) |

Counters get `_total` suffix. Gauges and histogram base names are unchanged. This matches the
existing pattern: `snmp.event.published` becomes `snmp_event_published_total`,
`snmp_gauge_duration` histogram becomes `snmp_gauge_duration_milliseconds_bucket`.

### Operations dashboard — tenant table panel

Model this on the existing "Gauge Metrics" table in `simetra-business.json`. That panel uses:
- Query A (`instant`, `format: "table"`): current values via `label_replace` + `label_join`
- Query B (`instant`, `format: "table"`): trend via `delta(...[30s])`
- Query C (`instant`, `format: "table"`): P99 via `histogram_quantile(0.99, sum by (le, ...) (rate(..._bucket[...])))`
- Transformation: `merge` then `organize` to set column order

**Tenant table query A — current state with text mapping:**

```promql
snmp_tenant_state{k8s_pod_name=~"$pod"}
```

This is `instant` format. Grafana receives one row per `{tenant_id, priority}` label set.
Apply value mappings in fieldConfig overrides on "Value #A":

```json
"mappings": [
  { "options": { "0": { "text": "NotReady",   "color": "text"     } }, "type": "value" },
  { "options": { "1": { "text": "Healthy",    "color": "green"    } }, "type": "value" },
  { "options": { "2": { "text": "Resolved",   "color": "blue"     } }, "type": "value" },
  { "options": { "3": { "text": "Unresolved", "color": "dark-red" } }, "type": "value" }
]
```

Set `cellOptions.type = "color-background"` with `mode: "basic"` to color the cell background —
matching the operations dashboard "Role" column (Pod Identity panel, "Value #B" override).

**Tenant table query B — per-cycle counter rates:**

```promql
sum by (tenant_id, priority) (
  rate(snmp_tenant_tier3_evaluate_total{k8s_pod_name=~"$pod"}[$__rate_interval])
)
```

Use `rate()` for the counters to show events-per-second. `instant: true`. One column per counter
metric is added as a separate target (refId B, C, D, ...) and merged.

**Tenant table query C — P99 evaluation duration:**

```promql
histogram_quantile(0.99,
  sum by (le, tenant_id, priority) (
    rate(snmp_tenant_gauge_duration_milliseconds_bucket{k8s_pod_name=~"$pod"}[$__rate_interval])
  )
)
```

This matches the existing business dashboard P99 query pattern for `snmp_gauge_duration_milliseconds`.

### Column organization

Suggested visible columns for the tenant table:

| Column | Source | Display Name |
|--------|--------|--------------|
| `tenant_id` | label from any target | Tenant |
| `priority` | label from any target | Priority |
| `Value #A` | `snmp_tenant_state` | State |
| `Value #B` | rate of `tier3_evaluate` | Evaluate/s |
| `Value #C` | rate of `command_dispatched` | Dispatch/s |
| `Value #D` | rate of `command_suppressed` | Suppressed/s |
| `Value #E` | P99 histogram | P99 (ms) |

Standard boilerplate columns (`Time`, `job`, `instance`, `service_name`, SDK telemetry labels)
are hidden via `custom.hidden: true` overrides, matching the existing dashboard pattern.

---

## Alternatives Considered

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| `Gauge<T>.Record()` (push) | `ObservableGauge<T>` with callback | Requires external state store; EvaluateTenant already computes state synchronously — callback adds indirection without benefit |
| Instruments on `SnmpCollector` meter | Instruments on `SnmpCollector.Leader` meter | Leader meter is filtered on followers; tenant metrics must export from all instances for per-pod visibility |
| New `TenantMetricService` | Extend `PipelineMetricService` | Clean separation of concerns; PipelineMetricService already has 15 instruments; new class keeps tenant scope explicit |
| Default OTel histogram buckets | Custom bucket boundaries | Existing `snmp_gauge_duration` uses defaults and P99 queries work; change only if cardinality becomes an issue |
| `int` type for gauge values | `double` type | State is an enum (0-3); `int` signals intent; `Gauge<int>` is valid with `System.Diagnostics.Metrics` |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `UpDownCounter<T>` for state | UpDownCounter is for values that go up and down cumulatively (e.g., active connections); enum state is a snapshot, not a delta | `Gauge<T>` — records current value, replaces previous |
| `ObservableGauge` with shared dictionary | Requires concurrent dictionary keyed by tenantId to bridge from `EvaluateTenant` to callback; adds a write path and lock | `Gauge<T>.Record()` at call site |
| `TelemetryConstants.LeaderMeterName` for tenant meters | MetricRoleGatedExporter filters this meter on followers; tenant metrics need all-instance export | `TelemetryConstants.MeterName` ("SnmpCollector") |
| `delta()` PromQL for counter columns in table | `delta()` is for gauges; for monotonic counters use `rate()` or `increase()` | `rate(snmp_tenant_..._total[$__rate_interval])` |

---

## Version Compatibility

| Package | Version | Notes |
|---------|---------|-------|
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.0 | In csproj; `Gauge<T>` available since 1.10 |
| `OpenTelemetry.Extensions.Hosting` | 1.15.0 | `AddView` for histogram bucket override available |
| `System.Diagnostics.Metrics` | .NET 9 BCL | `Meter.CreateGauge<T>()` is a .NET 9 BCL API; OTel SDK wraps it |
| `Microsoft.Extensions.Diagnostics.Metrics` | N/A | Not used; project uses `IMeterFactory` from `Microsoft.Extensions.DependencyInjection` |

Note on `CreateGauge<T>()`: This is defined on `System.Diagnostics.Metrics.Meter` in .NET 9 BCL.
`SnmpMetricFactory` already calls it successfully. No NuGet package upgrade needed.

---

## Sources

- Codebase `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — existing Counter/Histogram pattern (HIGH)
- Codebase `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` — gate logic by meter name (HIGH)
- Codebase `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — meter name constants (HIGH)
- Codebase `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — `CreateGauge<double>()` in production use (HIGH)
- Codebase `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — meter registration, gated exporter wiring (HIGH)
- Codebase `deploy/grafana/dashboards/simetra-business.json` — table panel with label_join, delta, histogram_quantile patterns (HIGH)
- Codebase `deploy/grafana/dashboards/simetra-operations.json` — value mapping / color-background cell pattern for enum display (HIGH)
- GitHub issue open-telemetry/opentelemetry-dotnet #4805 — synchronous Gauge added in 1.10, PR #5867 merged (MEDIUM)
- OTel official docs https://opentelemetry.io/docs/languages/dotnet/metrics/instruments/ — ObservableGauge callback API (MEDIUM)
- OTel SDK customizing README https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md — `AddView` + `ExplicitBucketHistogramConfiguration` API (MEDIUM)

---
*Stack research for: tenant-level OTel metrics (counters, gauge, histogram) with all-instance export and Grafana table dashboard*
*Researched: 2026-03-22*
