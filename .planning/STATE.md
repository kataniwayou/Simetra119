# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-07)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.2 Operational Enhancements — Complete

## Current Position

Phase: 15 (K8s ConfigMap Watch and Unified Config)
Plan: 5 of 5
Status: Complete
Last activity: 2026-03-07 — Completed quick task 017: Split ConfigMap into OidMap + Devices watchers

Progress: [####################] 48/48 v1.0 complete, 9/9 v1.1 plans, 5/5 v1.2 plans

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-07 |

See `.planning/MILESTONES.md` for details.
See `.planning/milestones/` for archived roadmaps and requirements.

## Accumulated Context

### Key Architectural Facts

- MediatR 12.5.0 (MIT) — do NOT upgrade to v13+ (RPL-1.5 license)
- Two-meter architecture: MeterName for all instances, LeaderMeterName for leader only
- Community string convention: Simetra.{DeviceName} for both auth and device identity
- host_name from NODE_NAME env var (K8s spec.nodeName), pod_name from HOSTNAME
- Heartbeat is internal infrastructure — pipeline metrics prove liveness, no metric export
- Split config: simetra-oidmaps ConfigMap (oidmaps.json bare dict) + simetra-devices ConfigMap (devices.json bare array) + simetra-config (appsettings only)
- OID map naming: obp_{metric}_L{linkNum} for OBP, npb_{metric} / npb_port_{metric}_P{n} for NPB
- Config auto-scan: CONFIG_DIRECTORY env var with ContentRootPath/config fallback
- K8s directory mount at /app/config (no subPath) enables ConfigMap hot-reload
- DeviceRegistry resolves K8s Service DNS names at startup via Dns.GetHostAddresses fallback
- OidMapService decoupled from IOptionsMonitor; accepts Dictionary + supports UpdateMap atomic swap
- DeviceRegistry supports ReloadAsync with async DNS and volatile FrozenDictionary swap
- JobIntervalRegistry.Unregister and LivenessVectorService.Remove for cleanup on config reload
- OidMapWatcherService watches simetra-oidmaps ConfigMap, calls UpdateMap only (no device/scheduler deps)
- DeviceWatcherService watches simetra-devices ConfigMap, calls ReloadAsync + ReconcileAsync only (no OID map dep)
- DeviceOptions.CommunityString optional override; null falls back to Simetra.{Name} convention
- DynamicPollScheduler registered in both K8s and local dev modes for symmetric ReconcileAsync
- Thread pool ceiling of 50 (generous headroom for dynamic device additions at runtime)
- Program.cs local dev loads oidmaps.json (bare dict) and devices.json (bare array) independently
- RBAC Role named simetra-role covers leases + configmaps (renamed from simetra-lease-role)
- DynamicPollScheduler.ReconcileAsync diffs Quartz metric-poll-* jobs and adds/removes/reschedules
- K8s deployments use projected volume combining simetra-config, simetra-oidmaps, simetra-devices into /app/config
- Each watcher has independent SemaphoreSlim reload lock (no cascading reloads)

### Known Tech Debt

- IDeviceRegistry.TryGetDevice(IPAddress) orphaned — community string replaced IP lookup
- PollSchedulerStartupService thread pool log off-by-one (HeartbeatJob not counted)

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 017 | Split unified ConfigMap into separate OID map and devices watchers | 2026-03-07 | 2377154 | [017-split-configmap-oidmap-devices](./quick/017-split-configmap-oidmap-devices/) |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-08
Stopped at: Quick task 017 complete
Resume file: None
