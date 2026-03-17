# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-17)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.1 E2E Tenant Evaluation Tests — Phase 53: Single-Tenant Scenarios

## Current Position

Phase: 53 of 55 (Single-Tenant Scenarios)
Plan: 03 of TBD
Status: In progress
Last activity: 2026-03-17 — Completed 53-03-PLAN.md — STS-04 suppression window (3 windows, 6 sub-scenarios) and STS-05 staleness detection

Progress: [████░░░░░░] 44% (v2.1)

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
| 53-03 | sleep 20 in STS-04 Window 3 is the only fixed sleep in Phase 53 | No log event signals suppression window expiry; fixed sleep is unavoidable |
| 53-03 | STS-05 primes with healthy + sleep 20 before stale switch | HasStaleness returns false for null slots; slots must hold recent data to age out |
| 53-03 | STS-04 suppressed counter uses device_name="e2e-tenant-A-supp" | IncrementCommandSuppressed(tenant.Id) uses tenant ID as label value, not device name |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-17T13:26:45Z
Stopped at: Completed 53-03-PLAN.md — STS-04 suppression window and STS-05 staleness detection (scenarios 32-33)
Resume file: None
