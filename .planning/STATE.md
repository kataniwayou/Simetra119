# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-25)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v3.0 Preferred Leader Election — MILESTONE COMPLETE

## Current Position

Phase: 89 of 89 (Observability and Deployment Wiring)
Plan: 2 of 2 in current phase
Status: PHASE COMPLETE — v3.0 Preferred Leader Election feature-complete
Last activity: 2026-03-26 — Completed 89-02-PLAN.md (pod anti-affinity deployment wiring)

Progress: [████████████████████] ~100% — v3.0 COMPLETE

## Performance Metrics

**Velocity:**
- Total plans completed: 167 (v1.0 through v3.0 Phase 89, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~43 hours

## Accumulated Context

### Key Facts for v3.0

- Two-lease mechanism: leadership lease (existing `snmp-collector`) + heartbeat lease (`snmp-collector-preferred`)
- PHYSICAL_HOSTNAME env var (not NODE_NAME) — already injected from spec.nodeName in existing deployment
- PreferredHeartbeatJob is both reader (Phase 85: all pods poll heartbeat lease, update volatile bool) and writer (Phase 86: preferred pod stamps lease, gated by IsPreferredPod)
- IPreferredStampReader: narrow interface with single `bool IsPreferredStampFresh` — K8sLeaseElection reads in-memory bool (zero network calls in gate path)
- Freshness threshold = HeartbeatDurationSeconds + 5s (clock-skew tolerance) — never exactly equal to heartbeat interval
- 404 response from lease read = stale (same as old timestamp) — absence is not instant "down" signal
- Shutdown strategy: TTL expiry, NOT explicit delete — prevents 404 window that triggers premature non-preferred race
- Voluntary yield mechanism: cancel _innerCts only, NOT StopAsync (which would cancel the entire host)
- OnStoppedLeading must be idempotent: sets _isLeader = false only, never destructive teardown
- Startup validator: CFG-04 — heartbeat lease name must differ from leadership lease name (prevents 409 Conflict)
- Startup validator: CFG-02 — warn/throw when PreferredNode configured but PHYSICAL_HOSTNAME is empty
- Phase 86 (writer path): readiness gate selected — IHostApplicationLifetime.ApplicationStarted.Register callback, volatile bool _isSchedulerReady on job (RESOLVED in 86-01)
- Phase 5 (voluntary yield) has open question: LeaderElector state after mid-renewal cancellation — resourceVersion staleness risk

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
v2.3 and v2.4 decisions archived to milestones/v2.3-ROADMAP.md and milestones/v2.4-ROADMAP.md.

### Pending Todos

None.

### Blockers/Concerns

- None. All 89 phases complete. v3.0 Preferred Leader Election is feature-complete.
- Deployment manifest ready: `kubectl apply -f deploy/k8s/snmp-collector/deployment.yaml`
- Anti-affinity rule ensures one pod per node (matches replicas: 3 on a 3-node cluster).

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 085 | Reorder tenant table columns and batch metrics at exit | 2026-03-23 | 934c413 | [085-tenant-table-reorder-and-metric-timing](./quick/085-tenant-table-reorder-and-metric-timing/) |
| 086 | Remove 5 redundant ops dashboard panels | 2026-03-23 | fded5bd | [086-remove-redundant-ops-panels](./quick/086-remove-redundant-ops-panels/) |
| 087 | Switch Grafana to manual dashboard management | 2026-03-23 | 8c5221f | [087-grafana-manual-dashboard-mgmt](./quick/087-grafana-manual-dashboard-mgmt/) |
| 088 | Move dispatch after state decision | 2026-03-23 | 25f07b7 | [088-move-dispatch-after-decide](./quick/088-move-dispatch-after-decide/) |
| 089 | Append index to duplicate tenant names instead of skipping | 2026-03-23 | 8873c2e | [089-duplicate-tenant-name-index](./quick/089-duplicate-tenant-name-index/) |
| 090 | Enable table footer row counts on all 4 table panels | 2026-03-24 | 32ed4dd | [090-table-footer-row-counts](./quick/090-table-footer-row-counts/) |
| 091 | Add evaluation pipeline logging to SnapshotJob | 2026-03-24 | dce2a00 | [091-snapshot-evaluation-logging](./quick/091-snapshot-evaluation-logging/) |
| 092 | Startup probe device check and watcher naming | 2026-03-25 | 96faff7 | [092-startup-probe-device-check-and-watcher-naming](./quick/092-startup-probe-device-check-and-watcher-naming/) |
| 093 | Rename HeartbeatJob to SnmpHeartbeatJob | 2026-03-26 | cf77510 | [093-rename-heartbeatjob-to-snmpheartbeatjob](./quick/093-rename-heartbeatjob-to-snmpheartbeatjob/) |

## Session Continuity

Last session: 2026-03-26
Stopped at: Completed quick-093 — renamed HeartbeatJob to SnmpHeartbeatJob
Resume file: None
