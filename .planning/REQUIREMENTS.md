# Requirements: SNMP Monitoring System

**Defined:** 2026-03-14
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.7 Requirements — Configuration Consistency & Tenant Commands

Requirements for CommunityString validation, self-describing tenant entries, tenant command data model, config rename, and code cleanup.

**Operator config ordering (documented, not enforced):** oidmaps/commandmaps → devices → tenants. Each file has its own independent watcher. No cross-watcher cascading reloads.

### CommunityString Foundation

- [ ] **CS-01**: `DeviceOptions.Name` renamed to `DeviceOptions.CommunityString`, holding the full community string value (e.g. `"Simetra.NPB-01"`) — no implicit derivation
- [ ] **CS-02**: `DeviceInfo.Name` derived from `CommunityString` via `CommunityStringHelper.TryExtractDeviceName()` at device load time — all downstream consumers (Prometheus labels, Quartz job keys, logs, trap routing) continue using the extracted short name
- [ ] **CS-03**: CommunityString validated at load time on all config entries (devices and tenants) — must be non-null/non-empty, start with `Simetra.` (case-sensitive Ordinal), have a non-empty non-whitespace suffix
- [ ] **CS-04**: Invalid CommunityString on device entry = skip entire device with Error-level structured log; invalid on tenant metric/command entry = skip that entry with Error-level structured log; other entries in same file unaffected
- [ ] **CS-05**: MetricPollJob uses the explicit `DeviceInfo.CommunityString` directly — the `?? CommunityStringHelper.DeriveFromDeviceName()` fallback is removed
- [ ] **CS-06**: Trap listener `Simetra.*` pattern extraction verified consistent with new CommunityString approach — `SnmpTrapListenerService` continues extracting device name from community string and routing via `DeviceRegistry.TryGetDeviceByName()`
- [ ] **CS-07**: Operator config ordering documented: oidmaps/commandmaps → devices → tenants; each file has independent watcher; operator responsible for alignment across files

### Tenant Structure

- [ ] **TEN-01**: `MetricSlotOptions` retains current shape (Ip, Port, MetricName, TimeSeriesSize) plus optional `IntervalSeconds` — CommunityString resolved from DeviceRegistry by IP+Port at load time, not stored in tenant config
- [ ] **TEN-02**: New `Commands` list on `TenantOptions` — each `CommandSlotOptions` entry has Ip, Port, CommandName, Value (string, required non-empty), ValueType (string) — CommunityString resolved from DeviceRegistry by IP+Port at load time
- [ ] **TEN-03**: `ValueType` validated against allowed set `{ "Integer32", "IpAddress", "OctetString" }` at load time — invalid ValueType = skip command entry with Error log
- [ ] **TEN-04**: `TenantVectorRegistry` constructor removes `IOidMapService` dependency — keeps `IDeviceRegistry` for CommunityString resolution by IP+Port
- [ ] **TEN-05**: Unresolvable MetricName in tenant config (not in current OID map) = skip entry with Error-level structured log; other entries in same tenant unaffected
- [ ] **TEN-06**: Unresolvable CommandName in tenant config (not in current command map) = store entry as-is with Debug log — resolution deferred to execution time
- [ ] **TEN-07**: Tenant metric/command entry whose IP+Port has no matching device in DeviceRegistry = skip entry with Error-level structured log
- [ ] **TEN-08**: `TenantVectorOptionsValidator` activated with real structural validation — non-null/non-empty Ip, MetricName/CommandName; Port 1–65535; TimeSeriesSize >= 1; non-empty Value on commands
- [ ] **TEN-09**: Optional `IntervalSeconds` field on `MetricSlotOptions` — stored in `MetricSlotHolder` for observability; defaults to 0 if absent
- [ ] **TEN-10**: Optional `Name` field on `TenantOptions` — used in log context instead of synthetic `tenant-{index}` ID; falls back to auto-generated ID if absent
- [ ] **TEN-11**: Structured log fields on tenant entry skip events include EntryType, EntryIndex, Reason (unresolvable MetricName / device not found by IP+Port / invalid ValueType / missing role), ConfigMap source — suitable for Loki alerting
- [ ] **TEN-12**: `MetricSlotOptions` gains required `Role` property — valid values `"Evaluate"` or `"Resolved"`, validated at load time; invalid or missing Role = skip metric entry with Error log
- [ ] **TEN-13**: Tenant loading requires at least one metric with Role="Resolved" AND at least one with Role="Evaluate" AND at least one command entry — missing any = skip entire tenant with Error log

### Device Registry Consistency

