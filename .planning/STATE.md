# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-07)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.2 Operational Enhancements — Phase 15 (next)

## Current Position

Phase: 15 (K8s ConfigMap Watch and Unified Config)
Plan: 5 of 5
Status: In progress
Last activity: 2026-03-07 — Completed 15-04-PLAN.md (K8s manifests: unified ConfigMap, RBAC, cleanup)

Progress: [################____] 4/5 Phase 15 plans

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
- Unified simetra-config.json ConfigMap key replaces separate oidmap-obp.json, oidmap-npb.json, devices.json keys
- OID map naming: obp_{metric}_L{linkNum} for OBP, npb_{metric} / npb_port_{metric}_P{n} for NPB
- Config auto-scan: CONFIG_DIRECTORY env var with ContentRootPath/config fallback
- K8s directory mount at /app/config (no subPath) enables ConfigMap hot-reload
- DeviceRegistry resolves K8s Service DNS names at startup via Dns.GetHostAddresses fallback
- OidMapService decoupled from IOptionsMonitor; accepts Dictionary + supports UpdateMap atomic swap
- DeviceRegistry supports ReloadAsync with async DNS and volatile FrozenDictionary swap
- JobIntervalRegistry.Unregister and LivenessVectorService.Remove for cleanup on config reload
- SimetraConfigModel POCO unifies OidMap + Devices in single JSON document
- DeviceOptions.CommunityString optional override; null falls back to Simetra.{Name} convention
- RBAC Role named simetra-role covers leases + configmaps (renamed from simetra-lease-role)
- DynamicPollScheduler.ReconcileAsync diffs Quartz metric-poll-* jobs and adds/removes/reschedules
- ConfigMapWatcherService watches simetra-config ConfigMap via K8s API with auto-reconnect
- ConfigMap reload serialized via SemaphoreSlim; orchestrates OidMap + DeviceRegistry + PollScheduler

### Known Tech Debt

- IDeviceRegistry.TryGetDevice(IPAddress) orphaned — community string replaced IP lookup
- PollSchedulerStartupService thread pool log off-by-one (HeartbeatJob not counted)

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-07
Stopped at: Completed 15-04-PLAN.md
Resume file: None
