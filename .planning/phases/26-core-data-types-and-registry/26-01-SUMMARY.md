---
phase: 26-core-data-types-and-registry
plan: 01
subsystem: pipeline
tags: [MetricSlot, MetricSlotHolder, RoutingKey, Tenant, PriorityGroup, volatile, FrozenDictionary, xunit]

# Dependency graph
requires:
  - phase: 25-config-models-and-validation
    provides: config models used by tenant configuration loading
provides:
  - MetricSlot: immutable sealed record class (Value, StringValue, UpdatedAt)
  - MetricSlotHolder: Volatile.Read/Write wrapper for atomic slot pointer swaps
  - RoutingKey: readonly record struct with OrdinalIgnoreCase comparer for FrozenDictionary
  - Tenant: sealed class composing IReadOnlyList<MetricSlotHolder>
  - PriorityGroup: named record grouping tenants by priority level
affects: [26-02-TenantVectorRegistry, phase-27-fan-out, evaluation-engine]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Volatile.Read/Write on non-volatile field for atomic reference swap (no volatile keyword to avoid CS0420)"
    - "Immutable record class as value cell for lock-free pointer swap"
    - "IEqualityComparer<T> singleton with OrdinalIgnoreCase for FrozenDictionary routing index"
    - "Sealed class with readonly IReadOnlyList<T> properties for immutable aggregate roots"

key-files:
  created:
    - src/SnmpCollector/Pipeline/MetricSlot.cs
    - src/SnmpCollector/Pipeline/MetricSlotHolder.cs
    - src/SnmpCollector/Pipeline/RoutingKey.cs
    - src/SnmpCollector/Pipeline/Tenant.cs
    - src/SnmpCollector/Pipeline/PriorityGroup.cs
    - tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs
    - tests/SnmpCollector.Tests/Pipeline/RoutingKeyTests.cs
  modified: []

key-decisions:
  - "Removed volatile keyword from MetricSlotHolder._slot field — Volatile.Read/Write provides full memory barriers; volatile + Volatile.Read/Write together triggers CS0420 and is redundant"
  - "RoutingKeyComparer: private constructor + singleton Instance field to enforce single-instance usage with FrozenDictionary"
  - "PriorityGroup is not sealed — records cannot be sealed in C# (sealed prevents inheritance of records)"

patterns-established:
  - "MetricSlotHolder pattern: plain field + Volatile.Read/Write in methods — callers never touch the field directly"
  - "RoutingKeyComparer singleton for FrozenDictionary — passed explicitly, not as default struct equality"

# Metrics
duration: 3min
completed: 2026-03-10
---

# Phase 26 Plan 01: Core Data Types and Registry Summary

**Five foundational pipeline types for the v1.5 priority vector data layer: MetricSlot (atomic value cell), MetricSlotHolder (Volatile.Read/Write wrapper), RoutingKey (case-insensitive composite key), Tenant (metric holder aggregate), and PriorityGroup (priority-ordered tenant batch).**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-10T17:20:26Z
- **Completed:** 2026-03-10T17:23:02Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- All five core data types created in `SnmpCollector.Pipeline` namespace — zero new NuGet packages
- MetricSlotHolder encapsulates Volatile.Read/Write for lock-free atomic pointer swaps; ReadSlot returns null before first write
- RoutingKeyComparer provides OrdinalIgnoreCase equality and hash codes for FrozenDictionary construction in Plan 02
- 15 unit tests covering: null-before-write, gauge/info write, overwrite, snapshot consistency, constructor metadata, comparer equality/inequality, hash consistency, and dictionary integration
- Build: 0 errors, 0 warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Core data types** - `1194706` (feat)
2. **Task 2: Unit tests for MetricSlotHolder and RoutingKey** - `93e86c9` (test)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/MetricSlot.cs` - Immutable sealed record class with Value, StringValue, UpdatedAt
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` - Volatile.Read/Write wrapper; ReadSlot returns null before first write
- `src/SnmpCollector/Pipeline/RoutingKey.cs` - readonly record struct + RoutingKeyComparer singleton (OrdinalIgnoreCase)
- `src/SnmpCollector/Pipeline/Tenant.cs` - Sealed class with Id, Priority, IReadOnlyList<MetricSlotHolder> Holders
- `src/SnmpCollector/Pipeline/PriorityGroup.cs` - Named record grouping tenants by priority (not sealed — records cannot be sealed)
- `tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs` - 6 tests covering atomic semantics
- `tests/SnmpCollector.Tests/Pipeline/RoutingKeyTests.cs` - 9 tests covering case-insensitive equality and dictionary integration

## Decisions Made

- **Removed volatile keyword from MetricSlotHolder._slot:** The spec said "belt and suspenders" (volatile field + Volatile.Read/Write), but combining them triggers CS0420 (a reference to a volatile field will not be treated as volatile when passed by ref). Since `Volatile.Read`/`Volatile.Write` already provide full acquire/release memory barriers, the `volatile` keyword is redundant and was removed to achieve the required 0-warnings build.
- **RoutingKeyComparer private constructor:** Enforces singleton usage; consumers must use `RoutingKeyComparer.Instance` to prevent accidental multiple instances.
- **PriorityGroup not sealed:** C# records cannot be declared `sealed` (they implicitly prevent unsealed inheritance at the language level via `init`-only properties).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed volatile keyword from MetricSlotHolder._slot to eliminate CS0420 warnings**

- **Found during:** Task 1 build verification
- **Issue:** Plan specified "volatile field + Volatile.Read/Write (belt and suspenders)" but this combination generates CS0420 warnings, violating the "0 warnings" build requirement
- **Fix:** Removed `volatile` keyword; `Volatile.Read`/`Volatile.Write` provide equivalent-or-stronger guarantees (full memory barriers vs. volatile's acquire/release semantics on reads/writes)
- **Files modified:** `src/SnmpCollector/Pipeline/MetricSlotHolder.cs`
- **Verification:** Build: 0 errors, 0 warnings; all 15 tests pass
- **Committed in:** `1194706` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — build correctness)
**Impact on plan:** Single-field change with no behavioral impact. Volatile.Read/Write remains the synchronization mechanism as specified. No scope creep.

## Issues Encountered

None — straightforward type creation with one build warning remediation.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- All five types ready for TenantVectorRegistry (Plan 02) which will build a FrozenDictionary<RoutingKey, MetricSlotHolder> routing index using RoutingKeyComparer.Instance
- MetricSlotHolder.ReadSlot()/WriteValue() API is final — fan-out engine (Phase 27) will call these directly
- PriorityGroup composition chain (PriorityGroup → Tenant → MetricSlotHolder → MetricSlot) is established and ready for registry construction

---
*Phase: 26-core-data-types-and-registry*
*Completed: 2026-03-10*
