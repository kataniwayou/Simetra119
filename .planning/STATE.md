# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-16)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.0 Tenant Evaluation & Control

## Current Position

Phase: Not started (defining requirements)
Plan: —
Status: Defining requirements for v2.0
Last activity: 2026-03-16 — Milestone v2.0 started

Progress: [░░░░░░░░░░] v2.0 — Defining requirements

## Performance Metrics

**Velocity:**
- Total plans completed: 94 (v1.0 through v1.9, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~39 hours

**Recent Trend:**
- 41-01: ~10 min
- 42-01: ~10 min
- 42-02: ~5 min
- quick/058: ~5 min
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

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 058 | Add GraceMultiplier + resolve IntervalSeconds/GraceMultiplier from device poll group | 2026-03-15 | b2059b6 | [058-gracemultiplier-device-resolved](./quick/058-gracemultiplier-device-resolved/) |
| 059 | Build, deploy, and test heartbeat liveness E2E script | 2026-03-16 | 2d2a97a | [059-build-deploy-test-heartbeat-liveness](./quick/059-build-deploy-test-heartbeat-liveness/) |
| 060 | Pipeline panel layout: 4 semantic rows (events/polls/traps/routing) | 2026-03-16 | 142e5a0 | [060-pipeline-panel-layout-rows](./quick/060-pipeline-panel-layout-rows/) |

## Session Continuity

Last session: 2026-03-15T16:17:42Z
Stopped at: Completed 44-02-PLAN.md — LivenessHealthCheck extended with pipeline-arrival staleness; 338 tests green; v1.10 complete
Resume file: None
