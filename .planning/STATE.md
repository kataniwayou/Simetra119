# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-15)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.10 Heartbeat Refactor & Pipeline Liveness — Phase 43 ready to plan

## Current Position

Phase: 43 of 44 (Heartbeat Cleanup)
Plan: — (not started)
Status: Ready to plan
Last activity: 2026-03-15 — v1.10 roadmap created; Phase 43 and 44 defined

Progress: [####################] v1.0-v1.9 complete | [ ] 0/3 v1.10 plans

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

- Heartbeat bypass lives in `TenantVectorFanOutBehavior` as `if (DeviceName == HeartbeatDeviceName)` — Phase 43 deletes this block entirely
- `TenantVectorRegistry.Reload` contains a hardcoded heartbeat holder + tenant at `int.MinValue` priority — Phase 43 removes all of this
- `ILivenessVectorService` stamps on job completion in `HeartbeatJob.finally` — this is UNCHANGED; it serves scheduler liveness, not pipeline liveness
- New `IHeartbeatLivenessService` is distinct from `ILivenessVectorService` — stamps when `OtelMetricHandler` processes a heartbeat message (pipeline arrival, not job completion)
- Staleness window: `HeartbeatJobOptions.DefaultIntervalSeconds` (15) × default `GraceMultiplier` (2.0) = 30s — no hardcoded magic numbers
- HB-08/09/10 are preserved-behavior requirements — verified in Phase 44, not implemented

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 058 | Add GraceMultiplier + resolve IntervalSeconds/GraceMultiplier from device poll group | 2026-03-15 | b2059b6 | [058-gracemultiplier-device-resolved](./quick/058-gracemultiplier-device-resolved/) |

## Session Continuity

Last session: 2026-03-15
Stopped at: v1.10 roadmap created — Phase 43 (Heartbeat Cleanup) and Phase 44 (Pipeline Liveness) defined; ready to plan Phase 43
Resume file: None
