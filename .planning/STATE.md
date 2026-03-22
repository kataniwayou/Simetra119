# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-22)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.3 Metric Validity & Correctness — Phase 68: Command Counters

## Current Position

Phase: 68 of 71 (Command Counters)
Plan: 3 of 3 in current phase
Status: Phase complete
Last activity: 2026-03-22 — Completed 68-03-PLAN.md (CCV-03 assertion corrected: dispatched and suppressed both fire simultaneously during suppression window; replaced eq 0 with both > 0)

Progress: [███░░░░░░░] v2.3 phase 68 complete (3/3 plans)

## Performance Metrics

**Velocity:**
- Total plans completed: 143 (v1.0 through v2.3 phase 68, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~40 hours

*Updated after each plan completion*

## Accumulated Context

### Key Facts

- SnapshotJob IntervalSeconds=1 in E2E cluster (not 15s production default)
- Existing OIDs: .999.4.x (T1), .999.5.x (T2), .999.6.x (T3), .999.7.x (T4)
- sim_set_oid for per-OID control, sim_set_oid_stale for staleness
- Thresholds: Resolved Min:1, Evaluate Min:10
- Grace window: TimeSeriesSize(3) * IntervalSeconds(1) * GraceMultiplier(2) = 6s
- Stage gating: script checks FAIL_COUNT before proceeding to next stage
- 37 metric instruments total: 14 pipeline counters, 4 business instruments, 3 histograms, 16 test OIDs
- snmp.event.errors and snmp.trap.dropped are safety-net counters — assert stays 0
- Leader-gated export: snmp_gauge/snmp_info only exported by leader pod
- E2E scenarios continue from 69 (68 existing: 01-68)
- MCV-11/12 require fake unreachable device IP in E2E fixture config
- CCV-04 requires triggering SET for OID not in command map
- snmp_command_failed_total OID-not-found path uses device_name=IP:port (not tenant name); use empty filter '' to avoid label brittleness

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 082 | Refactor PollOptions.MetricNames to Metrics object wrapper | 2026-03-20 | 7205c05 | [082-metricnames-to-polloptions-object](./quick/082-metricnames-to-polloptions-object/) |
| 083 | Transform E2E fixture MetricNames to Metrics object-wrapper | 2026-03-20 | d9b34ef | [083-e2e-metricnames-to-metrics-transform](./quick/083-e2e-metricnames-to-metrics-transform/) |
| 084 | Align PSS-17c and PSS-20c --since to 10s (eliminate 2s overlap) | 2026-03-22 | 20f02aa | [084-pss-17c-20c-since-alignment](./quick/084-pss-17c-20c-since-alignment/) |

## Session Continuity

Last session: 2026-03-22T17:14:43Z
Stopped at: Completed 68-03-PLAN.md — CCV-03 assertion corrected (dispatched_delta > 0 AND suppressed_delta > 0 during suppression window); Phase 68 fully complete (3/3 plans)
Resume file: None
