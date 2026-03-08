---
phase: 16-test-k8s-configmap-watchers
plan: 01
subsystem: testing
tags: [kubernetes, configmap, watch-api, hot-reload, oidmap, prometheus]

# Dependency graph
requires:
  - phase: 17-split-configmap-oidmap-devices
    provides: OidMapWatcherService with K8s API watch and OidMapService.UpdateMap diff logging
provides:
  - Verified OidMapWatcherService live reload across all CRUD scenarios
  - Confirmed malformed JSON error handling retains previous map
  - Confirmed Prometheus reflects metric name changes within scrape cycle
affects: [16-02, 16-03]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified: []

key-decisions:
  - "No code changes -- pure operational verification against live K8s cluster"

patterns-established: []

# Metrics
duration: 7min
completed: 2026-03-08
---

# Phase 16 Plan 01: OidMap ConfigMap Watcher UAT Summary

**All 6 OidMap hot-reload scenarios verified against 3-replica K8s cluster: add, rename, remove, malformed JSON, and restore -- all via K8s watch API with sub-second event delivery**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-08T09:21:08Z
- **Completed:** 2026-03-08T09:27:30Z
- **Tasks:** 2/2
- **Files modified:** 0 (operational verification only)

## Accomplishments

- Baseline metrics confirmed flowing: OBP-01 (24 series), NPB-01 (64 series)
- OidMapWatcherService receives K8s watch events within 1 second of ConfigMap apply
- All 3 replicas (1 leader + 2 followers) process every watch event independently
- Malformed JSON logged as error and previous OID map retained (no data loss)
- Prometheus reflects metric name changes within one scrape cycle (~15s)

## Test Results

| # | Scenario | Result | Details |
|---|----------|--------|---------|
| 0 | Baseline verification | PASS | OBP-01: 24 series, NPB-01: 64 series |
| 1 | Add new OID | PASS | 3/3 pods: "+1 added", "test_oid_added" entry logged |
| 2 | Rename existing OID | PASS | 3/3 pods: "~1 changed", Prometheus shows renamed metric (1 series) |
| 3 | Remove OID | PASS | 3/3 pods: "-1 removed", "was test_oid_added" logged |
| 4 | Malformed JSON | PASS | 3/3 pods: [ERR] "Failed to parse", metrics still flowing (25 series) |
| 5 | Restore original | PASS | 3/3 pods: "92 entries total", original metric name back in Prometheus |

## Key Log Evidence

**Scenario 1 (Add):**
```
OidMap hot-reloaded: 93 entries total, +1 added, -0 removed, ~0 changed
OidMap added: 1.3.6.1.4.1.47477.10.99.1.0 -> test_oid_added
```

**Scenario 2 (Rename):**
```
OidMap hot-reloaded: 93 entries total, +0 added, -0 removed, ~1 changed
OidMap changed: 1.3.6.1.4.1.47477.10.21.1.3.1.0 obp_link_state_L1 -> obp_link_state_L1_renamed
```

**Scenario 3 (Remove):**
```
OidMap hot-reloaded: 92 entries total, +0 added, -1 removed, ~0 changed
OidMap removed: 1.3.6.1.4.1.47477.10.99.1.0 (was test_oid_added)
```

**Scenario 4 (Malformed JSON):**
```
[ERR] Failed to parse oidmaps.json from ConfigMap simetra-oidmaps -- skipping reload
```

**Scenario 5 (Restore):**
```
OidMap hot-reloaded: 92 entries total, +0 added, -0 removed, ~1 changed
OidMap changed: 1.3.6.1.4.1.47477.10.21.1.3.1.0 obp_link_state_L1_renamed -> obp_link_state_L1
```

## Task Commits

No code commits -- this is an operational verification plan. Only the summary artifact is committed.

**Plan metadata:** (docs: complete 16-01 plan)

## Files Created/Modified

None -- operational verification only.

## Decisions Made

None - followed plan as specified.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- `kubectl logs -l app=snmp-collector --since=Xm` returned empty when using the label selector combined with `--since` flag. Workaround: queried each pod individually by name. This appears to be a kubectl client quirk on Windows/Git Bash.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- OidMap hot-reload verified end-to-end, ready for 16-02 (Device ConfigMap watcher testing)
- ConfigMap restored to original 92-entry state
- No blockers

---
*Phase: 16-test-k8s-configmap-watchers*
*Completed: 2026-03-08*
