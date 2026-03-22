# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-20)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.2 Progressive E2E Snapshot Suite (complete)

## Current Position

Phase: 65 of 65 (E2E Runner Fixes — Complete)
Plan: 01 of 01
Status: Phase complete
Last activity: 2026-03-22 — Completed 65-01: E2E runner fixes (stale filenames, cleanup trap, flaky PSS-18c/19c, standalone report categories)

Progress: [██████████] v2.2 Phase 65 complete (all phases and plans done)

## Performance Metrics

**Velocity:**
- Total plans completed: 142 (v1.0 through v2.1, including quick tasks + 56-01 + 56-02 + 57-01 + 57-02 + 59-01 + 59-02 + 62-01 + 62-02 + quick-081 + 63-01 + 63-02 + 64-01 + 64-02 + 64-03 + quick-082 + quick-083 + 65-01 + quick-084)
- Average duration: ~25 min
- Total execution time: ~39.75 hours

*Updated after each plan completion*

## Accumulated Context

### Key Facts

- SnapshotJob IntervalSeconds=1 in E2E cluster (not 15s production default)
- Existing OIDs: .999.4.x (T1), .999.5.x (T2), .999.6.x (T3), .999.7.x (T4)
- sim_set_oid for per-OID control, sim_set_oid_stale for staleness
- Thresholds: Resolved Min:1, Evaluate Min:10
- Grace window: TimeSeriesSize(3) * IntervalSeconds(1) * GraceMultiplier(2) = 6s
- Stage gating: script checks FAIL_COUNT before proceeding to next stage
- PSS-04/05 (trap/command immunity) require tenant metrics mapped to trap/command-sourced OIDs -- design decision for Phase 62

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.

| Plan | Decision | Rationale |
|------|----------|-----------|
| 63-01 | run-stage2.sh is PSS-only (53-61), not full suite | Avoids non-PSS failures contaminating PSS stage gate; run-all.sh handles full suite |
| 63-01 | Stage gate checks raw FAIL_COUNT (not delta) | Runner only sources PSS scenarios before gate -- no prior contamination possible |
| 63-01 | Explicit scenario list in runner (not glob) | Clarity; prevents unexpected file pickup |
| 63-02 | PSS-11 omits T1 counter negative assertion | snmp_command_dispatched_total is shared by device_name; T1 tier=3 log is the correct independence proof |
| 63-02 | PSS-13 uses delta >= 2 (not delta > 0) | delta > 0 passes if only one tenant dispatched; >= 2 proves both tenants contributed |
| 63-02 | All 6 OIDs primed in all two-tenant scenarios | Both tenants must pass their own readiness grace; priming only one tenant leaves the other indeterminate |
| 64-01 | run-stage3.sh manages Stage 3 fixture lifecycle (not individual scenarios) | 7 scenarios share identical setup; per-scenario apply would add 7 duplicate reload cycles |
| 64-01 | _STAGE3_CONFIGMAP_SAVED flag guards cleanup trap | Prevents restoring configmap that was never saved (early exit via Stage 1/2 gate) |
| 64-02 | G2 assertions use tier=3 (not just tier=) | Proves gate passed AND G2 reached Healthy; SNS templates used weaker tier= match |
| 64-02 | Re-prime at scenario start (not OID reset at end) | Ensures clean state regardless of prior scenario; avoids extra grace wait between scenarios |
| 64-03 | PSS-20 re-applies fixture (not just reset_oid_overrides) | reset_oid_overrides alone doesn't empty populated series; fixture re-apply forces fresh holders with empty G1 series |
| 64-03 | BEFORE snapshots taken after G1 assertions confirmed | Ensures BEFORE baseline is post-gate-block-establishment; prior scenario activity doesn't inflate BEFORE |
| 65-01 | --since must match sleep exactly (10s not 12s) | 2s overlap allowed prior-cycle logs to bleed into log-absence window causing false-positive failures |
| 65-01 | Standalone runners override _REPORT_CATEGORIES before each generate_report | SCENARIO_RESULTS starts at 0 in standalone runs; default indices 52-67 always skips PSS category |

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 082 | Refactor PollOptions.MetricNames to Metrics object wrapper | 2026-03-20 | 7205c05 | [082-metricnames-to-polloptions-object](./quick/082-metricnames-to-polloptions-object/) |
| 083 | Transform E2E fixture MetricNames to Metrics object-wrapper | 2026-03-20 | d9b34ef | [083-e2e-metricnames-to-metrics-transform](./quick/083-e2e-metricnames-to-metrics-transform/) |
| 084 | Align PSS-17c and PSS-20c --since to 10s (eliminate 2s overlap) | 2026-03-22 | 20f02aa | [084-pss-17c-20c-since-alignment](./quick/084-pss-17c-20c-since-alignment/) |

## Session Continuity

Last session: 2026-03-22T13:18:39Z
Stopped at: Completed quick-084 (PSS-17c and PSS-20c --since alignment from 12s to 10s)
Resume file: None
