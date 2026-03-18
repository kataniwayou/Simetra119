---
phase: 57-deterministic-watcher-startup-order
plan: 01
subsystem: infra
tags: [backgroundservice, configmap, watcher, startup, kubernetes]

# Dependency graph
requires:
  - phase: none
    provides: existing watcher services with inline initial-load
provides:
  - public InitialLoadAsync on all 4 ConfigMap watchers
  - watch-loop-only ExecuteAsync (no initial-load try/catch)
affects: [57-02 (Program.cs sequential startup calls)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "InitialLoadAsync/ExecuteAsync split: initial config load separated from watch loop for deterministic startup ordering"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Services/OidMapWatcherService.cs
    - src/SnmpCollector/Services/DeviceWatcherService.cs
    - src/SnmpCollector/Services/CommandMapWatcherService.cs
    - src/SnmpCollector/Services/TenantVectorWatcherService.cs

key-decisions:
  - "InitialLoadAsync returns Task<int> with entry count for caller diagnostics"

patterns-established:
  - "InitialLoadAsync pattern: public method calls private LoadFromConfigMapAsync, logs count, returns count, no exception handling (crash-the-pod)"

# Metrics
duration: 5min
completed: 2026-03-18
---

# Phase 57 Plan 01: Extract InitialLoadAsync Summary

**Public InitialLoadAsync on all 4 ConfigMap watchers with crash-the-pod semantics, ExecuteAsync reduced to watch-loop only**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-18T15:33:19Z
- **Completed:** 2026-03-18T15:38:00Z
- **Tasks:** 1
- **Files modified:** 4

## Accomplishments
- Extracted InitialLoadAsync from OidMapWatcherService, DeviceWatcherService, CommandMapWatcherService, TenantVectorWatcherService
- Each InitialLoadAsync returns entry count (OID entries, device count, command entries, tenant count)
- Removed initial-load try/catch from all 4 ExecuteAsync methods -- now watch-loop only
- All 453 existing tests pass unchanged

## Task Commits

Each task was committed atomically:

1. **Task 1: Extract InitialLoadAsync from all 4 watchers** - `346e384` (feat)

## Files Created/Modified
- `src/SnmpCollector/Services/OidMapWatcherService.cs` - Added InitialLoadAsync returning _oidMapService.EntryCount
- `src/SnmpCollector/Services/DeviceWatcherService.cs` - Added InitialLoadAsync returning _deviceRegistry.AllDevices.Count
- `src/SnmpCollector/Services/CommandMapWatcherService.cs` - Added InitialLoadAsync returning _commandMapService.Count
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` - Added InitialLoadAsync returning _registry.TenantCount

## Decisions Made
None - followed plan as specified.

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All 4 watchers expose public InitialLoadAsync, ready for Plan 02 to wire sequential calls in Program.cs
- No blockers

---
*Phase: 57-deterministic-watcher-startup-order*
*Completed: 2026-03-18*
