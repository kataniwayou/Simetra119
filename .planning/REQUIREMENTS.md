# Requirements: SNMP Monitoring System

**Defined:** 2026-03-13
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.6 Requirements — Organization & Command Map Foundation

Requirements for OID map integrity validation, human-name device configuration, and SNMP command map lookup infrastructure.

### OID Map Integrity

- [ ] **MAP-01**: OidMapService detects duplicate OID keys at load time and logs structured warning per duplicate (OID, conflicting names, retained name)
- [ ] **MAP-02**: OidMapService detects duplicate metric name values at load time and logs structured warning per duplicate (name, conflicting OIDs)
- [ ] **MAP-03**: OidMapService maintains a reverse index (FrozenDictionary name → OID), rebuilt atomically alongside forward map on every UpdateMap call
- [ ] **MAP-04**: IOidMapService exposes ResolveToOid(string metricName) returning OID string or null

### Device Config Human Names

- [ ] **DEV-01**: MetricPollOptions has a Metrics property accepting human-readable metric names alongside existing Oids
- [ ] **DEV-02**: At device config load, each Metrics[] entry is resolved to its OID via IOidMapService.ResolveToOid; resolved OIDs populate the runtime poll list
- [ ] **DEV-03**: Unresolvable metric names log a structured warning (device name, metric name) and are skipped — no poll job registered for that entry
- [ ] **DEV-04**: Oids[] and Metrics[] coexist in the same poll group; both contribute to the runtime OID list
- [ ] **DEV-05**: Entries in Metrics[] that look like raw OIDs (digits and dots only) log a warning suggesting the Oids field instead
- [ ] **DEV-06**: When OID map changes, device config is re-resolved against the new map — previously-unresolvable names that now resolve trigger poll job registration
- [ ] **DEV-07**: Reload diff logging includes metric name translation changes (newly resolved, newly unresolvable)

### Command Map Infrastructure

- [ ] **CMD-01**: commandmaps.json uses OID → command name format (mirrors oidmaps.json)
- [ ] **CMD-02**: CommandMapService singleton with forward (OID → name) and reverse (name → OID) FrozenDictionary indexes, atomic volatile swap on reload
- [ ] **CMD-03**: CommandMapWatcherService watches simetra-commandmaps ConfigMap via K8s API with hot-reload
- [ ] **CMD-04**: CommandMapWatcherService falls back to local filesystem loading in non-K8s dev mode
- [ ] **CMD-05**: CommandMapService detects duplicate OID keys and duplicate command names at load time with structured warnings
- [ ] **CMD-06**: CommandMapService logs structured diff on reload (added/removed/changed entries) and entry count

## Out of Scope

| Feature | Reason |
|---------|--------|
| SNMP SET execution | Command map is lookup-only this milestone; execution is a future milestone |
| Command authorization / access control | No commands executed, nothing to authorize |
| Command parameter schemas / typed parameters | Type metadata is MIB-level knowledge, out of scope for lookup table |
| Mandatory migration of devices.json to human names | Oids[] and Metrics[] coexist; operators migrate at their own pace |
| Separate per-device-type command maps | Single simetra-commandmaps ConfigMap mirrors oidmaps pattern |
| Command map HTTP API or Prometheus export | In-process lookup only; not telemetry |
| Hard startup failure on unresolvable metric names | Soft warning + skip is correct; hard failure too severe |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| MAP-01 | TBD | Pending |
| MAP-02 | TBD | Pending |
| MAP-03 | TBD | Pending |
| MAP-04 | TBD | Pending |
| DEV-01 | TBD | Pending |
| DEV-02 | TBD | Pending |
| DEV-03 | TBD | Pending |
| DEV-04 | TBD | Pending |
| DEV-05 | TBD | Pending |
| DEV-06 | TBD | Pending |
| DEV-07 | TBD | Pending |
| CMD-01 | TBD | Pending |
| CMD-02 | TBD | Pending |
| CMD-03 | TBD | Pending |
| CMD-04 | TBD | Pending |
| CMD-05 | TBD | Pending |
| CMD-06 | TBD | Pending |

**Coverage:**
- v1.6 requirements: 17 total
- Mapped to phases: 0
- Unmapped: 17

---
*Requirements defined: 2026-03-13*
*Last updated: 2026-03-13 after initial definition*
