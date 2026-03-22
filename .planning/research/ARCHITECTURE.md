# Architecture Research

**Domain:** Tenant-level observability metrics — SNMP monitoring system
**Researched:** 2026-03-22
**Confidence:** HIGH (all claims derived from direct inspection of named source files)

---

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          OTel MeterProvider                              │
│                                                                          │
│  AddMeter("SnmpCollector")          PipelineMetricService                │
│  AddMeter("SnmpCollector.Leader")   SnmpMetricFactory                    │
│  AddMeter("SnmpCollector.Tenant")   TenantMetricService  ← NEW           │
│  AddMeter("System.Runtime")         (runtime instrumentation)            │
│                                                                          │
│  PeriodicExportingMetricReader (15s, Cumulative)                         │
│       │                                                                  │
│       ▼                                                                  │
│  MetricRoleGatedExporter(gatedMeterName = "SnmpCollector.Leader")        │
│  ┌───────────────────────────────────────────────────────────────────┐   │
│  │  IsLeader = true  → forward full batch to OtlpMetricExporter      │   │
│  │  IsLeader = false → drop "SnmpCollector.Leader" metrics only      │   │
│  │                     pass all other meters through unchanged        │   │
│  └───────────────────────────────────────────────────────────────────┘   │
│       │                                                                  │
│       ▼                                                                  │
│  OtlpMetricExporter → Grafana Alloy → Prometheus remote_write → Grafana  │
└─────────────────────────────────────────────────────────────────────────┘

Meter ownership and export behavior:

  "SnmpCollector"         PipelineMetricService   ALL instances export
  "SnmpCollector.Leader"  SnmpMetricFactory        leader-only export
  "SnmpCollector.Tenant"  TenantMetricService       ALL instances export  ← NEW
