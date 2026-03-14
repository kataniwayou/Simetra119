---
phase: 33-config-model-additions
plan: 01
subsystem: config
tags: [csharp, device-config, community-string, snmp, k8s, e2e-fixtures]

# Dependency graph
requires:
  - phase: 32-command-map-infrastructure
    provides: CommandMapService and watcher pattern to follow

provides:
  - DeviceOptions.CommunityString as primary device identifier (Name removed)
  - DeviceInfo.CommunityString non-nullable; Name derived at load time via CommunityStringHelper
  - DeviceRegistry validates + derives Name from CommunityString in constructor and ReloadAsync
  - DevicesOptionsValidator validates CommunityString follows Simetra.{DeviceName} convention
  - MetricPollJob uses device.CommunityString directly with no derivation fallback
  - All config JSON/YAML files updated atomically to new CommunityString format

affects:
  - 33-02 (CommandSlotOptions additions — same phase)
  - 34-validation-and-behavioral-changes
  - 35-tenant-vector-registry-refactor

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "CommunityString as primary device identity: config holds full Simetra.{Name} value, registry derives short name at load time"
    - "DeviceRegistry skip-with-error pattern: invalid CommunityString logs error and continues (no throw)"
    - "Clean break config: no dual-field support, no transition period"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Configuration/DeviceOptions.cs
    - src/SnmpCollector/Pipeline/DeviceInfo.cs
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
    - src/SnmpCollector/Jobs/MetricPollJob.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
    - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
    - src/SnmpCollector/config/devices.json
    - src/SnmpCollector/appsettings.Development.json
    - deploy/k8s/snmp-collector/simetra-devices.yaml
    - deploy/k8s/production/configmap.yaml
    - tests/e2e/fixtures/device-added-configmap.yaml
    - tests/e2e/fixtures/device-removed-configmap.yaml
    - tests/e2e/fixtures/device-modified-interval-configmap.yaml
    - tests/e2e/fixtures/fake-device-configmap.yaml
    - tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml
    - tests/e2e/scenarios/06-poll-unreachable.sh

key-decisions:
  - "CommunityString is required (non-nullable string.Empty default); DeviceRegistry skips invalid entries with LogError rather than throwing"
  - "DeviceInfo.Name remains the short extracted name (e.g. 'NPB-01') for all downstream consumers; no consumer changes needed"
  - "e2e-sim-unmapped-configmap.yaml updated despite not being in plan's file list (found via grep verification)"
  - ".original-devices-configmap.yaml is gitignored (runtime snapshot); updated on disk but not committed"
  - "E2E-SIM-2 device in device-added-configmap.yaml: Name line removed, existing CommunityString kept as-is"

patterns-established:
  - "Device identity: config JSON uses CommunityString field with Simetra.{Name} value; registry derives short name"
  - "Validator + registry both call TryExtractDeviceName: validator rejects at startup, registry skips at reload"

# Metrics
duration: 8min
completed: 2026-03-14
---

# Phase 33 Plan 01: Config Model Additions Summary

**DeviceOptions.Name renamed to CommunityString with Simetra.{Name} format; DeviceInfo.Name derived at load time by DeviceRegistry; all 10 config files updated atomically**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-14T19:39:33Z
- **Completed:** 2026-03-14T19:48:03Z
- **Tasks:** 2
- **Files modified:** 20

## Accomplishments

