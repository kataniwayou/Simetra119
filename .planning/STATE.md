# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-16)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.0 Tenant Evaluation & Control -- COMPLETE

## Current Position

Phase: 50 of 50 (Label Rename)
Plan: 1 of 1 in current phase
Status: v2.0 milestone complete
Last activity: 2026-03-17 — Completed quick/067 (Flatten tenants.json to bare array format)

Progress: [██████████] v2.0 — 13/13 plans complete

## Performance Metrics

**Velocity:**
- Total plans completed: 109 (v1.0 through v1.10 + Phases 45-50-01, including quick tasks)
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
- 50-01: ~2 min
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
- SnapshotJob Tier 1+2: HasStaleness (excludes Trap/0-interval), AreAllResolvedViolated (ConfirmedBad gate), IsViolated (strict inequality, null=violated), TierResult enum (Phase 48-02)
- MetricSlotHolder sentinel: constructor seeds ImmutableArray with MetricSlot(0, null, UtcNow) — ReadSlot never returns null, staleness clock starts at construction (quick-064)
- GraceMultiplier Range [2.0, 5.0] on PollOptions, LivenessOptions; TimeoutMultiplier Range [0.1, 0.9] on PollOptions (quick-064)
- SnapshotJob section in appsettings.json: IntervalSeconds=15, TimeoutMultiplier=0.8 (quick-064)
- SnapshotJob Tier 3+4: AreAllEvaluateViolated (vacuous false — no data = no command), Tier 4 command dispatch with suppression key {TenantId}:{Ip}:{Port}:{CommandName}, channel-full handled gracefully (Phase 48-03)
- SnapshotJob priority group traversal: Task.WhenAll parallel within-group, sequential across groups, advance gate blocks on Stale/Commanded, advances on Healthy/ConfirmedBad, Tier 4 zero-enqueue returns ConfirmedBad (Phase 48-04)
- CommandRequest: 5 fields (Ip, Port, CommandName, Value, ValueType) — DeviceName removed, resolved from DeviceRegistry at execution time (quick-061)
- CommandWorkerService: uses device.Name from registry for all labels/logs/counters, consistent with MetricPollJob pattern (quick-061)
- CommandWorkerService: Stopwatch-based duration logging — Information on SET success, Warning on timeout, both with DurationMs:F1 (Phase 49-01)
- Operations dashboard: 3 command panels (sent/failed/suppressed) at y=39 w=8 each, .NET Runtime row shifted to y=47 (Phase 49-01)
- Prometheus label renamed: metric_name -> resolved_name on all 4 SNMP instruments (Phase 50-01)

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 058 | Add GraceMultiplier + resolve IntervalSeconds/GraceMultiplier from device poll group | 2026-03-15 | b2059b6 | [058-gracemultiplier-device-resolved](./quick/058-gracemultiplier-device-resolved/) |
| 059 | Build, deploy, and test heartbeat liveness E2E script | 2026-03-16 | 2d2a97a | [059-build-deploy-test-heartbeat-liveness](./quick/059-build-deploy-test-heartbeat-liveness/) |
| 060 | Pipeline panel layout: 4 semantic rows (events/polls/traps/routing) | 2026-03-16 | 142e5a0 | [060-pipeline-panel-layout-rows](./quick/060-pipeline-panel-layout-rows/) |
| 061 | Remove DeviceName from CommandRequest, resolve from DeviceRegistry | 2026-03-16 | 88e2f8c | [061-remove-devicename-from-commandrequest](./quick/061-remove-devicename-from-commandrequest/) |
| 062 | Add finally block cleanup for OperationCorrelationId in services | 2026-03-16 | f9c73c7 | [062-add-correlation-finally-cleanup](./quick/062-add-correlation-finally-cleanup/) |
| 063 | Initialize CurrentCorrelationId with Guid at construction | 2026-03-16 | 223b454 | — |
| 064 | Staleness sentinel timestamp + Range validation + SnapshotJob config | 2026-03-16 | 6738f73 | [064-staleness-sentinel-range-validation](./quick/064-staleness-sentinel-range-validation/) |
| 065 | Remove snmp.aggregated.computed + add snmp.snapshot.cycle_duration_ms | 2026-03-17 | 45a14db | [065-remove-aggregated-add-cycle-duration](./quick/065-remove-aggregated-add-cycle-duration/) |
| 066 | Fix tenants.json binding to match devices.json pattern (remove double nesting) | 2026-03-17 | c0b85d7 | [066-tenants-config-binding-consistency](./quick/066-tenants-config-binding-consistency/) |
| 067 | Flatten tenants.json to bare array format matching devices.json | 2026-03-17 | acdde9b | [067-tenants-bare-array-config](./quick/067-tenants-bare-array-config/) |
| 068 | Threshold equality condition (Min==Max → violated if value equals) | 2026-03-17 | f87992b | [068-threshold-equal-condition](./quick/068-threshold-equal-condition/) |

## Session Continuity

Last session: 2026-03-17
Stopped at: Completed quick/067 — flatten tenants.json to bare array format; 416 tests green
Resume file: None
