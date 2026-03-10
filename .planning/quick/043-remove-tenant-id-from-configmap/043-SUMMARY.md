# Quick Task 043: Remove Tenant Id from ConfigMap Summary

**One-liner:** Removed operator-facing Id from TenantOptions; registry auto-generates positional tenant-{index} Ids at Reload() time

## What Was Done

### Task 1: Remove Id from TenantOptions, validator, and TenantVectorRegistry
- **Commit:** `e2a0df7`
- Removed `Id` property from `TenantOptions` config model
- Removed Id validation rules (required + duplicate) from `TenantVectorOptionsValidator`
- Changed `TenantVectorRegistry.Reload()` to auto-generate `tenant-{i}` Ids via for-loop index
- Simplified carry-over key from 4-tuple `(tenantId, ip, port, metricName)` to 3-tuple via `RoutingKey`
- Replaced named diff logging (added/removed/unchanged tenant names) with count-based log (`tenants=, slots=, carried_over=`)
- Deleted `StringTupleComparer` nested class (replaced by existing `RoutingKey` + `RoutingKeyComparer`)

### Task 2: Update all config files, tests, and e2e scenarios
- **Commit:** `21b4017`
- Stripped `"Id"` field from `tenantvector.json`, dev ConfigMap, and prod ConfigMap
- Removed prod ConfigMap header comment line documenting Id field
- Deleted 4 validator tests: EmptyTenantId, WhitespaceTenantId, DuplicateTenantIds, DuplicateTenantIdsCaseInsensitive
- Updated `MultipleErrorsCollected` test to remove Id="" error expectation
- Refactored `TenantVectorRegistryTests.CreateOptions` helper from string tenantId to integer tenantIndex grouping
- Updated all `CreateOptions` call sites (tenant-a/b/c -> 0/1/2)
- Updated tenant Id assertion from `"tenant-a"` to `"tenant-0"`
- Added `carried_over=` assertion to diff logging test
- Removed `Id` from all 3 `TenantVectorFanOutBehaviorTests` factory helpers
- Updated e2e scenario 28 inline JSON (both 3-tenant and 4-tenant blocks) to remove Id fields
- Changed 28d hot-reload verification from name-based (`"added"` + `"obp-poll-2"`) to count-based (`"reloaded"` + `"tenants=4"`)

## Deviations from Plan

None - plan executed exactly as written.

## Verification

- `dotnet build` compiles clean with 0 warnings, 0 errors
- All 201 unit tests pass (4 fewer than before: removed Id validation tests)
- No `"Id"` references in any tenant config file
- No `tenant.Id`, `tenantOpts.Id`, or `StringTupleComparer` references in source

## Decisions Made

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Carry-over simplified to (ip, port, metricName) 3-tuple | Multiple tenants with same key get any old holder's value (all equivalent from fan-out) |
| 2 | Count-based diff log replaces named tenant diff | Positional tenant-{i} Ids are meaningless for diff logging |

## Files Modified

- `src/SnmpCollector/Configuration/TenantOptions.cs` - Removed Id property
- `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` - Removed Id rules
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` - Auto-gen Ids, simplified carry-over, count-based log
- `src/SnmpCollector/config/tenantvector.json` - Removed Id fields
- `deploy/k8s/production/configmap.yaml` - Removed Id fields and header comment
- `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` - Removed Id fields
- `tests/e2e/scenarios/28-tenantvector-routing.sh` - Removed Id fields, count-based verification
- `tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs` - Removed Id tests and data
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` - Refactored to integer index helper
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` - Removed Id from factories
