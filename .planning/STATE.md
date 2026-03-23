# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-23)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.4 Tenant Vector Metrics — Phase 74

## Current Position

Phase: 73 of 75 (SnapshotJob Instrumentation) — COMPLETE
Plan: 2/2 — verified
Status: Phase 73 complete, verified ✓ — ready for Phase 74
Last activity: 2026-03-23 — Phase 73 verified (4/4 must-haves)

Progress: [█████░░░░░] v2.4 phase 2/4 complete

## Performance Metrics

**Velocity:**
- Total plans completed: 156 (v1.0 through v2.3, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~43 hours

## Accumulated Context

### Key Facts

- 106 E2E scenario scripts total (01-106)
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

## Session Continuity

Last session: 2026-03-23
Stopped at: Phase 73 verified — Phase 74 ready to discuss
Resume file: None
