---
phase: 10-metrics
plan: 08
subsystem: configuration
tags: [snmp, community-string, config-cleanup, gap-closure]

# Dependency graph
requires:
  - phase: 10-metrics (plan 07)
    provides: per-device Port and CommunityString in DeviceOptions/DeviceInfo
  - phase: 05-trap-ingestion
    provides: CommunityStringHelper with DeriveFromDeviceName
provides:
  - CommunityString removed from DeviceOptions, DeviceInfo, DeviceRegistry, DevicesOptionsValidator
  - MetricPollJob derives community at runtime via CommunityStringHelper.DeriveFromDeviceName
  - Config files cleaned of per-device CommunityString
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Convention-over-configuration: community string derived from device name, not stored separately"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Configuration/DeviceOptions.cs
    - src/SnmpCollector/Pipeline/DeviceInfo.cs
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
    - src/SnmpCollector/Jobs/MetricPollJob.cs
    - src/SnmpCollector/appsettings.Development.json
    - deploy/k8s/snmp-collector/configmap.yaml
    - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs

key-decisions:
  - "CommunityString property fully removed from config model -- convention Simetra.{Name} enforced at runtime by CommunityStringHelper"
  - "MetricPollJob change bundled with Task 1 (model removal) to keep build green between commits"

patterns-established:
  - "Convention-derived fields: if a value is always Prefix.{Name}, store Name only and derive at runtime"

# Metrics
duration: 4min
completed: 2026-03-06
---

# Phase 10 Plan 08: Remove Redundant Per-Device CommunityString Summary

**Eliminated redundant CommunityString from device config -- community derived at runtime via CommunityStringHelper.DeriveFromDeviceName(device.Name)**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-06T19:54:27Z
- **Completed:** 2026-03-06T19:58:38Z
- **Tasks:** 2
- **Files modified:** 10

## Accomplishments
- Removed CommunityString property from DeviceOptions, DeviceInfo record, and DeviceRegistry constructor
- Removed CommunityString validation rules from DevicesOptionsValidator (convention is now implicit)
- MetricPollJob derives community string at runtime via CommunityStringHelper.DeriveFromDeviceName
- Cleaned all config files (appsettings.Development.json, K8s configmap) of per-device CommunityString
- All 115 tests pass with updated DeviceInfo constructors

## Task Commits

Each task was committed atomically:

1. **Task 1: Remove CommunityString from config model, runtime record, registry, validator, and MetricPollJob** - `aab5878` (refactor)
2. **Task 2: Update tests and config files** - `7387d68` (test)

## Files Created/Modified
- `src/SnmpCollector/Configuration/DeviceOptions.cs` - Removed CommunityString property
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` - Removed CommunityString from record parameters
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` - Removed d.CommunityString from DeviceInfo constructor call
- `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` - Removed CommunityString validation block
- `src/SnmpCollector/Jobs/MetricPollJob.cs` - Changed to CommunityStringHelper.DeriveFromDeviceName(device.Name)
- `src/SnmpCollector/appsettings.Development.json` - Removed per-device CommunityString entries
- `deploy/k8s/snmp-collector/configmap.yaml` - Removed CommunityString from dummy device
- `tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs` - Updated DeviceInfo constructors, renamed test
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` - Removed CommunityString from test DeviceOptions
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - Removed CommunityString from test DeviceOptions

## Decisions Made
- MetricPollJob source change bundled into Task 1 commit (Rule 3 - Blocking): removing CommunityString from DeviceInfo would break the build if MetricPollJob was not updated simultaneously
- CommunityStringHelper.cs left untouched -- still needed for trap listener path (TryExtractDeviceName)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] MetricPollJob update moved from Task 2 to Task 1**
- **Found during:** Task 1 verification (dotnet build)
- **Issue:** Removing CommunityString from DeviceInfo broke MetricPollJob.cs build (CS1061)
- **Fix:** Applied MetricPollJob source change in Task 1 to keep build green
- **Files modified:** src/SnmpCollector/Jobs/MetricPollJob.cs
- **Verification:** dotnet build succeeds with 0 errors
- **Committed in:** aab5878 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Task ordering adjusted to maintain green build between commits. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- This is the final gap closure plan (plan 8 of 8) for Phase 10
- All CommunityString references now limited to CommunityStringHelper.cs and trap listener path
- Phase 10 (Metrics Redesign) is complete

---
*Phase: 10-metrics*
*Completed: 2026-03-06*
