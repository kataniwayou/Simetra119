# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-22)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.3 Metric Validity & Correctness — Phase 66: Pipeline Event Counters

## Current Position

Phase: 66 of 71 (Pipeline Event Counters)
Plan: 2 of 3 in current phase
Status: In progress
Last activity: 2026-03-22 — Completed 66-02-PLAN.md (MCV-01 through MCV-04 E2E scenarios)

Progress: [██░░░░░░░░] v2.3 phase 66 plan 2/3 complete

## Performance Metrics

**Velocity:**
- Total plans completed: 142 (v1.0 through v2.2, including quick tasks)
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

Last session: 2026-03-22T15:03:05Z
Stopped at: Completed 66-02-PLAN.md — MCV-01 through MCV-04 E2E scenarios (69-72)
Resume file: None
