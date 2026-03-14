# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-14)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.7 Configuration Consistency & Tenant Commands — Phase 33 ready to plan

## Current Position

Phase: 33 of 36 (Config Model Additions)
Plan: 01 of 2
Status: In progress
Last activity: 2026-03-14 — Completed 33-01 (DeviceOptions.Name → CommunityString rename)

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2, 2/2 v1.3, 11/11 v1.4 | [##########] 9/9 v1.5 | [##########] 8/8 v1.6 | [#         ] 1/? v1.7

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-08 |
| v1.2 Operational Enhancements | 15-16 | 8 | 2026-03-08 |
| v1.3 Grafana Dashboards | 18-19 | 2 | 2026-03-09 |
| v1.4 E2E System Verification | 20-24 | 11 | 2026-03-09 |
| v1.5 Priority Vector Data Layer | 25-29 | 9 | 2026-03-10 |
| v1.6 Organization & Command Map Foundation | 30-32 | 8 | 2026-03-13 |

See `.planning/MILESTONES.md` for details.

## Accumulated Context

### Key Architectural Facts

- All pods maintain tenant vector state (no leader gating) — decision from spec
- Routing key: (ip, port, metric_name) — after OidResolution sets MetricName
- Fan-out catches own exceptions, always calls next() — never kills OTel export
- FrozenDictionary atomic swap for registry + routing index
- MetricSlotHolder uses Volatile.Read/Write; MetricSlot is slim 3-field sample record
- MetricSlotHolder stores ImmutableArray cyclic time series capped at TimeSeriesSize
- TenantVectorRegistry._groups and ._routingIndex use volatile keyword
- OidMapService._reverseMap volatile FrozenDictionary for metric-name-to-OID reverse lookup
- CommandMapService null-return contract: unknown entries return null (no sentinel)
- commandmaps.json: array-of-objects [{Oid, CommandName}] format; ConfigMap name "simetra-commandmaps"
- oidmaps.json: array-of-objects [{Oid, MetricName}] format; all config files use MetricNames (human-readable)
- C# model: MetricPollOptions→PollOptions, MetricPolls→Polls, Oids→MetricNames; MetricPollInfo.Oids retained
- DeviceOptions.CommunityString (not Name) is primary device identifier; DeviceInfo.Name derived at load time via TryExtractDeviceName
- DeviceRegistry: invalid CommunityString logs error and skips device (no throw); consistent for constructor + ReloadAsync
- All config JSON/YAML: "CommunityString": "Simetra.XXX" format; "Name" field eliminated from device entries

### v1.7 Pre-Phase Decisions (to resolve in plans)

- DNS resolution in TenantVectorRegistry.Reload() after removing IDeviceRegistry: async Dns.GetHostAddressesAsync vs IP-only requirement — must be named decision in Phase 35 plan
- TenantVectorOptions.SectionName with file rename: keep "TenantVector" (simpler) vs rename to "Tenants" — must be named decision in Phase 36 plan
- Value/ValueType parse validation at load time vs execution time — recommend load-time for early operator feedback (SET execution is out of scope)

### Known Tech Debt

None.

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-14T19:48Z
Stopped at: Completed 33-01-PLAN.md (DeviceOptions.Name → CommunityString)
Resume file: None
