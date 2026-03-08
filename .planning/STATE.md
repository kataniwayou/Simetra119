# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-08)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.3 Grafana Dashboards — Complete

## Current Position

Phase: 19 of 19 (Business Dashboard)
Plan: 1 of 1 in current phase
Status: Phase 19 complete, v1.3 milestone complete
Last activity: 2026-03-08 — Completed quick task 024: Add panel descriptions to operations dashboard

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2 | [##########] 2/2 v1.3

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
- K8s directory mount at /app/config (no subPath) enables ConfigMap hot-reload
- Dashboard approach: Claude creates JSON files, user imports manually via Grafana UI (no K8s provisioning)
- Phase 17 removed — stale file cleanup merged into Phase 18
- Operations dashboard at deploy/grafana/dashboards/simetra-operations.json (21 panels: pod identity table, 11 pipeline counters, 6 runtime, 3 row headers; all non-row panels have tooltip descriptions; Host Name dropdown filters by service_instance_id)
- Business dashboard at deploy/grafana/dashboards/simetra-business.json (4 panels: 2 row headers, gauge table, info table with device filter)

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
| 022 | Fix operations dashboard missing metrics + Prometheus remote write | 2026-03-08 | 48647cd | [022-fix-operations-dashboard-missing-metrics](./quick/022-fix-operations-dashboard-missing-metrics/) |
| 023 | Fix OTel cumulative temporality for Prometheus rate() | 2026-03-08 | 3391e97 | [023-fix-otel-cumulative-temporality-for-rate](./quick/023-fix-otel-cumulative-temporality-for-rate/) |
| 024 | Add panel descriptions to operations dashboard | 2026-03-08 | cd5ac81 | [024-add-panel-descriptions-operations-dashb](./quick/024-add-panel-descriptions-operations-dashb/) |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-08
Stopped at: Completed quick-024 (Add panel descriptions to operations dashboard)
Resume file: None