```

### Component Responsibilities

| Component | Responsibility | Export behavior |
|-----------|---------------|-----------------|
| `PipelineMetricService` | 14 pipeline counters + 1 histogram on `"SnmpCollector"` meter | All instances |
| `SnmpMetricFactory` | `snmp_gauge`, `snmp_info` instruments on `"SnmpCollector.Leader"` meter | Leader only |
| `MetricRoleGatedExporter` | Drops `"SnmpCollector.Leader"` metrics on followers; forwards all other meters | — |
| `TenantMetricService` (NEW) | 8 tenant instruments on `"SnmpCollector.Tenant"` meter | All instances |
| `SnapshotJob` | 4-tier evaluation loop; calls both `PipelineMetricService` and `TenantMetricService` | — |
| `CommandWorkerService` | SET execution; calls `PipelineMetricService` for command outcomes; no tenant identity | — |
| `TenantVectorRegistry` | Holds tenant config; exposes `TenantCount`; no metric knowledge | — |

---

## MetricRoleGatedExporter: Bypass Strategy

### How the gate works today

`MetricRoleGatedExporter` is constructed with one `_gatedMeterName = "SnmpCollector.Leader"`. On follower instances, the `Export` method iterates the batch and forwards only metrics whose `MeterName` does not equal `_gatedMeterName`. Every other meter — including any future meter — passes through unconditionally.

This means the bypass for tenant metrics is already implemented. **No changes to `MetricRoleGatedExporter` are required.**

### Recommended strategy: third meter name in `TelemetryConstants`

Add one constant:

```csharp
// TelemetryConstants.cs
/// <summary>
/// Tenant observability meter — exported by ALL instances.
/// Used by TenantMetricService for per-tenant vector instruments.
/// </summary>
public const string TenantMeterName = "SnmpCollector.Tenant";
```

Register the meter in `ServiceCollectionExtensions.AddSnmpTelemetry`:

```csharp
metrics.AddMeter(TelemetryConstants.MeterName);        // existing
metrics.AddMeter(TelemetryConstants.LeaderMeterName);  // existing
metrics.AddMeter(TelemetryConstants.TenantMeterName);  // ADD
```

No other wiring changes. The gated exporter already passes any meter that is not `"SnmpCollector.Leader"`.

### Why the other options are rejected

| Option | Verdict | Reason |
|--------|---------|--------|
| Add tenant instruments to `PipelineMetricService` | Reject | Technically works (same all-instances meter) but conflates pipeline telemetry with tenant evaluation telemetry. The boundary between these two concerns is already respected in the existing design (`PipelineMetricService` vs `SnmpMetricFactory`). A new service maintains that pattern. |
| Metric-name prefix filtering in `MetricRoleGatedExporter` | Reject | The exporter is meter-name scoped by design. Adding name-prefix logic couples it to naming conventions and would silently break if names drifted. Meter-based filtering is the OTel-intended discrimination axis. |
| Separate OTel `MeterProvider` | Reject | Two providers means two OTLP exporters, two `PeriodicExportingMetricReader` instances, and duplicated resource-attribute configuration. Significant overhead for a problem the existing gated exporter already solves with zero changes. |

---

## New Component: TenantMetricService

### Location

`src/SnmpCollector/Telemetry/TenantMetricService.cs`

Mirrors `PipelineMetricService` exactly in structure: one `Meter` created via `IMeterFactory`, all instruments created in the constructor, public named increment/record methods.

### DI registration

In `ServiceCollectionExtensions.AddSnmpPipeline`, alongside `PipelineMetricService`:

```csharp
services.AddSingleton<TenantMetricService>();
```

### Instrument definitions

| ID | OTel name (proposed) | Type | Tags |
|----|----------------------|------|------|
| TMET-01 | `snmp.tenant.evaluations` | `Counter<long>` | `tenant_id`, `result` |
| TMET-02 | `snmp.tenant.stale` | `Counter<long>` | `tenant_id` |
| TMET-03 | `snmp.tenant.resolved` | `Counter<long>` | `tenant_id` |
| TMET-04 | `snmp.tenant.commands_dispatched` | `Counter<long>` | `tenant_id` |
| TMET-05 | `snmp.tenant.commands_suppressed` | `Counter<long>` | `tenant_id` |
| TMET-06 | `snmp.tenant.commands_failed` | `Counter<long>` | `tenant_id` |
| TMET-07 | `snmp.tenant.priority_group_advance_blocks` | `Counter<long>` | `priority` |
| TMET-08 | `snmp.tenant.active` | `Gauge<long>` | _(none)_ |
| TMET-09 | `snmp.tenant.cycle_duration_ms` | `Histogram<double>` | `tenant_id` |

Tag notes:
- `result` on TMET-01 takes values: `"resolved"`, `"healthy"`, `"unresolved"` — matching `TierResult` enum members.
- TMET-08 records a point-in-time count of configured tenants; it reads `ITenantVectorRegistry.TenantCount` once per cycle from `SnapshotJob.Execute`.
- TMET-09 requires per-tenant timing inside `EvaluateTenant` (see below).

---

## Increment Locations: Where Each Instrument Is Called

The following maps directly to the current `SnapshotJob` control flow. All instrument calls are additions; no existing calls are moved or removed.

```
SnapshotJob.Execute
│
├── [start of cycle]
│       TMET-08: tenantMetrics.SetActive(_registry.TenantCount)
│
├── foreach group in _registry.Groups
│     │
│     ├── EvaluateTenant(tenant)   [called per tenant, possibly parallel]
│     │     │
│     │     ├── Pre-tier: not ready → return Unresolved
│     │     │       [no new TMET increment — grace window, not an evaluation event]
│     │     │
│     │     ├── Tier 1: HasStaleness = true
│     │     │       TMET-02: IncrementStale(tenant.Id)
│     │     │       [falls through to Tier 4]
│     │     │
│     │     ├── Tier 2: AreAllResolvedViolated = true → return Resolved
│     │     │       TMET-03: IncrementResolved(tenant.Id)
│     │     │       TMET-01: IncrementEvaluation(tenant.Id, "resolved")
│     │     │       TMET-09: RecordCycleDuration(tenant.Id, elapsed)
│     │     │
│     │     ├── Tier 3: AreAllEvaluateViolated = false → return Healthy
│     │     │       TMET-01: IncrementEvaluation(tenant.Id, "healthy")
│     │     │       TMET-09: RecordCycleDuration(tenant.Id, elapsed)
│     │     │
│     │     └── Tier 4: command dispatch → return Unresolved
│     │           ├── per cmd: suppressed
│     │           │       TMET-05: IncrementCommandsSuppressed(tenant.Id)
│     │           ├── per cmd: TryWrite = true
│     │           │       TMET-04: IncrementCommandsDispatched(tenant.Id)
│     │           └── per cmd: TryWrite = false (channel full)
│     │                   TMET-06: IncrementCommandsFailed(tenant.Id)
│     │           TMET-01: IncrementEvaluation(tenant.Id, "unresolved")
│     │           TMET-09: RecordCycleDuration(tenant.Id, elapsed)
│     │
│     └── advance gate: shouldAdvance = false → break
│             TMET-07: IncrementAdvanceBlocks(group.Priority)
│
└── [existing] _pipelineMetrics.RecordSnapshotCycleDuration(sw.Elapsed.TotalMilliseconds)
```

### Per-tenant timing (TMET-09)

`EvaluateTenant` is currently synchronous with no timing. Add a `Stopwatch` at the top of `EvaluateTenant` and record before each `return`. Because `EvaluateTenant` has 4 return points (`Unresolved` from pre-tier, `Resolved` from Tier 2, `Healthy` from Tier 3, `Unresolved` from Tier 4), each must record before returning. The pre-tier `Unresolved` (grace window) should also record — it still consumes evaluation time.

`TenantMetricService` must be passed into `EvaluateTenant` (currently `internal`) either as a parameter or as a constructor-injected field. Using it as an injected field (same pattern as `_pipelineMetrics`) is the cleanest approach: add `TenantMetricService _tenantMetrics` to `SnapshotJob` constructor alongside `PipelineMetricService _pipelineMetrics`.

### CommandWorkerService: no changes

`CommandWorkerService` operates on individual `CommandRequest` items after dequeuing. At that point, `tenant_id` is not available — the channel carries only `(Ip, Port, CommandName, Value, ValueType)`. The TMET-04 through TMET-06 tenant-scoped command counters are incremented in `SnapshotJob.EvaluateTenant` at the dispatch decision site, where `tenant.Id` is in scope.

The existing `PipelineMetricService` counters in `CommandWorkerService` (`IncrementCommandFailed` with `device.Name` tag) remain unchanged and complementary: they cover SET execution failures at the worker level with a `device_name` tag.

---

## Integration Points Summary

### Files modified

| File | Change |
|------|--------|
| `src/SnmpCollector/Telemetry/TelemetryConstants.cs` | Add `TenantMeterName = "SnmpCollector.Tenant"` |
| `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` | `AddMeter(TelemetryConstants.TenantMeterName)` in `AddSnmpTelemetry`; `AddSingleton<TenantMetricService>()` in `AddSnmpPipeline` |
| `src/SnmpCollector/Jobs/SnapshotJob.cs` | Inject `TenantMetricService`; add per-tier increment calls; add per-tenant stopwatch |

### Files created

| File | Description |
|------|-------------|
| `src/SnmpCollector/Telemetry/TenantMetricService.cs` | New singleton; 8 instruments on `"SnmpCollector.Tenant"` meter |

### Files not modified

| File | Reason unchanged |
|------|-----------------|
| `MetricRoleGatedExporter.cs` | Bypass already works; no code change needed |
| `PipelineMetricService.cs` | No new instruments added here |
| `CommandWorkerService.cs` | No tenant identity available; existing counters unchanged |
| `TenantVectorRegistry.cs` | Read-only from metric perspective; `TenantCount` already public |
| `SnmpMetricFactory.cs` | Leader-gated instruments unaffected |

---

## Data Flow: SnapshotJob Evaluation to Grafana

```
Quartz 15s trigger
    │
    ▼
