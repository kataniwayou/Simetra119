# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-08)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.3 Grafana Dashboards

## Current Position

Phase: Not started (defining requirements)
Plan: —
Status: Defining requirements
Last activity: 2026-03-08 — Milestone v1.3 started

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-08 |
| v1.2 Operational Enhancements | 15-16 | 8 | 2026-03-08 |

See `.planning/MILESTONES.md` for details.
See `.planning/milestones/` for archived roadmaps and requirements.

## Accumulated Context

### Key Architectural Facts

- MediatR 12.5.0 (MIT) — do NOT upgrade to v13+ (RPL-1.5 license)
- Two-meter architecture: MeterName for all instances, LeaderMeterName for leader only
- Community string convention: Simetra.{DeviceName} for both auth and device identity
- host_name/pod_name removed from metric TagLists (redundant with OTel resource attrs service_instance_id + k8s_pod_name); logs still carry host_name
- Heartbeat is internal infrastructure — pipeline metrics prove liveness, no metric export
- IsHeartbeat bool flag set at ingestion boundary (ChannelConsumerService); behaviors/handlers use flag, not string comparison
- Split config: simetra-oidmaps ConfigMap (oidmaps.json bare dict) + simetra-devices ConfigMap (devices.json bare array) + simetra-config (appsettings only)
- OID map naming: obp_{metric}_L{linkNum} for OBP, npb_{metric} / npb_port_{metric}_P{n} for NPB
- K8s directory mount at /app/config (no subPath) enables ConfigMap hot-reload
- DeviceRegistry resolves K8s Service DNS names at startup via Dns.GetHostAddresses fallback
- OidMapService decoupled from IOptionsMonitor; accepts Dictionary + supports UpdateMap atomic swap
- DeviceRegistry supports ReloadAsync with async DNS and volatile FrozenDictionary swap
- OidMapWatcherService watches simetra-oidmaps ConfigMap, calls UpdateMap only (no device/scheduler deps)
- DeviceWatcherService watches simetra-devices ConfigMap, calls ReloadAsync + ReconcileAsync only (no OID map dep)
- DynamicPollScheduler.ReconcileAsync diffs Quartz metric-poll-* jobs and adds/removes/reschedules
- K8s deployments use projected volume combining simetra-config, simetra-oidmaps, simetra-devices into /app/config
- Each watcher has independent SemaphoreSlim reload lock (no cascading reloads)
- RBAC Role named simetra-role covers leases + configmaps
- PodIdentityOptions (renamed from SiteOptions) with PodIdentity property for K8sLeaseElection

### Known Tech Debt

- IDeviceRegistry.TryGetDevice(IPAddress) orphaned — community string replaced IP lookup
- PollSchedulerStartupService thread pool log off-by-one (HeartbeatJob not counted)

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 017 | Split unified ConfigMap into separate OID map and devices watchers | 2026-03-07 | 2377154 | [017-split-configmap-oidmap-devices](./quick/017-split-configmap-oidmap-devices/) |
| 018 | Add IsHeartbeat flag to pipeline (replace string comparisons) | 2026-03-08 | 5d0f980 | [018-add-isheartbeat-flag-to-pipeline](./quick/018-add-isheartbeat-flag-to-pipeline/) |
| 019 | Reorganize Simetra files and cleanup K8s manifests | 2026-03-08 | f3844f6 | [019-reorganize-simetra-files-cleanup-k8s](./quick/019-reorganize-simetra-files-cleanup-k8s/) |
| 020 | Remove redundant host_name/pod_name metric tags | 2026-03-08 | 976b36e | [020-remove-redundant-host-pod-tags](./quick/020-remove-redundant-host-pod-tags/) |
| 021 | Remove Site, fix deploy YAML issues | 2026-03-08 | 2e880d9 | [021-remove-site-fix-deploy-yaml-issues](./quick/021-remove-site-fix-deploy-yaml-issues/) |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-08
Stopped at: v1.2 milestone complete
Resume file: None
