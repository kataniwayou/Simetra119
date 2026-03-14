---
phase: 33-config-model-additions
plan: 02
subsystem: config
tags: [csharp, config-models, tenant-vector, snmp, command-slot, interval-seconds]

# Dependency graph
requires:
  - phase: 33-01
    provides: DeviceOptions.CommunityString foundation; TenantVectorRegistryTests DNS test updated
  - phase: 32-command-map-infrastructure
    provides: CommandMapService pattern (CommandSlotOptions follows same shape)

provides:
  - CommandSlotOptions sealed class with Ip, Port, CommandName, Value, ValueType
  - TenantOptions.Name (string?) for human-readable tenant identifier
  - TenantOptions.Commands (List<CommandSlotOptions>) for SNMP SET targets
  - MetricSlotOptions.IntervalSeconds (int, default 0) for observability
  - MetricSlotOptions.Role (string) for Evaluate/Resolved slot classification
  - TenantVectorRegistry constructor takes only IDeviceRegistry + ILogger (IOidMapService removed)
  - DeriveIntervalSeconds method deleted; IntervalSeconds read directly from MetricSlotOptions
  - Reload uses tenantOpts.Name for tenant ID when present
  - ServiceCollectionExtensions DI factory updated to 2-arg constructor
  - 3 new unit tests covering Name and IntervalSeconds behavior

affects:
  - 34-validation-and-behavioral-changes (validates Role, CommandSlotOptions.ValueType at load time)
  - 35-tenant-vector-registry-refactor (registry now decoupled from IOidMapService)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "IntervalSeconds directly in config: MetricSlotOptions.IntervalSeconds replaces DeriveIntervalSeconds cross-service derivation"
    - "TenantOptions.Name optional override: empty/null falls back to auto-generated tenant-{i} id"
    - "CommandSlotOptions shape: mirrors MetricSlotOptions pattern (sealed, mutable {get;set;} with defaults)"

key-files:
  created:
    - src/SnmpCollector/Configuration/CommandSlotOptions.cs
  modified:
    - src/SnmpCollector/Configuration/TenantOptions.cs
    - src/SnmpCollector/Configuration/MetricSlotOptions.cs
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs

key-decisions:
  - "IOidMapService removed from TenantVectorRegistry: interval is now a direct config field; no cross-service lookup at reload time"
  - "Role property added to MetricSlotOptions alongside IntervalSeconds per user instruction (must_haves requirement)"
  - "DeriveIntervalSeconds deleted entirely: no migration path; callers must set IntervalSeconds in config"

patterns-established:
  - "Config-first interval: IntervalSeconds lives in MetricSlotOptions, not derived from device poll groups"
  - "Optional tenant name: TenantOptions.Name overrides auto-generated ID; empty/whitespace falls back"

# Metrics
duration: 4min
completed: 2026-03-14
---

# Phase 33 Plan 02: Config Model Additions Summary

**CommandSlotOptions + TenantOptions.Name/Commands + MetricSlotOptions.IntervalSeconds/Role added; IOidMapService dependency removed from TenantVectorRegistry with DeriveIntervalSeconds deleted**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-14T19:51:01Z
- **Completed:** 2026-03-14T19:55:29Z
- **Tasks:** 2
- **Files modified:** 7 (1 created, 6 modified)

## Accomplishments

- Created `CommandSlotOptions` sealed class with Ip, Port, CommandName, Value, ValueType — ready for Phase 34 validation and future SET execution
- Added `TenantOptions.Name` (optional) and `TenantOptions.Commands` list; `MetricSlotOptions.IntervalSeconds` and `MetricSlotOptions.Role` for v1.7 self-describing tenant entries
- Removed `IOidMapService` from `TenantVectorRegistry` constructor and deleted `DeriveIntervalSeconds` — interval now flows directly from config; all 254 tests pass including 3 new tests for Name and IntervalSeconds behavior

## Task Commits

Each task was committed atomically:

1. **Task 1: Create CommandSlotOptions, add fields to TenantOptions and MetricSlotOptions** - `e9a5a8a` (feat)
2. **Task 2: Remove IOidMapService from TenantVectorRegistry, update DI and tests** - `8fa0399` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` - New sealed class; Ip, Port, CommandName, Value, ValueType
- `src/SnmpCollector/Configuration/TenantOptions.cs` - Added Name (string?) and Commands (List<CommandSlotOptions>)
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` - Added IntervalSeconds (int) and Role (string)
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` - Removed IOidMapService field + constructor param; deleted DeriveIntervalSeconds; added Name-based tenantId; uses metric.IntervalSeconds directly
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - TenantVectorRegistry factory updated to 2-arg constructor
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` - CreateRegistry() and 2 explicit constructors updated; 3 new tests added
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` - 4 TenantVectorRegistry constructors updated (blocking fix)
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - 2 TenantVectorRegistry DI factory lambdas updated (blocking fix)

## Decisions Made

- **IOidMapService removed from TenantVectorRegistry:** DeriveIntervalSeconds was deriving interval by cross-referencing the OID map at reload time. Since IntervalSeconds is now a direct config field (MetricSlotOptions), the cross-service coupling is unnecessary. Clean break — no migration path needed.
- **Role added to MetricSlotOptions:** Per plan must_haves, Role (string) is required alongside IntervalSeconds. Defaults to empty string; validated in Phase 34.
- **DeriveIntervalSeconds deleted entirely:** No callers remain after this change. Deletion enforces the new contract that config must supply IntervalSeconds explicitly.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] TenantVectorFanOutBehaviorTests.cs had 4 TenantVectorRegistry 3-arg constructors**
- **Found during:** Task 2 (test build verification)
- **Issue:** `TenantVectorFanOutBehaviorTests.cs` at lines 203, 307, 330, 361 still used the old 3-arg constructor `(IDeviceRegistry, IOidMapService, ILogger)` — compiler error
- **Fix:** Removed `NSubstitute.Substitute.For<IOidMapService>()` argument from all 4 constructors
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs`
- **Verification:** Build succeeds, 254 tests pass
- **Committed in:** `8fa0399` (Task 2 commit)

**2. [Rule 1 - Bug] PipelineIntegrationTests.cs had 2 TenantVectorRegistry 3-arg DI factory lambdas**
- **Found during:** Task 2 (test build verification)
- **Issue:** `PipelineIntegrationTests.cs` at lines 78 and 191 used `new TenantVectorRegistry(IDeviceRegistry, IOidMapService, ILogger)` in DI factory lambdas — compiler error
- **Fix:** Removed `sp.GetRequiredService<IOidMapService>()` argument from both lambdas
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs`
- **Verification:** Build succeeds, 254 tests pass
- **Committed in:** `8fa0399` (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 Rule 1 bugs)
**Impact on plan:** Both auto-fixes necessary for compilation. No scope creep — these were additional callsites of the constructor being refactored.

## Issues Encountered

None - both deviations were caught immediately by the compiler on the first test build attempt.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 34 (validation and behavioral changes) has CommandSlotOptions, IntervalSeconds, and Role fields to validate
- Phase 35 (TenantVectorRegistry refactor) has the IOidMapService dependency already removed
- All 254 tests pass; no blockers

---
*Phase: 33-config-model-additions*
*Completed: 2026-03-14*
