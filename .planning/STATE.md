# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-23)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.4 Tenant Vector Metrics — Phase 75

## Current Position

Phase: 75 of 75 (E2E Validation Scenarios) — In progress
Plan: 2/4 complete
Status: Plans 75-01 and 75-02 complete — scenarios 107-110 created
Last activity: 2026-03-23 — Completed 75-02-PLAN.md (scenarios 109-110)

Progress: [████████░░] v2.4 phase 4/4 in progress

## Performance Metrics

**Velocity:**
- Total plans completed: 156 (v1.0 through v2.3, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~43 hours

## Accumulated Context

### Key Facts

- 110 E2E scenario scripts total (01-110, including TVM scenarios 107-110)
- run-all.sh uses sort -V for version-aware ordering (required for 100+ scenarios)
- CommandWorkerService resolves SET response names via command map (not OID map)
- dispatched = evaluation decision, suppressed = execution prevention, failed = runtime error
- snmp.event.rejected = ValidationBehavior failures only (not unmapped OIDs)
- Heartbeat produces snmp_gauge{resolved_name="Heartbeat"} (via MergeWithHeartbeatSeed)
- v2.4: TenantMetricService uses "SnmpCollector.Tenant" meter — NOT "SnmpCollector.Leader" (would be follower-gated)
- v2.4: Tier counters increment by holder/command count per cycle, not by 1
- v2.4: Duration stopwatch inside EvaluateTenant per-tenant, not around Task.WhenAll group
- v2.4: Prometheus label casing (tenant_id vs tenantId) must be confirmed before authoring dashboard PromQL
- v2.4: SnapshotJob.EvaluateTenant returns TenantState; pre-tier = NotReady, tier-4 = Unresolved; both block advance gate
- v2.4: DI registration wires ITenantMetricService → TenantMetricService (confirmed in ServiceCollectionExtensions.cs line 409)
- v2.4: CommandRequest carries TenantId + Priority as positional params 6 and 7 for per-tenant metric tagging
- v2.4: CommandWorkerService IncrementCommandFailed calls are additive (tenant + pipeline metrics both fire at each failure site)
- v2.4: EvaluateTenant instrumented with RecordAndReturn at all 4 return paths (NotReady/Resolved/Healthy/Unresolved)
- v2.4: Tier counters use count-then-loop pattern — CountX() computed once, loop calls IncrementTierX once per holder
- v2.4: NotReady path records only state gauge + duration (no tier or command counters — design decision)
- v2.4: Operations dashboard Tenant Status table uses columns Host, Pod, Tenant, Priority, State, Dispatched, Failed, Suppressed, Stale, Resolved, Evaluate, P99 (ms), Trend (panel id=28, row id=27)
- v2.4: tenant_id and priority are literal snake_case tag keys in TenantMetricService (no OTel conversion needed)
- v2.4: Resolved path (tier=2) asserts state=2 + duration delta + no commands (tier2_resolved counter NOT assertable when ALL resolved are violated)
- v2.4: Histogram P99 PromQL: histogram_quantile(0.99, rate(tenant_evaluation_duration_milliseconds_bucket{...}[5m])) — guard NaN/+Inf
- v2.4: ROADMAP "tenant_gauge_duration_milliseconds" is a typo; correct name is tenant_evaluation_duration_milliseconds

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
v2.3 decisions archived to milestones/v2.3-ROADMAP.md.

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 082 | Refactor PollOptions.MetricNames to Metrics object wrapper | 2026-03-20 | 7205c05 | [082-metricnames-to-polloptions-object](./quick/082-metricnames-to-polloptions-object/) |
| 083 | Transform E2E fixture MetricNames to Metrics object-wrapper | 2026-03-20 | d9b34ef | [083-e2e-metricnames-to-metrics-transform](./quick/083-e2e-metricnames-to-metrics-transform/) |
| 084 | Align PSS-17c and PSS-20c --since to 10s (eliminate 2s overlap) | 2026-03-22 | 20f02aa | [084-pss-17c-20c-since-alignment](./quick/084-pss-17c-20c-since-alignment/) |
| 085 | Reorder tenant table columns and batch metrics at exit | 2026-03-23 | 934c413 | [085-tenant-table-reorder-and-metric-timing](./quick/085-tenant-table-reorder-and-metric-timing/) |
| 086 | Remove 5 redundant ops dashboard panels | 2026-03-23 | fded5bd | [086-remove-redundant-ops-panels](./quick/086-remove-redundant-ops-panels/) |
| 087 | Switch Grafana to manual dashboard management | 2026-03-23 | 8c5221f | [087-grafana-manual-dashboard-mgmt](./quick/087-grafana-manual-dashboard-mgmt/) |

## Session Continuity

Last session: 2026-03-23
Stopped at: Completed 75-02-PLAN.md — scenarios 109-110 created
Resume file: None
