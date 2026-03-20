---
phase: quick-082
plan: 01
subsystem: configuration
tags: [csharp, dotnet, configuration, refactor, PollOptions, PollMetricOptions]

# Dependency graph
requires:
  - phase: any phase using PollOptions
    provides: PollOptions.MetricNames (List<string>) — now replaced
provides:
  - PollMetricOptions sealed class with MetricName property
  - PollOptions.Metrics (List<PollMetricOptions>) replacing MetricNames
  - All JSON/YAML configs updated to new object array shape
affects: [any future phase adding per-metric configuration fields]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Per-metric config wrapper: PollMetricOptions wraps MetricName to allow future per-metric fields (thresholds, labels) without another breaking change"

key-files:
  created:
    - src/SnmpCollector/Configuration/PollMetricOptions.cs
  modified:
    - src/SnmpCollector/Configuration/PollOptions.cs
    - src/SnmpCollector/Services/DeviceWatcherService.cs
    - src/SnmpCollector/config/devices.json
    - src/SnmpCollector/appsettings.Development.json
    - deploy/k8s/snmp-collector/simetra-devices.yaml
    - deploy/k8s/production/configmap.yaml
    - tests/SnmpCollector.Tests/Pipeline/AggregatedMetricModelTests.cs
    - tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs

key-decisions:
  - "PollMetricOptions is a sealed class (not a record) to allow future mutable properties"
  - "JSON key renamed from MetricNames to Metrics; object key is MetricName — aligns with existing TenantVector slot naming"

patterns-established:
  - "Per-metric wrapper: future per-metric config (thresholds, labels) goes in PollMetricOptions, not PollOptions"

# Metrics
duration: 15min
completed: 2026-03-20
---

# Quick Task 082: MetricNames to PollMetricOptions Summary

**PollOptions.MetricNames (List<string>) replaced with PollOptions.Metrics (List<PollMetricOptions>) across C# source, 4 config files, and unit tests — enables future per-metric configuration without another breaking change**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-20T12:45:00Z
- **Completed:** 2026-03-20T13:00:00Z
- **Tasks:** 3
- **Files modified:** 8 (+ 1 created)

## Accomplishments

- Created `PollMetricOptions` sealed class with `MetricName` property in `SnmpCollector.Configuration` namespace
- Replaced `PollOptions.MetricNames` (List<string>) with `PollOptions.Metrics` (List<PollMetricOptions>) and updated XML doc comment
- Updated `DeviceWatcherService.BuildPollGroups` to iterate `poll.Metrics` using `m.MetricName`
- Transformed all 4 config files (33+ poll groups total): `devices.json`, `appsettings.Development.json`, `simetra-devices.yaml`, `production/configmap.yaml`
- Updated all unit tests (20 changes across 2 test files); 462 tests pass green

## Task Commits

Each task was committed atomically:

1. **Task 1: Create PollMetricOptions and update C# source** - `35cb49b` (feat)
2. **Task 2: Transform all JSON and YAML config files** - `69d8622` (chore)
3. **Task 3: Update unit tests** - `6214df9` (test)

## Files Created/Modified

- `src/SnmpCollector/Configuration/PollMetricOptions.cs` - New wrapper class with MetricName property
- `src/SnmpCollector/Configuration/PollOptions.cs` - MetricNames → Metrics (List<PollMetricOptions>)
- `src/SnmpCollector/Services/DeviceWatcherService.cs` - BuildPollGroups iterates poll.Metrics / m.MetricName
- `src/SnmpCollector/config/devices.json` - 8 poll groups converted to Metrics object array
- `src/SnmpCollector/appsettings.Development.json` - 3 poll groups converted
- `deploy/k8s/snmp-collector/simetra-devices.yaml` - 12 poll groups converted
- `deploy/k8s/production/configmap.yaml` - 10 poll groups converted + comment updated
- `tests/SnmpCollector.Tests/Pipeline/AggregatedMetricModelTests.cs` - 2 JSON literals updated
- `tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs` - ~18 initializers updated

## Decisions Made

- JSON key is `Metrics` (not `MetricNames`) and the object property is `MetricName` — consistent with TenantVector metric slot naming already in the codebase
- `PollMetricOptions` is `sealed class` (not record) to allow future mutable property additions without breaking serialization

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Any future plan adding per-metric configuration (thresholds, display labels, scrape flags) can add properties to `PollMetricOptions` without changing the JSON key or breaking existing config
- Zero remaining `MetricNames` references in source, configs, or tests

---
*Phase: quick-082*
*Completed: 2026-03-20*
