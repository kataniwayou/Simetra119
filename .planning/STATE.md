# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-23)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.5 Tenant Metrics Approach Modification — Phase 79

## Current Position

Phase: 78 of 80 (Counter Reference Cleanup) — COMPLETE
Plan: 1/1 — complete
Status: Phase 78 complete ✓ — ready for Phase 79
Last activity: 2026-03-23 — Phase 78 complete (dead code removed)

Progress: [██████░░░░] v2.5 phase 3/5 complete

## Performance Metrics

**Velocity:**
- Total plans completed: 156 (v1.0 through v2.4, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~43 hours

## Accumulated Context

### Key Facts

- 112 E2E scenario scripts total (01-112, including TVM scenarios 107-112)
- run-all.sh uses sort -V for version-aware ordering (required for 100+ scenarios)
- CommandWorkerService resolves SET response names via command map (not OID map)
- dispatched = evaluation decision, suppressed = execution prevention, failed = runtime error
- v2.4: TenantMetricService uses "SnmpCollector.Tenant" meter — NOT "SnmpCollector.Leader"
- v2.4: Tier counters increment by holder/command count per cycle, not by 1
- v2.4: Duration stopwatch inside EvaluateTenant per-tenant, not around Task.WhenAll group
- v2.4: EvaluateTenant instrumented with RecordAndReturn at all 4 return paths (NotReady/Resolved/Healthy/Unresolved)
- v2.4: NotReady path records only state gauge + duration (no tier or command counters — design decision)
- v2.4: Operations dashboard Tenant Status table panel id=28, row id=27
- v2.5: Resolved percent numerator = violated holders (higher = worse), consistent with evaluate direction
- v2.5: Command percentages: dispatched + failed + suppressed share total_tenant_commands as denominator
- v2.5: Only NotReady returns early from EvaluateTenant; all other paths gather all tier results first
- v2.5: All 6 percentage gauge calls + state gauge call recorded at single exit point after state determined
- v2.5 (76-01): tenant.state renamed to tenant.evaluation.state; aligns with evaluation.duration naming
- v2.5 (76-01): RecordXxxPercent API — service is passive recorder; callers compute ratios before calling
- v2.5 (76-01): Zero denominator: callers guard division; service records 0.0 as passed
- v2.5 (77-01): EvaluateTenant: 1 early return (NotReady only); all other paths: gather > decide > compute > single exit
- v2.5 (77-01): Stale path: resolvedTotal/evaluateTotal stub=1 to avoid div/0; both record 0.0%
- v2.5 (77-01): CommandWorkerService no longer holds ITenantMetricService — tenant command % recorded at dispatch in SnapshotJob only
- v2.5 (77-02): All 8 percentage-recording tests assert all 6 RecordXxxPercent gauges with exact computed values (never Arg.Any<double>)
- v2.5 (77-02): 479 unit tests pass after phase 77 complete

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

## Session Continuity

Last session: 2026-03-23T17:36:35Z
Stopped at: Completed 77-02-PLAN.md — percentage gauge test coverage complete
Resume file: None
