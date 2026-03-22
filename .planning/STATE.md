# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-22)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.3 Metric Validity & Correctness — Phase 69: Business Metric Value Correctness

## Current Position

Phase: 69 of 71 (Business Metric Value Correctness)
Plan: 1 of 2 in current phase
Status: In progress
Last activity: 2026-03-22 — Completed 69-01-PLAN.md (MVC-01 through MVC-07 static exact-value scenarios; CCV report range fix |82|87|; new MVC category |88|95|)

Progress: [████░░░░░░] v2.3 phase 69 plan 1 complete (1/2 plans in phase)

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
- CCV-04 triggers command.failed via SET timeout (unreachable IP), NOT unmapped CommandName (unmapped causes TEN-13 to skip tenant at load time)
- snmp_command_failed_total timeout path uses device_name=device.Name (e.g. "FAKE-UNREACHABLE"); OID-not-found/device-not-found paths use device_name=IP:port
- FAKE-UNREACHABLE device must be in DeviceRegistry before tenant fixture is applied (TryGetByIpPort must succeed at validation time)
- CCV SCENARIO_RESULTS: 6 entries at indices 82-87 (83=CCV-01 assert_delta_ge, 83-85=CCV-02/03 3 entries, 86-87=CCV-04 2 entries)
- MVC SCENARIO_RESULTS: 7 entries at indices 88-94 (MVC-01 through MVC-07); index 95 reserved for MVC-08
- Counter32/Counter64 arrive in snmp_gauge as raw gauge values (no rate conversion) -- OtelMetricHandler calls RecordGauge for all 5 numeric SNMP types
- IpAddress snmp_info value label: MEDIUM confidence on exact format; MVC-07 asserts "10.0.0.1" and logs actual value in EVIDENCE

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

Last session: 2026-03-22T18:04:50Z
Stopped at: Completed 69-01-PLAN.md — MVC-01 through MVC-07 static exact-value scenarios (86-92); CCV report range fix; new MVC category |88|95|
Resume file: None
