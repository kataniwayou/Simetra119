---
phase: 41-threshold-model-and-holder-storage
plan: 01
subsystem: pipeline
tags: [csharp, configuration, MetricSlotHolder, ThresholdOptions, TenantVectorRegistry]

# Dependency graph
requires:
  - phase: 40-role-validation
    provides: MetricSlotOptions.Role pattern used as template for Threshold property
  - phase: 37-json-deserialization
    provides: PropertyNameCaseInsensitive=true on deserializer (no JsonPropertyName attributes needed)
provides:
  - ThresholdOptions sealed class with double? Min and double? Max
  - MetricSlotOptions.Threshold nullable property (backward compatible)
  - MetricSlotHolder.Threshold get-only property set from constructor
  - TenantVectorRegistry.Reload passes metric.Threshold through to holder
  - 3 unit tests verifying end-to-end threshold storage flow
affects: [42-threshold-validation, future runtime threshold evaluation phases]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ThresholdOptions: sealed class POCO with double? Min and double? Max — no JsonPropertyName attributes (PropertyNameCaseInsensitive covers it)"
    - "Config-to-holder pass-through: optional constructor param with default null keeps all call sites backward compatible"
    - "Threshold is config identity, not runtime state — not added to CopyFrom"

key-files:
  created:
    - src/SnmpCollector/Configuration/ThresholdOptions.cs
  modified:
    - src/SnmpCollector/Configuration/MetricSlotOptions.cs
    - src/SnmpCollector/Pipeline/MetricSlotHolder.cs
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs

key-decisions:
  - "ThresholdOptions is a sealed class (not record) — consistent with all other options types in the project"
  - "Min and Max are double? (nullable) — either can be absent independently"
  - "threshold = null as last optional parameter ensures zero breaking changes to existing call sites"
  - "Threshold NOT added to CopyFrom — it is config identity, not runtime state; each reload provides authoritative value from new config"
  - "No validation in this phase — Phase 42 handles Min > Max detection and skip-with-error-log logic"

patterns-established:
  - "Optional constructor param pattern: new optional params on MetricSlotHolder always go last with default null/0 to preserve backward compat"
  - "Config-to-holder identity flow: MetricSlotOptions property -> Reload passes to constructor -> MetricSlotHolder exposes as get-only"

# Metrics
duration: 2min
completed: 2026-03-15
---

# Phase 41 Plan 01: Threshold Model & Holder Storage Summary

**ThresholdOptions sealed class (double? Min, double? Max) wired through MetricSlotOptions, MetricSlotHolder constructor, and TenantVectorRegistry.Reload with 3 new unit tests**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-15T12:25:26Z
- **Completed:** 2026-03-15T12:27:34Z
- **Tasks:** 2
- **Files modified:** 6 (1 created, 5 modified)

## Accomplishments
- Created ThresholdOptions sealed class following the existing config POCO pattern
- Wired ThresholdOptions through the full config-to-holder chain: MetricSlotOptions -> TenantVectorRegistry.Reload -> MetricSlotHolder
- All 3 new tests verify the storage chain end-to-end; all 329 tests pass with zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Add ThresholdOptions and wire production code** - `e8fa603` (feat)
2. **Task 2: Add 3 unit tests for threshold storage** - `29acd3c` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/SnmpCollector/Configuration/ThresholdOptions.cs` - New sealed class with double? Min and double? Max
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` - Added ThresholdOptions? Threshold property after Role
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` - Added using, ThresholdOptions? Threshold property, extended constructor with optional param
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` - Reload now passes metric.Threshold to MetricSlotHolder constructor
- `tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs` - Added Constructor_StoresThreshold and Constructor_NullThreshold_DefaultsToNull
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` - Added Reload_ThresholdFromConfig_StoredInHolder (section 12)

## Decisions Made
- ThresholdOptions is a sealed class (not record) — consistent with MetricSlotOptions, CommandSlotOptions, DeviceOptions patterns throughout the project
- Optional parameter `ThresholdOptions? threshold = null` placed last in MetricSlotHolder constructor to maintain backward compatibility at all existing call sites (heartbeat holder, existing tests, CreateHolder helper)
- Threshold deliberately excluded from `CopyFrom` — it is configuration identity sourced fresh on every reload, not runtime state to carry over
- No validation added — Phase 42 owns Min > Max detection and the skip-invalid-field-keep-entry behavior

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Threshold data is now available on all MetricSlotHolders loaded from config
- Phase 42 can read `holder.Threshold` and `metricOptions.Threshold` to implement validation (Min > Max detection, error log, null-out on holder)
- No blockers or concerns

---
*Phase: 41-threshold-model-and-holder-storage*
*Completed: 2026-03-15*
