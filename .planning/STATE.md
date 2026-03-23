# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-23)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** Planning next milestone

## Current Position

Milestone: v2.5 Tenant Metrics Approach Modification — SHIPPED 2026-03-23
Status: Milestone complete, archived
Last activity: 2026-03-23 — v2.5 milestone archived

Progress: [██████████] v2.5 phase 6/6 complete

## Performance Metrics

**Velocity:**
- Total plans completed: 156 (v1.0 through v2.4, including quick tasks)
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
- v2.5 (80-01): E2E smoke (107): percentage gauges asserted present (A-F); 6 old v2.4 counter names asserted absent (K-P)
- v2.5 (80-01): E2E smoke (107): stale path uses gauge value > 0 via awk float compare (no snapshot_counter/delta needed)
- v2.5 (80-01): E2E NotReady (108): tier counter snapshot/delta code removed; only state+duration assertions remain
- v2.5 (80-01): E2E all-instances (112): tenant_state -> tenant_evaluation_state in all PromQL queries
- v2.5 (80-02): E2E TVM-03/04/05: tenant_state -> tenant_evaluation_state; dispatched_total delta -> dispatched_percent gauge
- v2.5 (80-02): E2E TVM-03: resolved_percent>0 and stale_percent presence assertions added for Resolved path
- v2.5 (80-02): E2E TVM-04: stale_percent=0 and evaluate_percent=0 assertions confirm zero-violation Healthy path
- v2.5 (80-02): E2E TVM-05: dispatched_percent polled with 30s deadline loop; evaluate_percent>0 asserted for violated OID
- v2.5 (80-02): Duration histogram _count delta retained in all TVM scenarios (monotonic counter, delta valid)
- v2.5 (81-01): E2E TVM-07: 4-holder fixture (2 eval + 2 resolved); violate 1 of 2 each => evaluate_percent=50, resolved_percent=50
- v2.5 (81-01): E2E percentage == N assertion: awk '{exit (int($1+0.5) == N) ? 0 : 1}' handles float-to-integer rounding

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

Last session: 2026-03-23T20:40:10Z
Stopped at: Completed 81-01-PLAN.md — Scenario 113 (TVM-07) with 50% partial percentage assertions
Resume file: None
