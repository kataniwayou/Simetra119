# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-25)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v3.0 Preferred Leader Election — Phase 86: PreferredHeartbeatService Writer Path

## Current Position

Phase: 86 of 89 (PreferredHeartbeatService Writer Path)
Plan: 0 of TBD in current phase
Status: Ready to plan
Last activity: 2026-03-26 — Phase 85 complete (2 plans, 500 tests, 13 new)

Progress: [█████████████████░░░] ~85%

## Performance Metrics

**Velocity:**
- Total plans completed: 159 (v1.0 through v3.0 Phase 85, including quick tasks)
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
- Phase 86 (writer path): readiness gate mechanism not yet selected — three options: ApplicationStarted vs IHealthCheckService poll vs TaskCompletionSource<bool>
- Phase 5 (voluntary yield) has open question: LeaderElector state after mid-renewal cancellation — resourceVersion staleness risk

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
v2.3 and v2.4 decisions archived to milestones/v2.3-ROADMAP.md and milestones/v2.4-ROADMAP.md.

### Pending Todos

None.

### Blockers/Concerns

- Phase 86 (writer path): Readiness gate mechanism not yet selected — three options exist. Resolve via brief code inspection of ReadinessHealthCheck before starting 86-01.
- Phase 88 (voluntary yield): LeaderElector behavior after mid-renewal cancellation unconfirmed. May need a minimal unit test spike before modifying the live election loop.

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

## Session Continuity

Last session: 2026-03-26
Stopped at: Phase 85 fully complete (85-01 + 85-02) — ready to plan Phase 86
Resume file: None
