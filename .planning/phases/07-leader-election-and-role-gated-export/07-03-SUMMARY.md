---
phase: 07-leader-election-and-role-gated-export
plan: 03
subsystem: telemetry
tags: [leader-election, MetricRoleGatedExporter, BaseExporter, OtlpMetricExporter, reflection, SnmpMetricFactory, LeaderMeterName, role-gating, metrics]

# Dependency graph
requires:
  - phase: 07-01-foundation-types
    provides: ILeaderElection interface, TelemetryConstants.LeaderMeterName, AlwaysLeaderElection
  - phase: 01-infrastructure-foundation
    provides: TelemetryConstants.MeterName, PipelineMetricService, SnmpMetricFactory baseline
provides:
  - MetricRoleGatedExporter: BaseExporter<Metric> wrapper that gates LeaderMeterName export behind ILeaderElection.IsLeader
  - SnmpMetricFactory updated to register snmp_gauge, snmp_counter, snmp_info on LeaderMeterName meter
affects:
  - 07-04-DI-wiring (registers MetricRoleGatedExporter in OTel metric pipeline)
  - 07-05-tests (SnmpMetricFactoryTests must update MeterListener to listen on LeaderMeterName)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "MetricRoleGatedExporter delegation pattern: BaseExporter<Metric> wraps inner exporter, delegates OnForceFlush/OnShutdown/Dispose"
    - "Lazy reflection-based ParentProvider propagation: one-time reflection SetValue on first Export call to include resource attributes in OTLP"
    - "ExportResult.Success for intentionally suppressed followers: return Success (not Failure) when zero ungated metrics to avoid SDK retry backoff"
    - "Follower filtering via List<Metric> + new Batch<Metric>(array, count): enumerates batch, filters by MeterName, constructs filtered batch"

key-files:
  created:
    - src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs
  modified:
    - src/SnmpCollector/Telemetry/SnmpMetricFactory.cs

key-decisions:
  - "MetricRoleGatedExporter takes BaseExporter<Metric> not OtlpMetricExporter directly — generic enough for testing with stub exporters"
  - "ParentProvider reflection required: internal setter on BaseExporter<Metric> not accessible via normal API; one-time lazy propagation avoids repeated reflection cost"
  - "ExportResult.Success for empty follower batch: Failure triggers OTel SDK exponential retry; intentional suppression is not an error"
  - "SnmpMetricFactory meter change is ONLY change needed: instruments inherit meter from Meter object; no instrument-level changes required"
  - "PipelineMetricService left unchanged on MeterName: pipeline counters (snmp.event.*, snmp.poll.*, snmp.trap.*) must export from ALL instances"

patterns-established:
  - "Role-gated export pattern: wrap inner exporter in MetricRoleGatedExporter, pass gatedMeterName=TelemetryConstants.LeaderMeterName"
  - "Meter-based discrimination: meter name (not instrument name) is the gating key — all instruments on a meter are gated together"

# Metrics
duration: 4min
completed: 2026-03-05
---

# Phase 7 Plan 03: MetricRoleGatedExporter and SnmpMetricFactory LeaderMeterName Summary

**MetricRoleGatedExporter BaseExporter<Metric> wrapper gates snmp_gauge/snmp_counter/snmp_info export behind ILeaderElection.IsLeader using meter name discrimination; SnmpMetricFactory moved to LeaderMeterName so business instruments are filterable by followers**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-03-05T16:20:12Z
- **Completed:** 2026-03-05T16:24:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- MetricRoleGatedExporter created: leaders pass entire Batch<Metric> to inner exporter; followers enumerate batch, filter out LeaderMeterName metrics, pass only ungated metrics
- Reflection-based ParentProvider propagation ensures OTLP resource attributes (service.name, service.instance.id) are included in inner exporter output despite the internal setter on BaseExporter<Metric>
- ExportResult.Success returned for follower batches with zero ungated metrics — correctly avoids OTel SDK retry backoff on intentional suppression
- SnmpMetricFactory meter changed from MeterName to LeaderMeterName: all three business instruments (snmp_gauge, snmp_counter, snmp_info) are now on the gatable meter
- PipelineMetricService confirmed unchanged: pipeline counters remain on MeterName and will export from all instances

## Task Commits

Each task was committed atomically:

1. **Task 1: MetricRoleGatedExporter with reflection-based ParentProvider propagation** - `ec40824` (feat)
2. **Task 2: Update SnmpMetricFactory to use LeaderMeterName** - `6f379a9` (feat)

## Files Created/Modified

- `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` - Sealed BaseExporter<Metric> wrapper; leader passes batch through; follower filters by MeterName; lazy reflection ParentProvider propagation
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` - Meter created on TelemetryConstants.LeaderMeterName instead of MeterName; updated XML doc to document leader-gated meter assignment

## Decisions Made

- **MetricRoleGatedExporter accepts BaseExporter<Metric> inner:** Generic type (not OtlpMetricExporter) makes the wrapper testable with stub exporters in Plan 05 tests without requiring a real OTLP endpoint.
- **Lazy reflection ParentProvider propagation:** SDK sets ParentProvider during MeterProvider construction, after the exporter's constructor runs. Reflection is the only way to set the internal setter. One-time lazy call in Export avoids repeated reflection overhead on the hot path.
- **ExportResult.Success for empty follower batch:** The OTel SDK interprets ExportResult.Failure as a transient network error and triggers exponential retry. Intentional suppression is not a failure — returning Success avoids spurious retry cycles.
- **Only meter creation changes in SnmpMetricFactory:** Instruments inherit their meter from the Meter object they are created on. Moving `meterFactory.Create(...)` from MeterName to LeaderMeterName is sufficient; each instrument's MeterName property automatically reflects the change.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. The `Batch<Metric>(T[] array, int count)` constructor was confirmed available in OTel 1.15.0 (same as the Simetra reference implementation). Build succeeded on first attempt.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

MetricRoleGatedExporter and updated SnmpMetricFactory are ready for DI wiring (Plan 04):
- Plan 04 must register MetricRoleGatedExporter in the OTel metric pipeline via `AddMetricExporter` or equivalent hook
- Plan 04 must pass `TelemetryConstants.LeaderMeterName` as the `gatedMeterName` constructor argument
- Plan 04 must resolve `ILeaderElection` from DI (AlwaysLeaderElection or K8sLeaseElection based on SiteOptions.Role)

Plan 05 (tests) must update `SnmpMetricFactoryTests` to listen on `TelemetryConstants.LeaderMeterName` instead of `TelemetryConstants.MeterName` — existing tests will fail until this fix, as noted in the plan.

No blockers for Plan 04.

---
*Phase: 07-leader-election-and-role-gated-export*
*Completed: 2026-03-05*
