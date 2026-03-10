# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-10)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.5 Priority Vector Data Layer — Complete

## Current Position

Phase: 29 — fifth of 5 in v1.5 (K8s Deployment and E2E Validation) — COMPLETE
Plan: All plans complete
Status: Milestone v1.5 complete
Last activity: 2026-03-11 — Completed quick task 045: Remove tenant vector validation

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2, 2/2 v1.3, 11/11 v1.4 | [##########] 9/9 v1.5

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-08 |
| v1.2 Operational Enhancements | 15-16 | 8 | 2026-03-08 |
| v1.3 Grafana Dashboards | 18-19 | 2 | 2026-03-09 |
| v1.4 E2E System Verification | 20-24 | 11 | 2026-03-09 |
| v1.5 Priority Vector Data Layer | 25-29 | 9 | 2026-03-10 |

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
- TenantVectorRegistry.Reload() value carry-over: uses (ip, port, metricName) 3-tuple key via RoutingKey; copies MetricSlot value via ReadSlot()/WriteValue(), never copies holder object
- TenantOptions has no Id property; TenantVectorRegistry auto-generates tenant-{index} Ids at Reload() time (Q043)
- Heartbeat exports as snmp_gauge{device_name="Simetra", metric_name="Heartbeat", snmp_type="counter32"} with incrementing Counter32 value (Q041)
- ValueExtractionBehavior is 5th in pipeline chain; sets ExtractedValue + ExtractedStringValue once — consumers read pre-extracted (D27-01)
- OidMapService.MergeWithHeartbeatSeed called in both constructor and UpdateMap — heartbeat seed survives every ConfigMap reload (D27-01)
- OtelMetricHandler reads ExtractedValue/ExtractedStringValue; uses TypeCode.ToString().ToLowerInvariant() for snmpType label (D27-01)
- TenantVectorFanOutBehavior is 6th in pipeline chain; next() is outside try/catch — fan-out exceptions never kill OTel export (D27-02)
- MetricSlot.TypeCode (SnmpType) preserved through WriteValue and Reload carry-over; consumers use TypeCode to distinguish Value vs StringValue (D27-02)
- snmp.tenantvector.routed counter increments once per slot write with device_name tag (D27-02)
- PipelineIntegrationTests must register ITenantVectorRegistry when using AddSnmpPipeline() (D27-02)
- simetra-tenantvector ConfigMap uses bare JSON { "Tenants": [] } — NOT { "TenantVector": { ... } }; section wrapper is IConfiguration-only (D28-02)
- TenantVectorWatcherService injects TenantVectorRegistry (concrete), not ITenantVectorRegistry — Reload() is not on the interface (D28-01)
- Concrete-first validator DI: AddSingleton<TenantVectorOptionsValidator>() + AddSingleton<IValidateOptions<T>>(sp => sp.GetRequiredService<TenantVectorOptionsValidator>()) ensures single instance (D28-01); validator is now no-op (Q045)
- Local dev tenantvector.json uses IConfiguration section wrapper; JsonDocument.Parse + TryGetProperty("TenantVector") extracts inner object before deserialization (D28-01)
- simetra-tenantvector ConfigMap uses DNS names (same as simetra-devices); TenantVectorRegistry.ResolveIp() maps ConfigAddress to ResolvedIp via IDeviceRegistry at Reload() time (Q044)
- E2E scenario 28 applies tenantvector ConfigMap directly (no ClusterIP derivation or sed substitution), hot-reload uses obp_r3_power_L1/obp_r4_power_L1 (valid oidmap metrics) for 4th tenant obp-poll-2 (Q044)
- kubectl.sh snapshot/restore_configmaps now includes simetra-tenantvector; report.sh Tenant Vector category covers indices 33-36 (D29-02)
- IntervalSeconds removed from tenant vector ConfigMap; TenantVectorRegistry.DeriveIntervalSeconds() resolves via IDeviceRegistry.TryGetByIpPort + IOidMapService.Resolve at Reload() time (Q042)

### Known Tech Debt

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 041 | Heartbeat as Counter32 snmp_gauge with device_name=Simetra | 2026-03-10 | 1c3ad32 | [041-heartbeat-counter32-simetra-device](./quick/041-heartbeat-counter32-simetra-device/) |
| 042 | Remove IntervalSeconds from tenant vector config, derive from DeviceRegistry | 2026-03-11 | bd4ad96 | [042-remove-intervalseconds-from-tenant-config](./quick/042-remove-intervalseconds-from-tenant-config/) |
| 043 | Remove tenant Id from ConfigMap, auto-generate positional Ids in registry | 2026-03-11 | 21b4017 | [043-remove-tenant-id-from-configmap](./quick/043-remove-tenant-id-from-configmap/) |
| 044 | Replace placeholder IPs with DNS names in tenant vector ConfigMaps | 2026-03-11 | 1883041 | [044-replace-placeholder-ips-with-dns](./quick/044-replace-placeholder-ips-with-dns/) |
| 045 | Remove tenant vector validation (no-op validator) | 2026-03-11 | ac07691 | [045-remove-tenant-vector-validation](./quick/045-remove-tenant-vector-validation/) |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-11
Stopped at: Completed quick task 045
Resume file: None
