# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-16)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.0 Tenant Evaluation & Control — Phase 45: Structural Prerequisites

## Current Position

Phase: 45 of 49 (Structural Prerequisites)
Plan: 0 of 2 in current phase
Status: Ready to plan
Last activity: 2026-03-16 — v2.0 roadmap created (phases 45-49, 17 requirements mapped)

Progress: [░░░░░░░░░░] v2.0 — 0/12 plans complete

## Performance Metrics

**Velocity:**
- Total plans completed: 94 (v1.0 through v1.10, including quick tasks)
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

### Key Facts for v2.0

- `SnmpSource.Synthetic` bypass in `OidResolutionBehavior` must NOT be extended to `Command` — SET response OIDs are real device OIDs requiring resolution
- `CommandWorkerService` must use Singleton-then-HostedService DI pattern — `AddHostedService<CommandWorkerService>()` directly creates a second instance that never processes commands
- Community string resolved in `CommandWorkerService` at execution time (not at SnapshotJob enqueue time) — hot-reload may change device config between enqueue and execute
- `[DisallowConcurrentExecution]` on `SnapshotJob` is the only concurrency guard for suppression check-then-suppress — must not be removed
- SharpSnmpLib IP address type is `Lextm.SharpSnmpLib.IP` (not `IpAddress`) — using wrong name produces CS0246
- `TryWrite` (non-blocking) for channel enqueue — `WriteAsync` blocks Quartz thread on full channel, cascading into liveness failures

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
Stopped at: v2.0 roadmap created — phases 45-49, 17/17 requirements mapped, ready to plan Phase 45
Resume file: None
