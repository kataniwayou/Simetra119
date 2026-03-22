# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-22)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.3 Metric Validity & Correctness — Phase 71: Negative Proofs (COMPLETE)

## Current Position

Phase: 71 of 71 (Negative Proofs)
Plan: 1 of 1 in current phase
Status: Phase complete — v2.3 milestone complete
Last activity: 2026-03-22 — Completed 71-01-PLAN.md (MNP-01 through MNP-05 negative-proof scenarios, Negative Proofs report category)

Progress: [██████████] v2.3 complete — all phases done (71/71)

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
- MLC SCENARIO_RESULTS: 8 entries at indices 96-103 (MLC-01 through MLC-08); MLC-03=index 96 (source=command), MLC-01/02/04-08 in plan 01 at 94-95/97-103
- MNP SCENARIO_RESULTS: 5 entries at indices 104-108 (MNP-01 through MNP-05); scenarios 102-106
- Next SCENARIO_RESULTS index available: 109; report category "Negative Proofs|104|108" is the 12th and last category
- device_name on snmp_gauge is community-derived (e.g. "E2E-SIM"), NOT tenant name (e.g. "e2e-pss-tenant"); dispatch counter uses tenant name, gauge series use device name

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

Last session: 2026-03-22T19:11:00Z
Stopped at: Completed 71-01-PLAN.md — MNP-01 through MNP-05 negative-proof scenarios and Negative Proofs report category; phase 71 complete; v2.3 milestone complete
Resume file: None
