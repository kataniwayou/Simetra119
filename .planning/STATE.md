# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-09)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.4 E2E System Verification -- Phase 22 complete, ready for Phase 23

## Current Position

Phase: 22 of 24 (Business Metric and Unknown OID Verification)
Plan: 02 of 02
Status: Phase complete
Last activity: 2026-03-09 -- Completed 22-02-PLAN.md (unknown OID + trap verification scenarios)

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2, 2/2 v1.3 | v1.4: [######....] 3/5 phases, 22: 2/2 plans

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-08 |
| v1.2 Operational Enhancements | 15-16 | 8 | 2026-03-08 |
| v1.3 Grafana Dashboards | 18-19 | 2 | 2026-03-09 |

See `.planning/MILESTONES.md` for details.
See `.planning/milestones/` for archived roadmaps and requirements.

## Accumulated Context

### Key Architectural Facts

- MediatR 12.5.0 (MIT) -- do NOT upgrade to v13+ (RPL-1.5 license)
- Two-meter architecture: MeterName for all instances, LeaderMeterName for leader only
- Community string convention: Simetra.{DeviceName} for both auth and device identity
- Split config: simetra-oidmaps ConfigMap + simetra-devices ConfigMap + simetra-config
- K8s directory mount at /app/config (no subPath) enables ConfigMap hot-reload

### E2E Test Context (v1.4)

- OTel 15s export interval: poll-until-satisfied with 30s timeout, 3s interval (never fixed sleeps)
- Prometheus 5-min staleness: verify removal via label change or counter stagnation, never absence
- Counter assertions: cumulative temporality, use deltas filtered by device_name
- Leader election: business metrics (snmp_gauge/snmp_info) export from leader only; pipeline counters from all
- Test simulator uses enterprise OID subtree 47477.999, community Simetra.E2E-SIM
- Sequential test execution required (shared Prometheus state)

### Known Tech Debt

None.

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-09
Stopped at: Completed 22-02-PLAN.md -- Phase 22 complete, ready for Phase 23
Resume file: None
