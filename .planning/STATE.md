# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-15)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.10 Heartbeat Refactor & Pipeline Liveness — Phase 43 complete, Phase 44 ready

## Current Position

Phase: 44 of 44 (Pipeline Liveness)
Plan: 01 of 02 (in progress)
Status: In progress
Last activity: 2026-03-15 — Completed 44-01-PLAN.md (IHeartbeatLivenessService + OtelMetricHandler stamp)

Progress: [####################] v1.0-v1.9 complete | [██ ] 2/3 v1.10 plans

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
- Staleness window: `HeartbeatJobOptions.DefaultIntervalSeconds` (15) × default `GraceMultiplier` (2.0) = 30s — no hardcoded magic numbers
- HB-08/09/10 are preserved-behavior requirements — verified in Phase 44, not implemented

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 058 | Add GraceMultiplier + resolve IntervalSeconds/GraceMultiplier from device poll group | 2026-03-15 | b2059b6 | [058-gracemultiplier-device-resolved](./quick/058-gracemultiplier-device-resolved/) |

## Session Continuity

Last session: 2026-03-15T16:13:35Z
Stopped at: Completed 44-01-PLAN.md — IHeartbeatLivenessService created, OtelMetricHandler stamps heartbeat pipeline arrival; 334 tests green
Resume file: None
