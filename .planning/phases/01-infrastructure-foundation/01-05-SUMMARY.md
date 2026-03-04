---
phase: 01-infrastructure-foundation
plan: 05
subsystem: infra
tags: [dotnet, options-validation, fail-fast, IValidateOptions, configuration]

# Dependency graph
requires:
  - phase: 01-infrastructure-foundation/01-04
    provides: AddSnmpConfiguration with ValidateDataAnnotations + ValidateOnStart pipeline already registered
provides:
  - SiteOptionsValidator: IValidateOptions<SiteOptions> checking IsNullOrWhiteSpace(Name) -> "Site:Name is required"
  - OtlpOptionsValidator: IValidateOptions<OtlpOptions> checking Endpoint and ServiceName fields
  - Both validators registered in AddSnmpConfiguration as singletons
  - HARD-04 satisfied: application refuses to start with missing required config
affects:
  - Phase 2 (OTel cardinality): all options-validation patterns established here apply to any new options classes
  - Phase 6 (state/metric poll): SnmpPollerOptions will follow same IValidateOptions pattern
  - Phase 7 (leader election): LeaderOptions will follow same IValidateOptions pattern

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "IValidateOptions<T> custom validator pattern for cross-field config validation (belt-and-suspenders alongside [Required] DataAnnotations)"
    - "File-scoped namespace SnmpCollector.Configuration.Validators for all validator classes"
    - "public sealed class + IValidateOptions<T> + ValidateOptionsResult.Fail(failures) pattern"

key-files:
  created:
    - src/SnmpCollector/Configuration/Validators/SiteOptionsValidator.cs
    - src/SnmpCollector/Configuration/Validators/OtlpOptionsValidator.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "IValidateOptions<T> adds 'Site:Name is required' alongside [Required] DataAnnotation -- belt-and-suspenders for clearer operational error messages"
  - "Validators registered as AddSingleton<IValidateOptions<T>, TValidator> at end of AddSnmpConfiguration"

patterns-established:
  - "Validator pattern: public sealed class XxxValidator : IValidateOptions<XxxOptions> in SnmpCollector.Configuration.Validators namespace"
  - "Fail-fast messages use config key path format: 'Section:Field is required' for operational clarity"

# Metrics
duration: 6min
completed: 2026-03-05
---

# Phase 1 Plan 5: Custom Options Validators Summary

**IValidateOptions<SiteOptions> and IValidateOptions<OtlpOptions> validators enforce fail-fast startup on missing required config, closing HARD-04**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-04T22:58:00Z
- **Completed:** 2026-03-04T23:04:28Z
- **Tasks:** 2 (1 implementation, 1 verification-only)
- **Files modified:** 3 (2 created, 1 modified)

## Accomplishments
- Created SiteOptionsValidator (checks Site:Name) and OtlpOptionsValidator (checks Otlp:Endpoint and Otlp:ServiceName)
- Both validators registered in AddSnmpConfiguration as IValidateOptions<T> singletons
- Verified all four fail-fast scenarios: empty Site:Name, empty Otlp:Endpoint, IntervalSeconds=0, and valid config
- Confirmed "Configuration validation failed:" + per-failure bullet list appears on stderr before any network traffic
- HARD-04 and Phase 1 Success Criterion #3 satisfied

## Task Commits

Each task was committed atomically:

1. **Task 1: Create SiteOptionsValidator and OtlpOptionsValidator** - `192191e` (feat)
2. **Task 2: Verify fail-fast behavior** - no commit (verification-only task, no files modified)

**Plan metadata:** (to be committed after SUMMARY creation)

## Files Created/Modified
- `src/SnmpCollector/Configuration/Validators/SiteOptionsValidator.cs` - Custom IValidateOptions<SiteOptions>; checks IsNullOrWhiteSpace(Name), returns "Site:Name is required"
- `src/SnmpCollector/Configuration/Validators/OtlpOptionsValidator.cs` - Custom IValidateOptions<OtlpOptions>; checks Endpoint and ServiceName, returns "Otlp:Endpoint is required" / "Otlp:ServiceName is required"
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Added using for Validators namespace; added AddSingleton<IValidateOptions<SiteOptions>, SiteOptionsValidator> and AddSingleton<IValidateOptions<OtlpOptions>, OtlpOptionsValidator> at end of AddSnmpConfiguration

## Decisions Made
- IValidateOptions custom messages use "Section:Field is required" format (matching OTel/Prometheus label naming conventions) for operational clarity in production alerts
- No custom validator needed for LoggingOptions or CorrelationJobOptions -- [Range] and [Required] DataAnnotations provide sufficient messages for those types

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

**Observation during Test 1 verification (Site:Name empty):** The "Configuration validation failed:" formatted prefix from Program.cs `catch (OptionsValidationException)` does NOT appear for the SiteOptions validation failure. This is because `IOptions<SiteOptions>` is resolved inside the OpenTelemetry logging processor factory (in `AddSnmpTelemetry`), which fires during `builder.Build()` DI container initialization -- NOT during `host.RunAsync()` where the catch block lives. The "Site:Name is required" text IS present in the unhandled exception message on stderr, satisfying HARD-04. This is pre-existing behavior from Plan 01-04's design decision to resolve `IOptions<SiteOptions>` inside the OTel logging processor factory.

For Otlp:Endpoint and CorrelationJob:IntervalSeconds validation failures, the formatted output IS correct ("Configuration validation failed: / - ...") because those validations fire during ValidateOnStart in RunAsync.

This is not a bug -- the fail-fast behavior is correct. The only difference is the output format for the SiteOptions path.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 1 infrastructure foundation complete: all 5 plans done
- Validation pipeline: ValidateDataAnnotations + custom IValidateOptions + ValidateOnStart all wired and verified
- Phase 2 (OTel cardinality locking) can begin; any new options classes should follow the same validator pattern
- No blockers

---
*Phase: 01-infrastructure-foundation*
*Completed: 2026-03-05*
