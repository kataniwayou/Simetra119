# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-24)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** Planning next milestone

## Current Position

Phase: — (between milestones)
Plan: —
Status: v2.6 shipped, ready for next milestone
Last activity: 2026-03-24 — v2.6 milestone archived

Progress: v2.6 shipped. Start next milestone with /gsd:new-milestone

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
- v2.6: Approach is interactive command interpreter, not 17 script files
- v2.6: Command pattern format: {Tenant}-{V/S}-{#}E-{#}R (e.g. T1_P1-V-2E-1R)
- v2.6: 4-tenant fixture — T1_P1 (P1, 2E/2R/1C), T2_P1 (P1, 4E/4R/1C), T1_P2 (P2, 2E/2R/1C), T2_P2 (P2, 4E/4R/1C)
- v2.6: Interpreter reuses existing simulator /oid/{suffix}/{value} HTTP endpoints — no new simulator code
- v2.6: Non-violated metrics in a pattern are set to healthy value (not left in previous state)
- v2.6: S mode calls sim_set_oid_stale for the specified metrics
- v2.6 (82-02): OID_MAP key format: TENANT.ROLE.N.FIELD — T1_P1 subtree=8, T2_P1=9, T1_P2=10, T2_P2=11
- v2.6 (82-02): Evaluate healthy=10/violated=0; Resolved healthy=1/violated=0; all 4 tenants reuse e2e_set_bypass
- v2.6 (82-01): Simulator baseline must use healthy values (eval=10, res=1) not 0 — otherwise tenants start Violated
- v2.6 (83-01): sim_command.sh uses #!/usr/bin/env bash + bash 4+ guard (macOS ships bash 3, lacks associative arrays)

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
| 090 | Enable table footer row counts on all 4 table panels | 2026-03-24 | 32ed4dd | [090-table-footer-row-counts](./quick/090-table-footer-row-counts/) |
| 091 | Add evaluation pipeline logging to SnapshotJob | 2026-03-24 | dce2a00 | [091-snapshot-evaluation-logging](./quick/091-snapshot-evaluation-logging/) |

## Session Continuity

Last session: 2026-03-24
Stopped at: Completed quick-091 — evaluation pipeline logging in SnapshotJob
Resume file: None
