---
phase: 04-counter-delta-engine
plan: 02
subsystem: pipeline
tags: [snmp, counter-delta, wrap-around, reboot-detection, concurrentdictionary, prometheus]

# Dependency graph
requires:
  - phase: 04-01
    provides: RecordCounter on ISnmpMetricFactory; SysUpTimeCentiseconds on SnmpOidReceived
  - phase: 03-01
    provides: SnmpOidReceived with SnmpType enum; SnmpSource enum
provides:
  - ICounterDeltaEngine interface with RecordDelta(7 params) returning bool
  - CounterDeltaEngine singleton service implementing all 5 delta computation paths
  - Per-OID+agent state tracking via ConcurrentDictionary<string, ulong>
  - Per-device sysUpTime tracking via ConcurrentDictionary<string, uint>
affects:
  - 04-03 (DI registration of CounterDeltaEngine)
  - 04-04 (OtelMetricHandler wiring CounterDeltaEngine into counter dispatch path)
  - 05-polling-engine (CounterDeltaEngine injected into poll result handler)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ConcurrentDictionary.AddOrUpdate for atomic first-poll baseline detection (no race between check and set)"
    - "Five-path counter delta dispatch: normal increment, Counter32 wrap, Counter64 reboot, sysUpTime reboot, first-poll baseline"
    - "Per-device sysUpTime stored separately from per-OID state — one uptime value covers all OIDs for a device"
    - "Math.Max(0.0, delta) clamp as final guard ensuring Prometheus counters never decrease"

key-files:
  created:
    - src/SnmpCollector/Pipeline/CounterDeltaEngine.cs
  modified: []

key-decisions:
  - "Counter32 wrap-around computes delta = (2^32 - previous) + current using Counter32Max = 4_294_967_296UL cast to uint for previous to avoid 64-bit overflow"
  - "Counter64 current < previous always treated as reboot (no wrap detection) — 64-bit counters rolling over in practice would require months at maximum rate"
  - "sysUpTime stored per-device (agent key), not per-OID — one sysUpTime reflects device reboot, not OID-specific state"
  - "AddOrUpdate captures previousValue via closure in updateValueFactory — atomic read+write with no separate lock"
  - "RecordDelta returns bool: false on first-poll baseline, true when delta emitted — caller can distinguish no-op from emission"

patterns-established:
  - "Five-path delta dispatch: all counter scenarios covered with explicit log messages for non-normal paths"
  - "ICounterDeltaEngine + CounterDeltaEngine collocated in same file (interface + implementation) — consistent with ISnmpMetricFactory pattern would be separate but kept together as single deliverable per plan"

# Metrics
duration: 1min
completed: 2026-03-05
---

# Phase 4 Plan 02: CounterDeltaEngine Summary

**ConcurrentDictionary-backed counter delta engine with all five SNMP counter paths: normal increment, Counter32 wrap at 2^32, Counter64 reboot, sysUpTime-based reboot detection, and first-poll baseline skip**

## Performance

- **Duration:** ~1 min
- **Started:** 2026-03-05T03:11:51Z
- **Completed:** 2026-03-05T03:12:37Z
- **Tasks:** 1 of 1
- **Files modified:** 1

## Accomplishments

- ICounterDeltaEngine interface with RecordDelta(oid, agent, source, metricName, typeCode, currentValue, sysUpTimeCentiseconds) returning bool
- CounterDeltaEngine singleton with all 5 delta computation paths fully implemented and logged
- Atomic first-poll baseline via ConcurrentDictionary.AddOrUpdate (no separate check-then-set race window)
- Per-device sysUpTime tracking enabling accurate reboot detection across all OIDs for a device

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ICounterDeltaEngine interface and CounterDeltaEngine implementation** - `d83f1a9` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/CounterDeltaEngine.cs` - ICounterDeltaEngine interface and CounterDeltaEngine sealed class with all 5 delta paths

## Decisions Made

- Counter32 wrap-around casts `previousValue.Value` to `uint` in `(Counter32Max - (uint)previousValue.Value)` to stay within 32-bit arithmetic — prevents 64-bit subtraction from inflating the result when previous is stored as ulong but represents a 32-bit counter
- Counter64 current < previous always treated as reboot (no wrap): 64-bit wrap in practice would require years at maximum SNMPv2c counter increment rates; conservative reboot treatment is correct
- sysUpTime keyed by `agent` not `oid|agent` — a device reboot resets all OID counters simultaneously; per-OID uptime would miss cross-OID reboot correlation
- `AddOrUpdate` closure pattern captures `previousValue` as `ulong?` — null means add path (first poll), non-null means update path (subsequent poll); single atomic operation
- `RecordDelta` returns `bool` to let callers distinguish first-poll no-op from delta emission — useful for logging/metrics at call site

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- CounterDeltaEngine is complete, compilable, and ready for DI registration in 04-03
- OtelMetricHandler (04-04) can inject ICounterDeltaEngine and route Counter32/Counter64 dispatch through RecordDelta
- Unit tests (04-03 or 04-04) should cover all 5 paths with synthetic values: first-poll returns false, normal increment, Counter32 wrap at boundary, Counter64 decrease as reboot, sysUpTime decrease as reboot
- No blockers

---
*Phase: 04-counter-delta-engine*
*Completed: 2026-03-05*
