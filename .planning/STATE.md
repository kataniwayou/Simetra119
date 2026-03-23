# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-23)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.6 E2E Manual Tenant Simulation Suite — Phase 82: Fixture & Infrastructure

## Current Position

Phase: 82 of 84 (Fixture & Infrastructure)
Plan: — (not yet planned)
Status: Ready to plan
Last activity: 2026-03-24 — v2.6 roadmap created (phases 82-84)

Progress: [░░░░░░░░░░] v2.6 phase 0/3 complete

## Performance Metrics

**Velocity:**
- Total plans completed: 156 (v1.0 through v2.5, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~43 hours

## Accumulated Context

### Key Facts

- 113 E2E scenario scripts total (01-113, including TVM scenarios 107-113)
- run-all.sh uses sort -V for version-aware ordering (required for 100+ scenarios)
- CommandWorkerService resolves SET response names via command map (not OID map)
- dispatched = evaluation decision, suppressed = execution prevention, failed = runtime error
- v2.4: TenantMetricService uses "SnmpCollector.Tenant" meter — NOT "SnmpCollector.Leader"
- v2.4: Tier counters increment by holder/command count per cycle, not by 1
- v2.5: Resolved percent numerator = violated holders (higher = worse), consistent with evaluate direction
- v2.5: Command percentages: dispatched + failed + suppressed share total_tenant_commands as denominator
- v2.5: Only NotReady returns early from EvaluateTenant; all other paths gather all tier results first
- v2.5: All 6 percentage gauge calls + state gauge call recorded at single exit point after state determined
- v2.5 (76-01): tenant.state renamed to tenant.evaluation.state
- v2.5 (77-01): CommandWorkerService no longer holds ITenantMetricService — tenant command % recorded at dispatch in SnapshotJob only
- v2.5 (81-01): E2E percentage == N assertion: awk '{exit (int($1+0.5) == N) ? 0 : 1}' handles float-to-integer rounding
- v2.6: New scripts numbered 114-130 (continuing from 113), category added to report.sh
- v2.6: 4-tenant fixture — T1_P1 (P1, 2E/2R/1C), T2_P1 (P1, 4E/4R/1C), T1_P2 (P2, 2E/2R/1C), T2_P2 (P2, 4E/4R/1C)
- v2.6: Scripts set OID violations and leave state as-is (no cleanup between scripts)
- v2.6: Script 01 (114) restarts pods to reset all states; user verifies Grafana at each step

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
v2.3 and v2.4 decisions archived to milestones/v2.3-ROADMAP.md and milestones/v2.4-ROADMAP.md.

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 085 | Reorder tenant table columns and batch metrics at exit | 2026-03-23 | 934c413 | [085-tenant-table-reorder-and-metric-timing](./quick/085-tenant-table-reorder-and-metric-timing/) |
| 086 | Remove 5 redundant ops dashboard panels | 2026-03-23 | fded5bd | [086-remove-redundant-ops-panels](./quick/086-remove-redundant-ops-panels/) |
| 087 | Switch Grafana to manual dashboard management | 2026-03-23 | 8c5221f | [087-grafana-manual-dashboard-mgmt](./quick/087-grafana-manual-dashboard-mgmt/) |
| 088 | Move dispatch after state decision | 2026-03-23 | 25f07b7 | [088-move-dispatch-after-decide](./quick/088-move-dispatch-after-decide/) |
| 089 | Append index to duplicate tenant names instead of skipping | 2026-03-23 | 8873c2e | [089-duplicate-tenant-name-index](./quick/089-duplicate-tenant-name-index/) |

## Session Continuity

Last session: 2026-03-24
Stopped at: v2.6 roadmap created — phases 82, 83, 84 defined
Resume file: None
