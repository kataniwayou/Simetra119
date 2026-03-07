# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-07)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.1 Device Simulation — Phase 13 (Simulator Refinement)

## Current Position

Phase: 13 of 14 (Simulator Refinement)
Plan: 3 of 3
Status: Phase complete
Last activity: 2026-03-07 — Completed 13-03-PLAN.md (K8s Deployment YAML Alignment)

Progress: [############........] 48/48 v1.0 complete, 7/9 v1.1 plans

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
- OBP OID maps stored as separate configmap keys (oidmap-obp.json) with "OidMap" section wrapper for config binding
- OID map naming: obp_{metric}_L{linkNum} for OBP, npb_{metric} / npb_port_{metric}_P{n} for NPB
- Config auto-scan: CONFIG_DIRECTORY env var with ContentRootPath/config fallback
- K8s directory mount at /app/config (no subPath) enables ConfigMap hot-reload

### Known Tech Debt

- IDeviceRegistry.TryGetDevice(IPAddress) orphaned — community string replaced IP lookup
- PollSchedulerStartupService thread pool log off-by-one (HeartbeatJob not counted)

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-07
Stopped at: Completed 13-02-PLAN.md (NPB Simulator Rewrite)
Resume file: None
