# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-16)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.0 Observability & Dashboard — Phase 49 in progress

## Current Position

Phase: 49 of 50 (Observability and Dashboard)
Plan: 1 of ? in current phase
Status: In progress
Last activity: 2026-03-16 — Completed 49-01-PLAN.md

Progress: [█████████░] v2.0 — 12/13 plans complete

## Performance Metrics

**Velocity:**
- Total plans completed: 105 (v1.0 through v1.10 + Phases 45-49-01, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~39 hours

**Recent Trend:**
- 46-01: ~10 min
- 46-02: ~5 min
- 46-03: ~1 min
- 47-01: ~1 min
- 47-02: ~2 min
- 48-01: ~1 min
- 48-02: ~5 min
- 48-03: ~3 min
- 48-04: ~5 min
- 49-01: ~4 min
- Trend: Stable (small surgical plans)

*Updated after each plan completion*

## Accumulated Context

### Key Facts for v2.0

- OidResolutionBehavior bypass is now data-driven (`MetricName is not null && != Unknown`) — no Source-specific conditions (Phase 45-01)
- MetricSlotHolder.Role: immutable string property from MetricSlotOptions.Role (Phase 45-02)
- Tenant.Commands: IReadOnlyList<CommandSlotOptions> from TenantOptions.Commands (Phase 45-02)
- Tenant.SuppressionWindowSeconds: int property (default 60) from TenantOptions (Phase 46-01)
- ISuppressionCache: singleton, TrySuppress(key, windowSeconds) returns true=suppressed/false=proceed, lazy TTL, stamps on allowed calls only (Phase 46-01)
- Value+ValueType parse validation at tenant config load time (Phase 46-01)
- ISnmpClient.SetAsync: single Variable, returns IList<Variable>, delegates to Messenger.SetAsync (Phase 46-02)
- SharpSnmpClient.ParseSnmpData: static helper for Integer32/OctetString/IP dispatch (Phase 46-02)
- SnapshotJobOptions: IntervalSeconds=15, TimeoutMultiplier=0.8, ValidateOnStart (Phase 46-02)
- PipelineMetricService: 15 counters incl. snmp.command.sent/failed/suppressed (Phase 46-03)
- `CommandWorkerService` must use Singleton-then-HostedService DI pattern
- Community string resolved in `CommandWorkerService` at execution time (not at enqueue time)
- `[DisallowConcurrentExecution]` on `SnapshotJob` is the only concurrency guard for suppression
- SharpSnmpLib IP type is `Lextm.SharpSnmpLib.IP` (not `IpAddress`)
- `TryWrite` (non-blocking) for channel enqueue
- ICommandChannel: Writer/Reader only, no Complete/WaitForDrainAsync — immediate stop on cancel (Phase 47-01)
- CommandChannel: BoundedChannel capacity 16, DropWrite mode, SingleWriter=false, SingleReader=true (Phase 47-01)
- CommandRequest: sealed record (Ip, Port, CommandName, Value, ValueType, DeviceName) — no CommunityString (Phase 47-01)
- CommandWorkerService: BackgroundService draining ICommandChannel, SetAsync with timeout, response dispatch via ISender.Send with Source=Command (Phase 47-02)
- DeviceName on SnmpOidReceived from req.DeviceName, not device.Name — locked decision (Phase 47-02)
- MetricName pre-set from ICommandMapService.ResolveCommandName on response varbinds (Phase 47-02)
- SnapshotJob: skeleton with 8-param DI, placeholder Groups loop, registered in Quartz with intervalRegistry "snapshot" entry (Phase 48-01)
- SnapshotJob Tier 1+2: HasStaleness (excludes Trap/0-interval/null-slot), AreAllResolvedViolated (ConfirmedBad gate), IsViolated (strict inequality, null=violated), TierResult enum (Phase 48-02)
- SnapshotJob Tier 3+4: AreAllEvaluateViolated (vacuous false — no data = no command), Tier 4 command dispatch with suppression key {TenantId}:{Ip}:{Port}:{CommandName}, channel-full handled gracefully (Phase 48-03)
- SnapshotJob priority group traversal: Task.WhenAll parallel within-group, sequential across groups, advance gate blocks on Stale/Commanded, advances on Healthy/ConfirmedBad, Tier 4 zero-enqueue returns ConfirmedBad (Phase 48-04)
- CommandWorkerService: Stopwatch-based duration logging — Information on SET success, Warning on timeout, both with DurationMs:F1 (Phase 49-01)
- Operations dashboard: 3 command panels (sent/failed/suppressed) at y=39 w=8 each, .NET Runtime row shifted to y=47 (Phase 49-01)

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 058 | Add GraceMultiplier + resolve IntervalSeconds/GraceMultiplier from device poll group | 2026-03-15 | b2059b6 | [058-gracemultiplier-device-resolved](./quick/058-gracemultiplier-device-resolved/) |
| 059 | Build, deploy, and test heartbeat liveness E2E script | 2026-03-16 | 2d2a97a | [059-build-deploy-test-heartbeat-liveness](./quick/059-build-deploy-test-heartbeat-liveness/) |
| 060 | Pipeline panel layout: 4 semantic rows (events/polls/traps/routing) | 2026-03-16 | 142e5a0 | [060-pipeline-panel-layout-rows](./quick/060-pipeline-panel-layout-rows/) |

## Session Continuity

Last session: 2026-03-16
Stopped at: Completed 49-01-PLAN.md — Stopwatch duration logging + 3 command dashboard panels; 414 tests green
Resume file: None
