---
phase: 49-observability-and-dashboard
plan: 01
subsystem: observability
tags: [stopwatch, logging, grafana, dashboard, prometheus, snmp-set]

# Dependency graph
requires:
  - phase: 47-command-channel-and-worker
    provides: CommandWorkerService with SET execution
  - phase: 46-03
    provides: PipelineMetricService with snmp.command.sent/failed/suppressed counters
provides:
  - Stopwatch-based duration logging on SET success and failure
  - 3 command panels in operations dashboard (sent, failed, suppressed)
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Stopwatch around SetAsync for round-trip duration (mirrors MetricPollJob pattern)"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Services/CommandWorkerService.cs
    - tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs
    - deploy/grafana/dashboards/simetra-operations.json

key-decisions:
  - "Duration log uses Elapsed.TotalMilliseconds with F1 format for sub-ms precision"
  - "Stopwatch not in scope for outer catch (pre-SET failures) — intentional, no duration for OID/device resolution failures"

patterns-established:
  - "Stopwatch duration logging pattern: start before try, stop in try body + each catch"

# Metrics
duration: 4min
completed: 2026-03-16
---

# Phase 49 Plan 01: Command Observability Summary

**Stopwatch-based SET duration logging in CommandWorkerService + 3 snmp.command.* Grafana panels for sent/failed/suppressed rates**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-16T14:43:55Z
- **Completed:** 2026-03-16T14:47:44Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- CommandWorkerService logs Information with command name, device name, and round-trip ms on SET success
- CommandWorkerService logs Warning with command name, device name, and round-trip ms on SET timeout
- Operations dashboard has 3 command panels (Command Sent, Command Failed, Command Suppressed) at y=39 with rate() PromQL queries
- .NET Runtime row and panels shifted down by 8 units to accommodate new command row

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Stopwatch duration logging to CommandWorkerService** - `1e65495` (feat)
2. **Task 2: Add 3 snmp.command.* panels to operations dashboard** - `d4cb03e` (feat)

## Files Created/Modified
- `src/SnmpCollector/Services/CommandWorkerService.cs` - Added System.Diagnostics import, Stopwatch around SetAsync, Information log on success, Warning log with duration on timeout
- `tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs` - Added CapturingLogger, 2 new tests for duration logging (success + failure), ILogger parameter on CreateService helper
- `deploy/grafana/dashboards/simetra-operations.json` - 3 new command panels (ids 24-26) at y=39, .NET Runtime row shifted to y=47

## Decisions Made
None - followed plan as specified.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed test assertion matching "completed" too broadly**
- **Found during:** Task 1 (duration logging tests)
- **Issue:** Test for success log filtered on "completed" which also matched "Command channel worker completed" info log
- **Fix:** Changed filter to "completed for" to match only the SET success log
- **Files modified:** tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs
- **Verification:** Test passes, single log entry matched
- **Committed in:** 1e65495 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Minor test assertion refinement. No scope creep.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Command observability complete (SNAP-16 + SNAP-17)
- Ready for remaining 49-* plans

---
*Phase: 49-observability-and-dashboard*
*Completed: 2026-03-16*