- Removed `DeviceOptions.Name`; `CommunityString` (non-nullable) is now the sole device identifier in config
- `DeviceInfo.Name` is now always the extracted short name (e.g. `"NPB-01"` from `"Simetra.NPB-01"`) — derived by `DeviceRegistry` in constructor and `ReloadAsync`; all downstream consumers (trap listener, Prometheus labels, job keys) unchanged
- `MetricPollJob` now uses `device.CommunityString` directly with no derivation fallback
- All config files (2 local dev, 2 K8s, 5 e2e fixture YAMLs, 1 e2e script) updated to `"CommunityString": "Simetra.XXX"` format; 251/251 tests pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Rename C# models, update registry, validator, MetricPollJob, and all tests** - `95e28b5` (feat)
2. **Task 2: Update all JSON/YAML config files and e2e scripts** - `07f2f1d` (chore)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/SnmpCollector/Configuration/DeviceOptions.cs` - Name removed, CommunityString is string (non-nullable)
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` - CommunityString non-nullable last positional param
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` - TryExtractDeviceName in constructor + ReloadAsync; skip-with-error on invalid CommunityString
- `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` - Validates CommunityString format
- `src/SnmpCollector/Jobs/MetricPollJob.cs` - Direct `device.CommunityString` use, no fallback
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` - All DeviceOptions updated; new `Constructor_CommunityString_StoredOnDeviceInfo` test
- `tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs` - MakeDevice and tests 3/8 updated
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` - DNS test DeviceInfo updated
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` - StubDeviceRegistry updated
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - DeviceOptions updated
- `src/SnmpCollector/config/devices.json` - OBP-01, NPB-01
- `src/SnmpCollector/appsettings.Development.json` - npb-core-01, obp-edge-01
- `deploy/k8s/snmp-collector/simetra-devices.yaml` - OBP-01, NPB-01, E2E-SIM
- `deploy/k8s/production/configmap.yaml` - OBP-01, NPB-01, E2E-SIM (Lease Name unchanged)
- `tests/e2e/fixtures/device-added-configmap.yaml` - 4 devices, E2E-SIM-2 Name line removed
- `tests/e2e/fixtures/device-removed-configmap.yaml` - OBP-01, NPB-01, FAKE-UNREACHABLE
- `tests/e2e/fixtures/device-modified-interval-configmap.yaml` - 4 devices
- `tests/e2e/fixtures/fake-device-configmap.yaml` - 4 devices
- `tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml` - OBP-01, NPB-01, E2E-SIM
- `tests/e2e/scenarios/06-poll-unreachable.sh` - Inline FAKE-UNREACHABLE JSON

## Decisions Made

- **DeviceRegistry skip-on-invalid vs. throw:** When CommunityString doesn't follow `Simetra.{Name}`, registry logs Error and skips device (doesn't throw). This is consistent with the reload path which can't throw.
- **E2E-SIM-2 special case:** The `device-added-configmap.yaml` already had `CommunityString: "Simetra.E2E-SIM"` alongside `Name: "E2E-SIM-2"`. Removed the Name line, kept CommunityString.
- **Additional test files discovered:** `PipelineIntegrationTests.cs`, `TenantVectorFanOutBehaviorTests.cs`, and `DynamicPollSchedulerTests.cs` (already had 6-param constructor) were updated — they were not listed in the plan but were found by build errors and grep.
- **e2e-sim-unmapped-configmap.yaml:** Not in the plan's file list but discovered by grep scan; updated to prevent operator confusion.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] PipelineIntegrationTests.cs used DeviceOptions.Name**
- **Found during:** Task 1 (build verification)
- **Issue:** `PipelineIntegrationTests.cs:60` still used `Name = KnownDevice` — compiler caught it
- **Fix:** Changed to `CommunityString = $"Simetra.{KnownDevice}"`
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs`
- **Verification:** Build succeeds
- **Committed in:** `95e28b5` (Task 1 commit)

**2. [Rule 1 - Bug] TenantVectorFanOutBehaviorTests.cs missing CommunityString**
- **Found during:** Task 1 (grep scan for all DeviceInfo construction sites)
- **Issue:** `StubDeviceRegistry` in the test constructed `DeviceInfo` with only 5 params — would fail to compile after making CommunityString required
- **Fix:** Added `$"Simetra.{name}"` as last argument
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs`
- **Verification:** Build succeeds, 251 tests pass
- **Committed in:** `95e28b5` (Task 1 commit)

**3. [Rule 2 - Missing Critical] e2e-sim-unmapped-configmap.yaml not in plan**
- **Found during:** Task 2 (grep verification scan)
- **Issue:** File was not in the plan's file list but had `"Name":` device entries that would cause runtime errors
- **Fix:** Updated 3 device entries to `"CommunityString": "Simetra.XXX"` format
- **Files modified:** `tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml`
- **Verification:** Zero remaining `"Name":` in device JSON/YAML contexts
- **Committed in:** `07f2f1d` (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (2 Rule 1 bugs, 1 Rule 2 missing critical)
**Impact on plan:** All auto-fixes necessary for correctness. No scope creep.

## Issues Encountered

None - all deviations were caught early by compiler errors and grep verification.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 33-02 (CommandSlotOptions data model) can start immediately — DeviceOptions is clean
- Phase 34 (validation and behavioral changes) has the CommunityString foundation it needs
- All downstream consumers (trap listener, Prometheus labels, job keys) continue using the extracted short name — no changes needed there

---
*Phase: 33-config-model-additions*
*Completed: 2026-03-14*
