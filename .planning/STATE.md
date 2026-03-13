# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-13)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.6 Organization & Command Map Foundation

## Current Position

Phase: 31 — Human-Name Device Config (complete)
Plan: 03 of 3 complete
Status: Phase complete
Last activity: 2026-03-13 — Completed 31-03-PLAN.md (config file migration to Polls/MetricNames, all environments)

Progress: [####################] 48/48 v1.0, 10/10 v1.1, 8/8 v1.2, 2/2 v1.3, 11/11 v1.4 | [##########] 9/9 v1.5 | [#########.] 3/3 phases v1.6 (Phase 31 complete)

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |
| v1.1 Device Simulation | 11-14 | 10 | 2026-03-08 |
| v1.2 Operational Enhancements | 15-16 | 8 | 2026-03-08 |
| v1.3 Grafana Dashboards | 18-19 | 2 | 2026-03-09 |
| v1.4 E2E System Verification | 20-24 | 11 | 2026-03-09 |
| v1.5 Priority Vector Data Layer | 25-29 | 9 | 2026-03-10 |
| v1.6 Organization & Command Map Foundation | 30-32 | TBD | In progress |

See `.planning/MILESTONES.md` for details.

## Accumulated Context

### Key Architectural Facts

- All pods maintain tenant vector state (no leader gating) — decision from spec
- Routing key: (ip, port, metric_name) — after OidResolution sets MetricName
- Port resolved via DeviceRegistry.TryGetDeviceByName — no changes to SnmpOidReceived
- Fan-out catches own exceptions, always calls next() — never kills OTel export
- FrozenDictionary atomic swap for registry + routing index
- MetricSlotHolder uses Volatile.Read/Write (NOT volatile keyword — CS0420 conflict); plain field is correct
- MetricSlot is a slim 3-field sample record: (Value, StringValue, Timestamp); TypeCode and Source promoted to MetricSlotHolder properties (Q053)
- MetricSlotHolder stores ImmutableArray cyclic time series capped at TimeSeriesSize (default 1); SeriesBox reference wrapper enables Volatile semantics (Q053)
- MetricSlotHolder.CopyFrom() bulk-loads series + TypeCode + Source during TenantVectorRegistry reload carry-over (Q053)
- MetricSlotOptions.TimeSeriesSize configures per-metric series depth; passed to MetricSlotHolder constructor (Q053, moved Q054)
- TenantVectorRegistry._groups and ._routingIndex use volatile keyword (reference reads, not ref-passed — no CS0420)
- Zero new NuGet packages needed
- FrozenSet<string> for O(1) metric name containment in OidMapService (D25-01)
- OidMapService._reverseMap volatile FrozenDictionary for metric-name-to-OID reverse lookup; built from post-heartbeat-seed map in constructor and UpdateMap (D30-01)
- RoutingKeyComparer.Instance singleton — pass explicitly to FrozenDictionary constructor
- PriorityGroup is not sealed (C# records cannot be declared sealed)
- TenantVectorRegistry.Reload() value carry-over: uses (ip, port, metricName) 3-tuple key via RoutingKey; copies MetricSlot value via ReadSlot()/WriteValue(), never copies holder object
- TenantOptions has no Id property; TenantVectorRegistry auto-generates tenant-{index} Ids at Reload() time (Q043)
- Heartbeat tenant hardcoded at int.MinValue priority in TenantVectorRegistry.Reload(); always present, operator responsible for not using int.MinValue (Q047)
- TenantVectorFanOutBehavior bypasses DeviceRegistry for HeartbeatDeviceName, routes via (127.0.0.1, 0, metricName) directly (Q047)
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

### v1.6 Architectural Decisions (pending confirmation in phase plans)

- Duplicate validation runs in OidMapWatcherService BEFORE calling OidMapService.UpdateMap — prevents phantom diff log entries (Pitfall 1) -- IMPLEMENTED D30-02
- OidMapWatcherService.ValidateAndParseOidMap uses JsonDocument.Parse (not JsonSerializer.Deserialize) with 3-pass skip-both duplicate detection (D30-02)
- Strategy A for human-name resolution: resolve at device-load time in DeviceWatcherService, not at poll time in MetricPollJob — avoids stale OIDs after OID map hot-reload (Pitfall 4)
- ConfigMap name for command map: "simetra-commandmaps" — mirrors "simetra-oidmaps" naming convention; must be locked as a constant before Phase 32 coding starts (Pitfall 12)
- Validation comparer: StringComparer.OrdinalIgnoreCase in all duplicate detection passes — matches runtime FrozenDictionary semantics (Pitfall 10)
- Validation runs against merged dictionary (after MergeWithHeartbeatSeed); "Heartbeat" rejected as user-supplied metric name value (Pitfall 13)
- OidMap format: array-of-objects [{Oid, MetricName}] replaces flat {OID: name} dict; ValidateAndParseOidMap uses EnumerateArray (D31-01)
- C# model rename complete: MetricPollOptions->PollOptions, MetricPolls->Polls, Oids->MetricNames; MetricPollInfo.Oids retained (holds resolved OIDs at runtime) (D31-01)
- simetra-oidmaps.yaml now has 105 entries (6 previously-missing entries added: obp_device_type/sw_version/serial + npb_model/serial/sw_version) (D31-01)
- DeviceRegistry.BuildPollGroups() resolves MetricNames to OIDs via IOidMapService.ResolveToOid at load time; null return = skip + LogWarning; device registered even with zero resolved OIDs (D31-02)
- IOidMapService injected as 2nd constructor param in DeviceRegistry; DI resolves automatically -- ServiceCollectionExtensions unchanged (D31-02)
- All device config files (local, K8s, production, E2E fixtures) now use Polls/MetricNames with human-readable names; production configmap.yaml oidmaps section converted to array format with 94 entries (D31-03)
- e2e-sim-unmapped-configmap.yaml uses e2e_intentionally_missing (absent from all oidmaps) to preserve unresolvable-name warning test path (D31-03)
- .original-devices-configmap.yaml is gitignored (runtime-generated by scenario 06 save_configmap) (D31-03)

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
| 046 | Add tenant vector routed metric to operations dashboard | 2026-03-11 | 03e8b98 | [046-add-tenantvector-metric-to-dashboard](./quick/046-add-tenantvector-metric-to-dashboard/) |
| 047 | Hardcode heartbeat as highest priority tenant (int.MinValue) | 2026-03-11 | 76b51d0 | [047-hardcode-heartbeat-highest-priority-tenant](./quick/047-hardcode-heartbeat-highest-priority-tenant/) |
| 048 | SNMP pipeline duration histogram with P99 Grafana panel | 2026-03-11 | fb0d399 | [048-snmp-pipeline-duration-histogram](./quick/048-snmp-pipeline-duration-histogram/) |
| 049 | Gauge/info duration histograms with P99 Grafana columns | 2026-03-11 | 9459222 | [049-gauge-info-duration-histograms](./quick/049-gauge-info-duration-histograms/) |
| 050 | Pipeline timing behavior (TimingBehavior as outermost MediatR behavior) | 2026-03-11 | c337652 | [050-pipeline-timing-behavior](./quick/050-pipeline-timing-behavior/) |
| 051 | Remove snmp.pipeline.duration histogram and Pipeline Duration P99 panel | 2026-03-11 | 53d5184 | [051-remove-pipeline-duration-metric](./quick/051-remove-pipeline-duration-metric/) |
| 052 | Add SnmpSource to MetricSlot and MetricSlotHolder.WriteValue | 2026-03-11 | e371cfe | [052-add-source-to-metricslot](./quick/052-add-source-to-metricslot/) |
| 053 | MetricSlot time series refactor (ImmutableArray cyclic series, CopyFrom) | 2026-03-12 | 78c3c19 | [053-metricslot-time-series-refactor](./quick/053-metricslot-time-series-refactor/) |
| 054 | Move TimeSeriesSize from TenantOptions to MetricSlotOptions (per-metric) | 2026-03-12 | 1d7386e | [054-move-timeseriessize-to-metric-slot](./quick/054-move-timeseriessize-to-metric-slot/) |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-13
Stopped at: Completed 31-03-PLAN.md (config file migration to Polls/MetricNames, all environments)
Resume file: None
