---
phase: 72
plan: "01"
subsystem: telemetry
tags: [otel, metrics, tenant, di, unit-tests]

dependency-graph:
  requires: []
  provides:
    - TenantMetricService singleton with 8 OTel instruments on SnmpCollector.Tenant meter
    - ITenantMetricService interface for testable injection
    - TenantState enum (NotReady=0, Healthy=1, Resolved=2, Unresolved=3)
    - TelemetryConstants.TenantMeterName constant
  affects:
    - Phase 73: SnapshotJob instrumentation will inject ITenantMetricService to call all 8 methods

tech-stack:
  added: []
  patterns:
    - MeterFactory-based instrument creation (singleton, no duplicate registration)
    - IMeterFactory + Gauge<double> for enum-as-integer state reporting
    - TagList with tenant_id + priority tags (no device_name leakage)

key-files:
  created:
    - src/SnmpCollector/Pipeline/TenantState.cs
    - src/SnmpCollector/Telemetry/ITenantMetricService.cs
    - src/SnmpCollector/Telemetry/TenantMetricService.cs
    - tests/SnmpCollector.Tests/Telemetry/TenantMetricServiceTests.cs
  modified:
    - src/SnmpCollector/Telemetry/TelemetryConstants.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

decisions:
  - "ITenantMetricService registered via interface (not concrete type) for testability in Phase 73"
  - "Gauge<double> records (double)(int)state — enum cast to int then double"
  - "SnmpCollector.Tenant meter added to AddMeter alongside pipeline/leader meters — passes MetricRoleGatedExporter ungated by design"
  - "No description parameters on counters (mirrors PipelineMetricService convention)"

metrics:
  duration: "2m 17s"
  completed: "2026-03-23"
---

# Phase 72 Plan 01: TenantMetricService Meter Registration Summary

**One-liner:** TenantMetricService singleton with 8 OTel instruments (6 counters, 1 gauge, 1 histogram) on SnmpCollector.Tenant meter, registered via ITenantMetricService interface, with MeterListener-based tests confirming all instruments emit with correct names and tenant_id/priority tags.

## What Was Built

Created the complete tenant metric infrastructure for Phase 73 (SnapshotJob instrumentation):

- **TenantState enum** (`Pipeline/TenantState.cs`): 4 values with explicit integers, replaces former internal SnapshotJob.TierResult enum.
- **TelemetryConstants.TenantMeterName**: `"SnmpCollector.Tenant"` constant used everywhere — no magic strings.
- **ITenantMetricService** (`Telemetry/ITenantMetricService.cs`): 8-method interface with `(string tenantId, int priority)` signatures on counters, `TenantState` on gauge method, `double durationMs` on histogram method.
- **TenantMetricService** (`Telemetry/TenantMetricService.cs`): Sealed singleton implementing ITenantMetricService + IDisposable, mirrors PipelineMetricService pattern exactly.
- **DI registration**: `AddMeter(TenantMeterName)` in WithMetrics, `AddSingleton<ITenantMetricService, TenantMetricService>` in AddSnmpPipeline.
- **8 unit tests**: MeterListener-based, each test validates instrument name, measurement value, and tag presence/absence.

## Instruments Created

| Instrument | Type | Name |
|---|---|---|
| Tier-1 stale | Counter<long> | tenant.tier1.stale |
| Tier-2 resolved | Counter<long> | tenant.tier2.resolved |
| Tier-3 evaluate | Counter<long> | tenant.tier3.evaluate |
| Command dispatched | Counter<long> | tenant.command.dispatched |
| Command failed | Counter<long> | tenant.command.failed |
| Command suppressed | Counter<long> | tenant.command.suppressed |
| Tenant state | Gauge<double> | tenant.state |
| Evaluation duration | Histogram<double> | tenant.evaluation.duration.milliseconds |

## Decisions Made

| Decision | Rationale |
|---|---|
| Register ITenantMetricService via interface, not concrete type | Testability for Phase 73 — SnapshotJob can use mock in unit tests |
| Gauge<double> with (double)(int)state cast | OTel Gauge requires numeric type; enum-to-int-to-double preserves Prometheus integer semantics |
| SnmpCollector.Tenant meter not gated in MetricRoleGatedExporter | Tenant metrics must export from all instances (not leader-only); MetricRoleGatedExporter unchanged |
| No description on counters | Mirrors PipelineMetricService convention |

## Verification

- `dotnet build src/SnmpCollector/SnmpCollector.csproj` — 0 errors
- `dotnet test` — 470 passed, 0 failed (8 new TenantMetricServiceTests + 462 existing)
- No magic string "SnmpCollector.Tenant" in TenantMetricService.cs code (only XML doc comments)
- AddMeter and AddSingleton confirmed in ServiceCollectionExtensions.cs

## Deviations from Plan

None — plan executed exactly as written.

## Next Phase Readiness

Phase 73 (SnapshotJob instrumentation) can proceed immediately:
- Inject `ITenantMetricService` into SnapshotJob
- Call all 8 methods at appropriate points in the evaluation cycle
- Stopwatch per-tenant for `RecordEvaluationDuration`
- Increment by holder/command count per cycle (not by 1)
