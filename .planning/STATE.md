# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-07)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.1 Device Simulation — Phase 11: OID Map Design and OBP Population

## Current Position

Phase: 11 of 14 (OID Map Design and OBP Population)
Plan: 01 of 03 complete
Status: In progress
Last activity: 2026-03-07 — Completed 11-01-PLAN.md (OBP OID map creation)

Progress: [##########..........] 48/48 v1.0 complete, 1/9 v1.1 plans

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |

See `.planning/MILESTONES.md` for details.
See `.planning/milestones/` for archived roadmaps and requirements.

## Accumulated Context

### Key Architectural Facts

- MediatR 12.5.0 (MIT) — do NOT upgrade to v13+ (RPL-1.5 license)
- Two-meter architecture: MeterName for all instances, LeaderMeterName for leader only
- Community string convention: Simetra.{DeviceName} for both auth and device identity
- host_name from NODE_NAME env var (K8s spec.nodeName), pod_name from HOSTNAME
- Heartbeat is internal infrastructure — pipeline metrics prove liveness, no metric export
- OBP OID maps stored as separate configmap keys (oidmap-obp.json), not merged into appsettings OidMap
- OID map naming: obp_{metric}_L{linkNum} for OBP device type

### Known Tech Debt

- IDeviceRegistry.TryGetDevice(IPAddress) orphaned — community string replaced IP lookup
- PollSchedulerStartupService thread pool log off-by-one (HeartbeatJob not counted)

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-07
Stopped at: Completed 11-01-PLAN.md (OBP OID map creation)
Resume file: None
