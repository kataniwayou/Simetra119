# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-09)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.5 Priority Vector Data Layer

## Current Position

Phase: Not started (defining requirements)
Plan: —
Status: Defining requirements for v1.5
Last activity: 2026-03-10 -- Milestone v1.5 started

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2, 2/2 v1.3, 11/11 v1.4

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-08 |
| v1.2 Operational Enhancements | 15-16 | 8 | 2026-03-08 |
| v1.3 Grafana Dashboards | 18-19 | 2 | 2026-03-09 |
| v1.4 E2E System Verification | 20-24 | 11 | 2026-03-09 |

See `.planning/MILESTONES.md` for details.
See `.planning/milestones/` for archived roadmaps and requirements.

## Accumulated Context

### Key Architectural Facts

- MediatR 12.5.0 (MIT) -- do NOT upgrade to v13+ (RPL-1.5 license)
- Two-meter architecture: MeterName for all instances, LeaderMeterName for leader only
- Community string convention: Simetra.{DeviceName} for both auth and device identity
- Split config: simetra-oidmaps ConfigMap + simetra-devices ConfigMap + simetra-config
- K8s directory mount at /app/config (no subPath) enables ConfigMap hot-reload
- DeviceRegistry primary key: (IP, Port); secondary: Name (trap listener compatibility)
- Quartz job key format: metric-poll-{ip}_{port}-{pollIndex} (underscore separator)

### Known Tech Debt

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 037 | IP+Port as primary device identity | 2026-03-10 | af973c7 | [037-ip-port-primary-device-identity](./quick/037-ip-port-primary-device-identity/) |
| 038 | Configurable poll timeout percentage | 2026-03-10 | 4984002 | [038-configurable-poll-timeout-multiplier](./quick/038-configurable-poll-timeout-multiplier/) |
| 039 | Add Source column to business dashboard | 2026-03-10 | d0e1270 | [039-add-source-column-to-business-dashboard](./quick/039-add-source-column-to-business-dashboard/) |
| 040 | Source distribution panels (rate + pie) | 2026-03-10 | f57cc5c | [040-source-distribution-panels](./quick/040-source-distribution-panels/) |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-10
Stopped at: Completed quick-037 (IP+Port as primary device identity)
Resume file: None
