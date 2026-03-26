---
phase: quick-094
plan: 01
subsystem: infra
tags: [quartz, k8s, preferred-leader, lease, di]

# Dependency graph
requires:
  - phase: phase-85-86
    provides: PreferredHeartbeatJob and PreferredHeartbeatJobOptions that were always registered in K8s
provides:
  - PreferredHeartbeatJob Quartz registration gated on LeaseOptions.PreferredNode being non-empty
  - PreferredHeartbeatJobOptions ValidateOnStart gated on same condition
  - preferredFeatureActive flag in AddSnmpScheduling combining IsInCluster + PreferredNode checks
affects: [future-preferred-leader-changes, ServiceCollectionExtensions]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "GetValue<string> from config section to read a single optional field without binding a full required-properties POCO"
    - "preferredFeatureActive bool combining environment check + config check, used to gate both count and registration"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "Use GetValue<string>(nameof(LeaseOptions.PreferredNode)) rather than binding full LeaseOptions — avoids throwing on required properties when section absent"
  - "Single preferredFeatureActive bool in AddSnmpScheduling covers both initialJobCount increment and Quartz registration block"
  - "preferredHbOptions binding moved inside the gate — no reason to read options that will never be used"

patterns-established:
  - "Feature-flag pattern: read single nullable config value via GetValue<string>, combine with environment check into a bool, gate all feature registrations on that bool"

# Metrics
duration: 2min
completed: 2026-03-26
---

# Quick Task 094: Skip Heartbeat Job When Preferred Node Empty — Summary

**PreferredHeartbeatJob and its options validation now only register when LeaseOptions.PreferredNode is non-empty and running in K8s, eliminating noisy 404s from the K8s lease API when the preferred-leader feature is off.**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-26T13:21:50Z
- **Completed:** 2026-03-26T13:23:11Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- `AddSnmpConfiguration`: `PreferredHeartbeatJobOptions` `ValidateOnStart` block wrapped in `IsInCluster() && !IsNullOrWhiteSpace(preferredNode)` guard
- `AddSnmpScheduling`: `preferredFeatureActive` bool introduced before `initialJobCount`; both the count increment and the Quartz registration block now use this flag instead of bare `IsInCluster()`
- `preferredHbOptions` binding moved inside the gate — options only read when the job will actually be registered
- Build clean (0 warnings), 524/524 tests pass

## Task Commits

1. **Task 1: Gate PreferredHeartbeatJob registration and options validation on PreferredNode** - `14b1f08` (feat)

## Files Created/Modified

- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Three targeted changes: gate in AddSnmpConfiguration, preferredFeatureActive variable + gate in AddSnmpScheduling

## Decisions Made

- Used `GetValue<string>(nameof(LeaseOptions.PreferredNode))` rather than binding a full `LeaseOptions` object in both methods — `LeaseOptions` has `required` properties (`Name`, `Namespace`) that would throw a binding exception when the section is absent or incomplete. Reading just the one optional string is safe.
- Named the variable `preferredFeatureActive` rather than inlining the expression — makes both usage sites (initialJobCount and Quartz block) self-documenting.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- When `PreferredNode` is absent from config (default for most deployments), PreferredHeartbeatJob and its validation are entirely skipped — no startup error, no runtime 404s.
- When `PreferredNode` is set, the existing path is unchanged and the job registers as before.

---
*Phase: quick-094*
*Completed: 2026-03-26*
