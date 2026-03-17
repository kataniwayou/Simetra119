# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-17)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.1 E2E Tenant Evaluation Tests — Phase 51: Simulator HTTP Control Endpoint

## Current Position

Phase: 51 of 55 (Simulator HTTP Control Endpoint)
Plan: 2 of 2 in current phase (phase complete)
Status: Phase complete
Last activity: 2026-03-17 — Completed 51-01-PLAN.md (scenario registry, DynamicInstance, WritableDynamicInstance, 6 new OIDs, aiohttp HTTP endpoint)

Progress: [██░░░░░░░░] ~20% (v2.1)

## Performance Metrics

**Velocity:**
- Total plans completed: 110 (v1.0 through v2.0, including quick tasks)
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

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-17T10:57:07Z
Stopped at: Completed 51-01-PLAN.md — e2e_simulator.py reworked with HTTP control endpoint, scenario registry, 15 OIDs
Resume file: None