SnapshotJob.Execute
    │  iterates ITenantVectorRegistry.Groups (priority-sorted)
    │  calls EvaluateTenant(tenant) per tenant
    │
    ├── TierResult outcome known per tenant
    │       TenantMetricService.Increment*(tenant.Id)   ← TMET-01..07, 09
    │       TenantMetricService.SetActive(TenantCount)  ← TMET-08
    │
    ▼
OTel SDK accumulates Cumulative counters in-process
    │
    ▼
PeriodicExportingMetricReader (15s interval, Cumulative temporality)
    │
    ▼
MetricRoleGatedExporter
    │  IsLeader=true  → full batch to OtlpMetricExporter
    │  IsLeader=false → "SnmpCollector.Leader" dropped
    │                   "SnmpCollector.Tenant" forwarded (all instances)
    │
    ▼
OtlpMetricExporter → Grafana Alloy
    │
    ▼
Prometheus (scrape from Alloy or remote_write)
    │
    ▼
Grafana table panel
  Example queries:
    sum by (tenant_id) (rate(snmp_tenant_evaluations_total{result="unresolved"}[1m]))
    sum by (tenant_id) (snmp_tenant_commands_dispatched_total)
    snmp_tenant_active
```

---

## Architectural Patterns

### Pattern 1: Meter-per-export-category

**What:** Each logical observability category (pipeline health, business/leader-gated, tenant evaluation) owns a distinct `Meter` with a name constant in `TelemetryConstants`. `MetricRoleGatedExporter` gates by meter name.

**When to use:** Whenever instruments in the same service have different export requirements (all-instances vs leader-only).

**Trade-offs:** Requires one `AddMeter` call per category at startup. Names must be documented centrally to stay consistent. The upside is that gating logic is decoupled from individual metric names and never needs to know about new instruments.

### Pattern 2: Singleton metric service per meter

**What:** Each `Meter` is wrapped in a singleton service. Instruments are created once in the constructor via `IMeterFactory`. Public methods name the increment/record operations explicitly.

**When to use:** Always, for this codebase. Prevents duplicate instrument registration (OTel throws on duplicate names within the same meter), makes the metric surface explicit and mockable, and provides a single injection point.

**Trade-offs:** One DI registration and constructor call per meter. Negligible cost.

### Pattern 3: Increment at the decision site, not the execution site

**What:** Tenant-scoped command counters (TMET-04 through TMET-06) are incremented in `SnapshotJob.EvaluateTenant` where `tenant.Id` is known, not in `CommandWorkerService` where the dequeued `CommandRequest` carries no tenant identity.

**When to use:** Whenever a metric tag value is only available at the decision site, not at the downstream executor.

**Trade-offs:** The pipeline-level `PipelineMetricService.IncrementCommandDispatched(device.Name)` and the tenant-level TMET-04 overlap in what they count. This is intentional: one carries `device_name`, the other carries `tenant_id`. Both cardinality dimensions are useful for different query shapes.

---

## Anti-Patterns

### Anti-Pattern 1: Adding tenant instruments to PipelineMetricService

**What people do:** Append TMET-01 through TMET-09 methods to `PipelineMetricService` because it uses the already all-instances `"SnmpCollector"` meter.

**Why it's wrong:** `PipelineMetricService` owns pipeline-health telemetry (event counts, trap counts, poll counts). Tenant evaluation outcomes are a different concern. Mixing them creates a service with two unrelated responsibilities and makes future extraction harder. The existing design already enforces this boundary (pipeline metrics in one service, business metrics in another).

**Do this instead:** New `TenantMetricService` on `"SnmpCollector.Tenant"` meter. Same pattern, clean boundary.

### Anti-Pattern 2: Metric-name prefix filtering in MetricRoleGatedExporter

**What people do:** Add `!metric.Name.StartsWith("snmp_tenant_")` inside the follower filter to pass tenant metrics through.

**Why it's wrong:** The exporter's logic is entirely meter-name-based — the OTel-intended axis. Switching to metric-name-based filtering couples the exporter to naming conventions, bypasses the `TelemetryConstants` contract, and silently breaks if naming drifts. It also requires touching a working, tested component.

**Do this instead:** New meter name. Zero changes to `MetricRoleGatedExporter`.

### Anti-Pattern 3: Recording per-tenant duration from outside EvaluateTenant

**What people do:** Wrap the `Task.WhenAll` group call with a stopwatch and associate elapsed time with each tenant.

**Why it's wrong:** `Task.WhenAll` measures wall-clock time for the entire group, not per-tenant CPU time. Tenants within a group run in parallel; one slow tenant inflates the duration attributed to all others. The `tenant_id` tag on TMET-09 becomes meaningless.

**Do this instead:** Stopwatch inside `EvaluateTenant`, recorded before each return point.

### Anti-Pattern 4: Deriving tenant-scoped command counters from CommandWorkerService

**What people do:** Add a `tenant_id` field to `CommandRequest` and increment TMET-04/05/06 inside `CommandWorkerService.ExecuteCommandAsync`.

**Why it's wrong:** The suppression decision and channel-full detection already happen in `SnapshotJob.EvaluateTenant`. Threading `tenant_id` through `CommandRequest` propagates a concern across a boundary for metrics only. The channel is intentionally a value-type transport with no tenant metadata. The dispatch decision site already has everything needed.

**Do this instead:** Increment TMET-04/05/06 in `EvaluateTenant` at each dispatch decision.

---

## Build Order

Each step has an explicit dependency on the previous one.

**Step 1: `TelemetryConstants`**

Add `TenantMeterName = "SnmpCollector.Tenant"`. No dependencies. No behavioral change.

**Step 2: `TenantMetricService`**

New file. Depends on `TelemetryConstants.TenantMeterName` and `IMeterFactory`. No callers yet. Constructor creates all 8 instruments. Can be verified standalone with a unit test (construct, verify no exceptions thrown).

**Step 3: `ServiceCollectionExtensions`**

Two one-line additions: `AddMeter` in `AddSnmpTelemetry`, `AddSingleton<TenantMetricService>()` in `AddSnmpPipeline`. Depends on steps 1 and 2. No behavioral change until `SnapshotJob` calls the methods.

**Step 4: `SnapshotJob`**

Inject `TenantMetricService` as constructor parameter (alongside existing `PipelineMetricService`). Add increment calls at each tier exit. Add `Stopwatch` inside `EvaluateTenant`. Depends on step 2. This is the only step with observable behavioral change (new metrics begin flowing).

**Step 5: Tests**

Unit tests for `TenantMetricService` (no exceptions on construction). Unit tests for `SnapshotJob` verifying increment calls via mock `TenantMetricService`. Integration test confirming the metric names appear in the OTel export.

---

## Sources

All claims derive from direct inspection of:
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — existing instrument list, meter name, increment method signatures
- `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` — `_gatedMeterName` field, follower filter logic (line 56: `metric.MeterName` comparison)
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — `MeterName` and `LeaderMeterName` constants
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — leader-gated meter pattern, `IMeterFactory` usage
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — `AddSnmpTelemetry` meter registration, `AddSnmpPipeline` singleton registrations
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — 4-tier evaluation flow, all return sites, existing `_pipelineMetrics` call sites, constructor parameter list
- `src/SnmpCollector/Services/CommandWorkerService.cs` — `CommandRequest` structure, no `tenant_id` in channel items, existing metric call sites
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `TenantCount` property, `Groups` iteration
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — data model confirming no tenant identity on individual holders

Confidence: HIGH for all integration points — derived from code, not inference.

---
*Architecture research for: tenant vector metrics integration — SnmpCollector*
*Researched: 2026-03-22*
