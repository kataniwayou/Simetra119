# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-15)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** Phase 45 — Structural Prerequisites

## Current Position

Phase: 45 (structural-prerequisites)
Plan: 02 of 2
Status: In progress (45-02 complete)
Last activity: 2026-03-16 — Completed 45-02-PLAN.md

Progress: [####################] v1.0-v1.9 complete | [███] 3/3 v1.10 plans | [█░] 1/2 Phase 45 plans

## Performance Metrics

**Velocity:**
- Total plans completed: 94 (v1.0 through v1.9, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~39 hours

**Recent Trend:**
- 42-02: ~5 min
- quick/058: ~5 min
- 45-02: ~3 min
- Trend: Stable (small surgical plans)

*Updated after each plan completion*

## Accumulated Context

### Key Architectural Facts (v1.10 relevant)

- Heartbeat bypass DELETED from `TenantVectorFanOutBehavior` (Phase 43 complete) — "Simetra" not in DeviceRegistry → fan-out naturally skipped
- `TenantVectorRegistry.Reload` heartbeat injection DELETED (Phase 43 complete) — TenantCount = survivingTenantCount (no +1)
- `ILivenessVectorService` stamps on job completion in `HeartbeatJob.finally` — this is UNCHANGED; it serves scheduler liveness, not pipeline liveness
- `IHeartbeatLivenessService` (Phase 44-01 complete) — distinct from `ILivenessVectorService`; stamps when `OtelMetricHandler` processes a heartbeat message (pipeline arrival, not job completion)
- `HeartbeatLivenessService`: volatile long (UTC ticks), Volatile.Write in Stamp(), Volatile.Read in LastArrival getter
- Stamp point: AFTER `_pipelineMetrics.IncrementHandled(deviceName)` in numeric case, guarded by `deviceName == HeartbeatJobOptions.HeartbeatDeviceName`
- DI: `services.AddSingleton<IHeartbeatLivenessService, HeartbeatLivenessService>()` in `AddSnmpPipeline`
- Staleness window: `IOptions<HeartbeatJobOptions>.Value.IntervalSeconds` (runtime-configured) × `GraceMultiplier` = threshold — no hardcoded values
- HB-06, HB-07 satisfied by Phase 44-02: LivenessHealthCheck reads IHeartbeatLivenessService.LastArrival, reports pipeline-heartbeat stale in K8s liveness probe
- HB-08/09/10 preserved and verified: HeartbeatJob.cs, OidMapService.cs, ILivenessVectorService untouched
- LivenessHealthCheck constructor now takes IHeartbeatLivenessService + IOptions<HeartbeatJobOptions> (DI auto-resolves via AddCheck<T>)
- Null LastArrival → always stale; pipeline-heartbeat key always in diagnostic data dict; 338 tests green
- MetricSlotHolder.Role: immutable string property set from MetricSlotOptions.Role in constructor (Phase 45-02) — NOT in CopyFrom
- Tenant.Commands: IReadOnlyList<CommandSlotOptions> set from TenantOptions.Commands in constructor (Phase 45-02)
- TenantVectorRegistry.Reload passes metric.Role and tenantOpts.Commands to runtime models (Phase 45-02); 342 tests green

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 058 | Add GraceMultiplier + resolve IntervalSeconds/GraceMultiplier from device poll group | 2026-03-15 | b2059b6 | [058-gracemultiplier-device-resolved](./quick/058-gracemultiplier-device-resolved/) |

## Session Continuity

Last session: 2026-03-16T11:25:18Z
Stopped at: Completed 45-02-PLAN.md — MetricSlotHolder.Role and Tenant.Commands propagated from config; 342 tests green
Resume file: None
