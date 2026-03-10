# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-10)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.5 Priority Vector Data Layer — Phase 25 (Config Models and Validation)

## Current Position

Phase: 25 — first of 5 in v1.5 (Config Models and Validation)
Plan: 01 of 5 in phase 25
Status: In progress
Last activity: 2026-03-10 — Completed 25-01-PLAN.md

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2, 2/2 v1.3, 11/11 v1.4 | [#________] 1/9 v1.5

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
- FrozenSet<string> for O(1) metric name containment in OidMapService (D25-01)

### Known Tech Debt

None.

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-10T13:11Z
Stopped at: Completed 25-01-PLAN.md (Config Models and Validation)
Resume file: None
