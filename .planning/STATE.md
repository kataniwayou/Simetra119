# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-17)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.1 E2E Tenant Evaluation Tests — Phase 53: Single-Tenant Scenarios

## Current Position

Phase: 53 of 55 (Single-Tenant Scenarios)
Plan: 02 of TBD
Status: In progress
Last activity: 2026-03-17 — Completed 53-02-PLAN.md — STS-01 healthy, STS-02 evaluate violated, STS-03 resolved gate scenario scripts

Progress: [████░░░░░░] 43% (v2.1)

## Performance Metrics

**Velocity:**
- Total plans completed: 115 (v1.0 through v2.1 Phase 52, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~39 hours

*Updated after each plan completion*

## Accumulated Context

### Key Facts

- Phase 51 is the hard dependency: aiohttp==3.13.3 HTTP server must be registered via `loop.run_until_complete(start_http_server())` BEFORE `snmpEngine.open_dispatcher()` — order cannot be reversed
- Minimum stabilization wait per scenario: 2 × SnapshotJob cycle (30s) + OTel scrape (15s) = ~45s; depth-3 time series scenarios require 75s+
- All Prometheus command counter assertions must use `sum(snmp_command_sent_total{...})` across replicas — per-pod checks will miss leader-only counter increments
- Use distinct tenant names per scenario fixture to prevent suppression cache bleed between scenarios
- Port 8080 for HTTP endpoint — no collision confirmed (collector health port is per-pod, separate Deployment; e2e-sim pod port 8080 is free)

### Decisions

| Plan | Decision | Rationale |
|------|----------|-----------|
| 53-01 | SuppressionWindowSeconds=30 in suppression fixture | 15s SnapshotJob interval > 10s default window; 30s allows second cycle to be suppressed |
| 53-01 | Distinct tenant ID e2e-tenant-A-supp for suppression fixture | Prevents suppression cache key bleed across scenarios |
| 53-01 | Removed placeholder report categories (Watcher Resilience, Tenant Vector) | No scenario files exist for those ranges; avoids phantom entries in reports |
| 53-02 | poll_until_log 90s for STS-02 tier=4 | TimeSeriesSize=3 requires ~30s fill time; 90s accommodates 3 poll cycles safely |
| 53-02 | Negative tier=4 assertion uses direct grep (since=60s) not poll_until_log | Absence check is a snapshot — polling would just time out; single-pass grep is correct |
| 53-02 | sim_set_scenario default called explicitly in STS-03 | Clarity over brevity; makes ConfirmedBad scenario intent obvious |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-17T13:25:16Z
Stopped at: Completed 53-02-PLAN.md — STS-01/02/03 scenario scripts (scenarios 29-31)
Resume file: None
