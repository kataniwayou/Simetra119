# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-23)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.5 Tenant Metrics Approach Modification — Phase 80

## Current Position

Phase: 79 of 80 (Dashboard Percentage Update) — COMPLETE
Plan: 1/1 — complete
Status: Phase 79 complete ✓ — ready for Phase 80
Last activity: 2026-03-23 — Phase 79 complete

Progress: [████████░░] v2.5 phase 4/5 complete

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
- v2.5 (79-01): Dashboard panel id=28 queries v2.5 gauges directly; no increase()/rate() wrappers on percentage columns
- v2.5 (79-01): Zero-fallback or-on clauses removed from gauge queries (gauges always have value after first recording)
- v2.5 (79-01): Percentage column headers use (%) suffix in displayName override; raw number display (decimals=0, no unit property)

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

Last session: 2026-03-23T18:08:44Z
Stopped at: Completed 79-01-PLAN.md — dashboard PromQL updated to v2.5 percentage gauges
Resume file: None