- [ ] **DEV-08**: Poll group where ALL MetricNames are unresolvable = skip job registration entirely (no Quartz job created for zero-OID groups)
- [ ] **DEV-09**: CommunityString validation on `DeviceOptions` at load time — invalid CommunityString = skip device entirely (not registered in DeviceRegistry, no poll jobs, no trap routing)
- [ ] **DEV-10**: Duplicate CommunityString (duplicate extracted device name) across devices = validation error with structured log — prevents silent `_byName` dictionary overwrite

### Config Rename

- [ ] **REN-01**: `tenantvector.json` → `tenants.json`: local dev file, ConfigMap name (`simetra-tenants`), ConfigMap key (`tenants.json`), C# constants (`ConfigMapName`, `ConfigKey`), `TenantVectorOptions.SectionName` → `"Tenants"`
- [ ] **REN-02**: All rename references updated atomically — K8s manifests (standalone + production), local dev config path, E2E test scripts, inline kubectl heredocs, deployment.yaml volume references
- [ ] **REN-03**: `oidmaps.json` → `oid_metric_map.json`: local dev file, ConfigMap name (`simetra-oid-metric-map`), ConfigMap key (`oid_metric_map.json`), C# constants in `OidMapWatcherService` (`ConfigMapName`, `ConfigKey`), all K8s manifests and E2E references
- [ ] **REN-04**: `commandmaps.json` → `oid_command_map.json`: local dev file, ConfigMap name (`simetra-oid-command-map`), ConfigMap key (`oid_command_map.json`), C# constants in `CommandMapWatcherService` (`ConfigMapName`, `ConfigKey`), all K8s manifests and E2E references

### Code Cleanup

- [ ] **CLN-01**: `TenantVectorRegistry.DeriveIntervalSeconds()` method removed (IntervalSeconds comes from config); `ResolveIp()` stays (still needed for DNS→IP routing key translation)
- [ ] **CLN-02**: `IOidMapService` constructor parameter removed from `TenantVectorRegistry`; `IDeviceRegistry` stays for CommunityString resolution
- [ ] **CLN-03**: CommunityString derivation fallback removed from `MetricPollJob` — no more `?? DeriveFromDeviceName(device.Name)` path

## Out of Scope

| Feature | Reason |
|---------|--------|
| SNMP SET command execution | Command entries are loaded and validated only; execution is a future milestone |
| Cross-validation between config files | Independent watchers; operator responsible for alignment; no cross-watcher coupling |
| Reverse CommunityString lookup in DeviceRegistry | Trap listener already extracts name and looks up by name — no new index needed |
| Validate tenant Device field matches DeviceRegistry | Device field is a label, not a foreign key; cross-validation recreates coupling |
| Dual ConfigMap watch for backward compat during rename | Clean break; atomic rename is simpler than dual-watch |
| CommandName validation against CommandMap at tenant load time | CommandMap hot-reloads independently; validation belongs at execution time |
| Community string auto-discovery | Static credentials configured by operator; probing would be a security violation |
| Enforced config ordering | Operator responsibility; watchers are independent by design |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CS-01 | Phase 33 | Complete |
| CS-02 | Phase 33 | Complete |
| CS-03 | Phase 34 | Pending |
| CS-04 | Phase 34 | Pending |
| CS-05 | Phase 33 | Complete |
| CS-06 | Phase 34 | Pending |
| CS-07 | Phase 34 | Pending |
| TEN-01 | Phase 33 | Complete |
| TEN-02 | Phase 33 | Complete |
| TEN-03 | Phase 34 | Pending |
| TEN-04 | Phase 35 | Pending |
| TEN-05 | Phase 35 | Pending |
| TEN-06 | Phase 35 | Pending |
| TEN-07 | Phase 34 | Pending |
| TEN-08 | Phase 35 | Pending |
| TEN-09 | Phase 33 | Complete |
| TEN-10 | Phase 33 | Complete |
| TEN-11 | Phase 34 | Pending |
| DEV-08 | Phase 34 | Pending |
| DEV-09 | Phase 34 | Pending |
| DEV-10 | Phase 34 | Pending |
| REN-01 | Phase 36 | Pending |
| REN-02 | Phase 36 | Pending |
| REN-03 | Phase 36 | Pending |
| REN-04 | Phase 36 | Pending |
| CLN-01 | Phase 35 | Pending |
| CLN-02 | Phase 35 | Pending |
| CLN-03 | Phase 34 | Pending |
| TEN-12 | Phase 33 | Complete |
| TEN-13 | Phase 34 | Pending |

**Coverage:**
- v1.7 requirements: 30 total
- Mapped to phases: 30
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-14*
*Last updated: 2026-03-14 after v1.7 roadmap — all 28 requirements mapped to Phases 33-36*
