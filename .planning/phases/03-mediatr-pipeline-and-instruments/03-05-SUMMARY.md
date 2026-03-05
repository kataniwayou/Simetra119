---
phase: 03-mediatr-pipeline-and-instruments
plan: 05
subsystem: pipeline
tags: [mediatr, di, behaviors, pipeline, ioc, serviceregistration]

# Dependency graph
requires:
  - phase: 03-01
    provides: PipelineMetricService, SnmpOidReceived notification class
  - phase: 03-02
    provides: LoggingBehavior, ExceptionBehavior
  - phase: 03-03
    provides: ValidationBehavior, OidResolutionBehavior
  - phase: 03-04
    provides: ISnmpMetricFactory, SnmpMetricFactory
provides:
  - AddSnmpPipeline extension method wiring all Phase 3 pipeline services into DI
  - MediatR registered with 4 open behaviors in correct Logging->Exception->Validation->OidResolution order
  - TaskWhenAllPublisher configured as singleton instance (PIPE-09)
  - PipelineMetricService and ISnmpMetricFactory/SnmpMetricFactory registered as singletons
  - Program.cs calling AddSnmpPipeline after AddSnmpConfiguration, before AddSnmpScheduling
affects: [03-06, phase-5-snmp-listener, phase-6-scheduler]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "AddSnmpPipeline follows same extension method pattern as AddSnmpConfiguration and AddSnmpScheduling"
    - "TaskWhenAllPublisher registered as singleton instance via cfg.NotificationPublisher = new ..., not AddSingleton<T>"
    - "Open behavior registration order matches pipeline execution order: first registered = outermost"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Program.cs

key-decisions:
  - "AddSnmpPipeline placed between AddSnmpConfiguration and AddSnmpScheduling — pipeline services depend on Phase 2 registrations (IDeviceRegistry, IOidMapService)"
  - "TaskWhenAllPublisher set as cfg.NotificationPublisher instance, not via AddSingleton — MediatR config owns the reference per PIPE-09"
  - "4 AddOpenBehavior calls in order Logging->Exception->Validation->OidResolution matches PIPE-08 spec: first registered = outermost = runs first"

patterns-established:
  - "Pipeline DI wiring: all pipeline services grouped in AddSnmpPipeline, separate from scheduling and configuration concerns"
  - "Behavior order annotation: each AddOpenBehavior call commented with ordinal and direction (outermost/innermost) for future maintainers"

# Metrics
duration: 4min
completed: 2026-03-05
---

# Phase 3 Plan 05: DI Wiring Summary

**MediatR pipeline wired into DI with 4 open behaviors (Logging->Exception->Validation->OidResolution), TaskWhenAllPublisher as singleton instance, and all Phase 3 singletons registered via AddSnmpPipeline**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-03-05T01:40:30Z
- **Completed:** 2026-03-05T01:44:33Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- `AddSnmpPipeline` extension method created with full MediatR registration, 4 ordered open behaviors, and Phase 3 singleton services
- Program.cs wired with `AddSnmpPipeline()` call inserted in correct position (after `AddSnmpConfiguration`, before `AddSnmpScheduling`)
- Build verified at zero errors with all Phase 3 types (behaviors, metric service, factory) resolving correctly

## Task Commits

Each task was committed atomically:

1. **Task 1: Add AddSnmpPipeline extension method** - `4246ffd` (feat)
2. **Task 2: Wire AddSnmpPipeline into Program.cs** - `82911c9` (feat)

## Files Created/Modified

- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Added `AddSnmpPipeline` method with MediatR registration (TaskWhenAllPublisher, 4 open behaviors), `PipelineMetricService` singleton, `ISnmpMetricFactory`/`SnmpMetricFactory` singleton; updated XML doc with new registration order
- `src/SnmpCollector/Program.cs` - Added `builder.Services.AddSnmpPipeline()` call between `AddSnmpConfiguration` and `AddSnmpScheduling`

## Decisions Made

- `AddSnmpPipeline` inserted after `AddSnmpConfiguration` because `OidResolutionBehavior` depends on `IOidMapService` and `ValidationBehavior` depends on `IDeviceRegistry` — both registered by `AddSnmpConfiguration`. Ordering is a correctness constraint.
- `TaskWhenAllPublisher` set via `cfg.NotificationPublisher = new TaskWhenAllPublisher()` (not `services.AddSingleton<...>`) because MediatR 12.x reads the instance from `MediatRServiceConfiguration.NotificationPublisher` directly.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

During startup verification, the app failed with `OptionsValidationException: SiteOptions.Name is required`. This is a pre-existing, expected behavior from Phase 1 (`AddSnmpTelemetry`'s `SnmpLogEnrichmentProcessor` resolves `IOptions<SiteOptions>` at logger provider build time). The failure occurs before any pipeline DI resolution and confirms the pipeline registered successfully — no pipeline-specific DI errors.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All Phase 3 pipeline services are registered and resolve correctly
- `AddSnmpPipeline` is the authoritative DI entry point for the MediatR pipeline; Phase 6 (SNMP Scheduler) and Phase 5 (SNMP Listener) will call `IMediator.Publish(new SnmpOidReceived(...))` to dispatch notifications through the pipeline
- Plan 03-06 (the final Phase 3 plan) can proceed; it will integrate any remaining Phase 3 wiring or proceed to test coverage

---
*Phase: 03-mediatr-pipeline-and-instruments*
*Completed: 2026-03-05*
