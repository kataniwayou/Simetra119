# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-10)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.5 Priority Vector Data Layer — Phase 28 (ConfigMap Watcher and Local Dev)

## Current Position

Phase: 28 — fourth of 5 in v1.5 (ConfigMap Watcher and Local Dev)
Plan: 02 of 5 in phase 28
Status: In progress
Last activity: 2026-03-10 — Completed 28-02 (simetra-tenantvector ConfigMap added to production manifests)

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2, 2/2 v1.3, 11/11 v1.4 | [######___] 6/9 v1.5

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-08 |
| v1.2 Operational Enhancements | 15-16 | 8 | 2026-03-08 |
| v1.3 Grafana Dashboards | 18-19 | 2 | 2026-03-09 |
| v1.4 E2E System Verification | 20-24 | 11 | 2026-03-09 |

See `.planning/MILESTONES.md` for details.

## Accumulated Context

### Key Architectural Facts

- All pods maintain tenant vector state (no leader gating) — decision from spec
- Routing key: (ip, port, metric_name) — after OidResolution sets MetricName
- Port resolved via DeviceRegistry.TryGetDeviceByName — no changes to SnmpOidReceived
- Fan-out catches own exceptions, always calls next() — never kills OTel export
- FrozenDictionary atomic swap for registry + routing index
- MetricSlotHolder uses Volatile.Read/Write (NOT volatile keyword — CS0420 conflict); plain field is correct
- TenantVectorRegistry._groups and ._routingIndex use volatile keyword (reference reads, not ref-passed — no CS0420)
- Zero new NuGet packages needed
- FrozenSet<string> for O(1) metric name containment in OidMapService (D25-01)
- RoutingKeyComparer.Instance singleton — pass explicitly to FrozenDictionary constructor
- PriorityGroup is not sealed (C# records cannot be declared sealed)
- TenantVectorRegistry.Reload() value carry-over: copies MetricSlot value via ReadSlot()/WriteValue(), never copies holder object
- IsHeartbeat removed from SnmpOidReceived — heartbeat flows as normal metric with MetricName="heartbeat" (D27-01)
- ValueExtractionBehavior is 5th in pipeline chain; sets ExtractedValue + ExtractedStringValue once — consumers read pre-extracted (D27-01)
- OidMapService.MergeWithHeartbeatSeed called in both constructor and UpdateMap — heartbeat seed survives every ConfigMap reload (D27-01)
- OtelMetricHandler reads ExtractedValue/ExtractedStringValue; uses TypeCode.ToString().ToLowerInvariant() for snmpType label (D27-01)
- TenantVectorFanOutBehavior is 6th in pipeline chain; next() is outside try/catch — fan-out exceptions never kill OTel export (D27-02)
- MetricSlot.TypeCode (SnmpType) preserved through WriteValue and Reload carry-over; consumers use TypeCode to distinguish Value vs StringValue (D27-02)
- snmp.tenantvector.routed counter increments once per slot write with device_name tag (D27-02)
- PipelineIntegrationTests must register ITenantVectorRegistry when using AddSnmpPipeline() (D27-02)
- simetra-tenantvector ConfigMap uses bare JSON { "Tenants": [] } — NOT { "TenantVector": { ... } }; section wrapper is IConfiguration-only (D28-02)

### Known Tech Debt

None.

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-10T20:17:59Z
Stopped at: Completed 28-02-PLAN.md
Resume file: None
