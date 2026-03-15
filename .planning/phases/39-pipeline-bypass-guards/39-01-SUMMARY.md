---
phase: 39-pipeline-bypass-guards
plan: 01
subsystem: pipeline
tags: [csharp, mediatr, pipeline-behavior, snmp, enum, unit-testing, xunit]

# Dependency graph
requires:
  - phase: 38-device-watcher-validation
    provides: DeviceWatcherService combined metric validation and BuildPollGroups logic
provides:
  - SnmpSource.Synthetic enum member enabling synthetic message dispatch
  - OidResolutionBehavior bypass guard preventing MetricName overwrite for synthetic messages
  - Unit tests proving synthetic bypass and sentinel OID passthrough
affects:
  - 40-metric-poll-job-aggregate-dispatch (consumes SnmpSource.Synthetic and bypass guard)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Source-discriminated bypass guard: early-return 'return await next();' inside is-cast block"
    - "Sentinel OID '0.0' passes existing OID regex without ValidationBehavior changes"
    - "Optional source parameter with default preserves backward compatibility in test factories"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/SnmpSource.cs
    - src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/ValidationBehaviorTests.cs

key-decisions:
  - "Option B bypass guard: Source == SnmpSource.Synthetic (consistent, unambiguous, future-safe)"
  - "Sentinel OID '0.0' — passes existing OID regex '^\\d+(\\.\\d+){1,}$' without ValidationBehavior changes"
  - "Bypass uses 'return await next();' (not 'await next(); return;') — method returns Task<TResponse>"
  - "Guard placed inside 'if (notification is SnmpOidReceived msg)' block before _oidMapService.Resolve"

patterns-established:
  - "Synthetic bypass pattern: if (msg.Source == SnmpSource.Synthetic) { return await next(); } at top of cast block"
  - "Test factory optional parameter: MakeNotification(string oid, SnmpSource source = SnmpSource.Poll) — backward compatible"

# Metrics
duration: 2min
completed: 2026-03-15
---

# Phase 39 Plan 01: Pipeline Bypass Guards Summary

**SnmpSource.Synthetic enum member and OidResolutionBehavior bypass guard enabling synthetic message dispatch with pre-set MetricName intact, backed by 4 new unit tests (316 total passing)**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-15T09:35:39Z
- **Completed:** 2026-03-15T09:37:25Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added `SnmpSource.Synthetic` as third enum member alongside Poll and Trap
- Inserted bypass guard in OidResolutionBehavior using `return await next();` — skips `_oidMapService.Resolve` for synthetic messages so pre-set MetricName is preserved
- Added 4 new unit tests: 3 in OidResolutionBehaviorTests (synthetic bypass preserves MetricName, bypass calls next, Poll regression), 1 in ValidationBehaviorTests (sentinel OID "0.0" passes existing regex)

## Task Commits

Each task was committed atomically:

1. **Task 1: Add SnmpSource.Synthetic and OidResolutionBehavior bypass guard** - `cf97676` (feat)
2. **Task 2: Add unit tests for synthetic bypass and sentinel OID** - `b71d930` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/SnmpSource.cs` - Added Synthetic as third enum member
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` - Bypass guard before OID resolution logic
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs` - Extended MakeNotification factory, added 3 new tests
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/ValidationBehaviorTests.cs` - Added sentinel OID "0.0" passthrough test

## Decisions Made

- **Option B bypass guard:** `Source == SnmpSource.Synthetic` (consistent, unambiguous, future-safe) — named per pre-phase decision in STATE.md
- **Sentinel OID "0.0":** passes existing OID regex `^\d+(\.\d+){1,}$` without any changes to ValidationBehavior — regex mathematical proof: `^\d+` matches "0", `(\.\d+){1,}` matches ".0" (one arc, satisfies minimum of 1)
- **Bypass return form:** `return await next();` (not `await next(); return;`) — Handle returns `Task<TResponse>`, result of awaiting `next()` must be returned explicitly
- **Guard placement:** inside `if (notification is SnmpOidReceived msg)` block, as first statement before `_oidMapService.Resolve` — required for type-safe access to `msg.Source`

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 40 (MetricPollJob Aggregate Dispatch) can now publish `SnmpSource.Synthetic` messages through the MediatR pipeline with pre-set MetricName intact
- Synthetic messages must have `DeviceName` set at publish time — ValidationBehavior runs before OidResolutionBehavior (Logging → Exception → Validation → OidResolution order)
- 316 tests passing, zero regressions

---
*Phase: 39-pipeline-bypass-guards*
*Completed: 2026-03-15*
