# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-20)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.2 Progressive E2E Snapshot Suite

## Current Position

Phase: 64 of 64 (Advance Gate Logic — In Progress)
Plan: 02 of 02
Status: Phase 64 plan 02 complete (gate-pass scenarios done; gate-block scenarios TBD)
Last activity: 2026-03-20 — Completed 64-02-PLAN.md (PSS-14/15/16 gate-pass scenarios)

Progress: [████████░░] v2.2 Phase 64 plan 02 complete (2/3 phases, 2/2 plans in phase 64)

## Performance Metrics

**Velocity:**
- Total plans completed: 137 (v1.0 through v2.1, including quick tasks + 56-01 + 56-02 + 57-01 + 57-02 + 59-01 + 59-02 + 62-01 + 62-02 + quick-081 + 63-01 + 63-02 + 64-01 + 64-02)
- Average duration: ~25 min
- Total execution time: ~39.5 hours

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

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-20T12:32:58Z
Stopped at: Completed 64-02-PLAN.md (PSS-14/15/16 gate-pass scenarios: all G1 Resolved, all G1 Healthy, G1 mixed Resolved+Healthy)
Resume file: None
