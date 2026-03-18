---
phase: 57-deterministic-watcher-startup-order
plan: 02
subsystem: infra
tags: [startup, program-cs, watcher, kubernetes, local-dev, deterministic-ordering]

# Dependency graph
requires:
  - phase: 57-01
    provides: public InitialLoadAsync on all 4 ConfigMap watchers
provides:
  - deterministic watcher startup order in K8s mode (OidMap -> Devices -> CommandMap -> Tenants)
  - fixed local-dev config load order (command map before tenants)
  - operator-visible startup sequence summary log with timing
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pre-host sequential InitialLoadAsync: resolve watcher services from DI, call InitialLoadAsync in dependency order before app.RunAsync"
    - "Startup timing pattern: shared Stopwatch.Restart per step, totalSw for overall, structured log with named parameters"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Program.cs

key-decisions:
  - "CancellationToken.None for InitialLoadAsync since host stoppingToken not yet available"
  - "No try/catch around InitialLoadAsync -- crash-the-pod semantics for K8s restart"
  - "Added comment explaining why command map must load before tenants in local-dev path"

patterns-established:
  - "K8s startup block pattern: resolve watcher via GetRequiredService, await InitialLoadAsync, capture timing, log summary"

# Metrics
duration: 2min
completed: 2026-03-18
---

# Phase 57 Plan 02: Wire Sequential Startup in Program.cs Summary

**Deterministic watcher startup order in Program.cs -- K8s sequential InitialLoadAsync with timing log, local-dev command-map-before-tenants fix**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-18T15:36:17Z
- **Completed:** 2026-03-18T15:38:09Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments
- K8s startup block calls InitialLoadAsync on all 4 watchers sequentially with per-watcher Stopwatch timing
- Summary INFO log shows all 4 watcher counts and elapsed times for operator visibility
- Fixed local-dev load order: command map now loads before tenants (was: tenants before command map)
- All 453 existing tests pass unchanged

## Task Commits

Each task was committed atomically:

1. **Task 1: Add K8s startup sequence in Program.cs** - `4d9f7a1` (feat)
2. **Task 2: Fix local-dev load order in Program.cs** - `594e63d` (fix)

## Files Created/Modified
- `src/SnmpCollector/Program.cs` - K8s startup block with sequential InitialLoadAsync calls and timing; local-dev block reordered (command map before tenants); added using directives for Services and Logging

## Decisions Made
- CancellationToken.None passed to InitialLoadAsync since host stoppingToken is not available pre-RunAsync
- No try/catch wrapping -- exceptions crash the pod for K8s restart (per CONTEXT.md decision)
- Added explanatory comment on command map ordering in local-dev block for future maintainability

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added missing using Microsoft.Extensions.Logging**
- **Found during:** Task 1 (K8s startup sequence)
- **Issue:** LogInformation extension method not found -- ILogger<Program> requires Microsoft.Extensions.Logging using directive
- **Fix:** Added `using Microsoft.Extensions.Logging;` to Program.cs imports
- **Files modified:** src/SnmpCollector/Program.cs
- **Verification:** Build succeeded after adding using directive
- **Committed in:** 4d9f7a1 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Trivial missing import. No scope creep.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 57 complete: all 4 watchers load deterministically in both K8s and local-dev modes
- No blockers

---
*Phase: 57-deterministic-watcher-startup-order*
*Completed: 2026-03-18*
