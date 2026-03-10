# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-10)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.5 Priority Vector Data Layer — Phase 25 (Config Models and Validation)

## Current Position

Phase: 25 — first of 5 in v1.5 (Config Models and Validation)
Plan: Not started
Status: Ready to plan
Last activity: 2026-03-10 — Roadmap created for v1.5

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2, 2/2 v1.3, 11/11 v1.4 | [_________] 0/9 v1.5

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-08 |
| v1.2 Operational Enhancements | 15-16 | 8 | 2026-03-08 |
| v1.3 Grafana Dashboards | 18-19 | 2 | 2026-03-09 |
| v1.4 E2E System Verification | 20-24 | 11 | 2026-03-09 |

See `.planning/MILESTONES.md` for details.

## Accumulated Context

### Key Architectural Facts

- All pods maintain tenant vector state (no leader gating) — decision from spec
- Routing key: (ip, port, metric_name) — after OidResolution sets MetricName
- Port resolved via DeviceRegistry.TryGetDeviceByName — no changes to SnmpOidReceived
- Fan-out catches own exceptions, always calls next() — never kills OTel export
- FrozenDictionary atomic swap for registry + routing index
- Immutable MetricSlot record with Volatile.Write for thread safety
- Zero new NuGet packages needed

### Known Tech Debt

None.

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-10
Stopped at: Roadmap created for v1.5 Priority Vector Data Layer
Resume file: None
