---
phase: 26-core-data-types-and-registry
plan: 02
subsystem: pipeline
tags: [TenantVectorRegistry, ITenantVectorRegistry, FrozenDictionary, volatile, PriorityGroup, RoutingKey, DI, xunit]

# Dependency graph
requires:
  - phase: 26-01
    provides: MetricSlotHolder, RoutingKey, RoutingKeyComparer, Tenant, PriorityGroup — all composed by TenantVectorRegistry
  - phase: 25-config-models-and-validation
    provides: TenantVectorOptions, TenantOptions, MetricSlotOptions — the config model Reload() reads
provides:
  - ITenantVectorRegistry: interface for testability and DI aliasing
  - TenantVectorRegistry: singleton registry with volatile-swapped FrozenDictionary routing index
  - Reload(TenantVectorOptions): atomic rebuild with value carry-over and structured diff logging
  - DI registration: concrete singleton + interface alias following OidMapService pattern
affects: [phase-27-fan-out, evaluation-engine, 26-03, pipeline-integration]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "volatile field swap for FrozenDictionary atomic reference replace without locks"
    - "SortedDictionary<int, List<Tenant>> for ascending priority bucket build"
    - "StringTupleComparer nested class for OrdinalIgnoreCase (tenantId, ip, port, metricName) carry-over lookup"
    - "Concrete-first DI singleton + interface alias (sp => sp.GetRequiredService<Concrete>()) for same instance"

key-files:
  created:
    - src/SnmpCollector/Pipeline/ITenantVectorRegistry.cs
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "volatile keyword on _groups and _routingIndex fields — unlike MetricSlotHolder._slot (which uses Volatile.Read/Write API on plain field), the registry fields hold reference types that readers access without passing by ref, so the volatile keyword is appropriate here and does not trigger CS0420"
  - "StringTupleComparer nested inside TenantVectorRegistry — only used by Reload(), no external consumers needed"
  - "Value carry-over reads the old MetricSlot and writes it to the new holder, never copying the holder object itself — ensures clean slate per Reload cycle"

patterns-established:
  - "TenantVectorRegistry.Reload pattern: capture old state, build new, carry over values, volatile swap — used for all future registry rebuilds"
  - "CapturingLogger test pattern: ILogger<T> implementation capturing formatted messages for log assertion tests"

# Metrics
duration: 3min
completed: 2026-03-10
---

# Phase 26 Plan 02: TenantVectorRegistry Summary

**TenantVectorRegistry singleton with FrozenDictionary routing index, volatile-swap Reload(), value carry-over by (tenantId, ip, port, metricName), structured diff logging, DI registration, and 15 unit tests.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-10T17:27:34Z
- **Completed:** 2026-03-10T17:31:06Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- ITenantVectorRegistry interface and TenantVectorRegistry implementation with two volatile reference fields for lock-free atomic snapshot swap
- Reload() carries over existing MetricSlot values via ReadSlot()/WriteValue() for all metrics matching by (tenantId, ip, port, metricName) OrdinalIgnoreCase; new metrics start with null slot
- Reload() logs structured diff: tenants added/removed/unchanged, total slots, carry-over count
- DI registered as concrete singleton first, then interface alias second — same instance pattern from OidMapService
- 15 unit tests covering all specified behaviors: pass rate 15/15

## Task Commits

Each task was committed atomically:

1. **Task 1: ITenantVectorRegistry interface and TenantVectorRegistry implementation** - `0cf1b5b` (feat)
2. **Task 2: DI registration and comprehensive unit tests** - `53ee287` (feat)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/ITenantVectorRegistry.cs` - Interface with Groups, TenantCount, SlotCount, TryRoute
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` - Singleton with volatile _groups/_routingIndex, Reload() with carry-over and diff logging, nested StringTupleComparer
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Added Phase 26 DI block: concrete + interface alias after TenantVectorOptionsValidator
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` - 15 unit tests with CreateRegistry/CreateOptions helpers and CapturingLogger

## Decisions Made

- **volatile keyword appropriate here:** Unlike MetricSlotHolder._slot (plain field passed by ref to Volatile.Read/Write API), the registry's _groups and _routingIndex fields are read directly as reference values — `volatile` provides the acquire semantics without the ref-passing that triggers CS0420.
- **StringTupleComparer nested inside TenantVectorRegistry:** Only used by Reload()'s carry-over dictionary; no external consumer ever needs it.
- **Value carry-over copies the slot value, not the holder:** `oldHolder.ReadSlot()` + `newHolder.WriteValue(slot.Value, slot.StringValue)` — each Reload creates fresh holders for a clean state while preserving the last observed metric value.

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

Pre-existing test failures (`ObpOidMapHas24Entries`, `ObpOidNamingConventionIsConsistent`) exist in the repository before this plan's changes — caused by unrelated `src/SnmpCollector/config/devices.json` modifications in the working tree. These 2 failures are not regressions from Plan 02 work.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- TenantVectorRegistry is ready for Phase 27 fan-out: `TryRoute(ip, port, metricName)` returns the holder list for each routing key; fan-out iterates `Groups` in priority order
- `Reload(TenantVectorOptions)` is the public API for config watchers (DeviceWatcherService, ConfigMap reload path) to call when tenant config changes
- All 26 core data types and registry foundation complete; Phase 27 can consume ITenantVectorRegistry directly via DI

---
*Phase: 26-core-data-types-and-registry*
*Completed: 2026-03-10*
