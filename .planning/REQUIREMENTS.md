# Requirements: SNMP Monitoring System

**Defined:** 2026-03-13
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.6 Requirements — Organization & Command Map Foundation

Requirements for OID map integrity validation, human-name device configuration, and SNMP command map lookup infrastructure.

### OID Map Integrity

- [x] **MAP-01**: OidMapService detects duplicate OID keys at load time and logs structured warning per duplicate (OID, conflicting names, both skipped)
- [x] **MAP-02**: OidMapService detects duplicate metric name values at load time and logs structured warning per duplicate (name, conflicting OIDs, both skipped)
- [x] **MAP-03**: OidMapService maintains a reverse index (FrozenDictionary name → OID), rebuilt atomically alongside forward map on every UpdateMap call
- [x] **MAP-04**: IOidMapService exposes ResolveToOid(string metricName) returning OID string or null

### Device Config Human Names

- [x] **DEV-01**: PollOptions has a MetricNames property accepting human-readable metric names (full replacement — Oids field removed)
- [x] **DEV-02**: At device config load, each MetricNames[] entry is resolved to its OID via IOidMapService.ResolveToOid; resolved OIDs populate the runtime poll list
- [x] **DEV-03**: Unresolvable metric names log a structured warning (device name, metric name, poll index) and are skipped — device still registered for traps
- [x] **DEV-04**: Full replacement — MetricNames[] is the only field; no coexistence with Oids[] (CONTEXT override: simpler, future-proof)
- [x] **DEV-05**: No raw OID detection — treated as metric name, fails resolution with standard warning (CONTEXT override: keep it simple)
- [x] **DEV-06**: Point-in-time resolution — no cross-watcher triggering; operator triggers device reload after OID map change (CONTEXT override: independent watchers)
- [x] **DEV-07**: Reload diff logging includes per-name resolution detail (resolved count/total, unresolved names listed)

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
| Mandatory migration of devices.json to human names | Full replacement shipped — all configs migrated to MetricNames |
| Separate per-device-type command maps | Single simetra-commandmaps ConfigMap mirrors oidmaps pattern |
| Command map HTTP API or Prometheus export | In-process lookup only; not telemetry |
| Hard startup failure on unresolvable metric names | Soft warning + skip is correct; hard failure too severe |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| MAP-01 | Phase 30 | Complete |
| MAP-02 | Phase 30 | Complete |
| MAP-03 | Phase 30 | Complete |
| MAP-04 | Phase 30 | Complete |
| DEV-01 | Phase 31 | Complete |
| DEV-02 | Phase 31 | Complete |
| DEV-03 | Phase 31 | Complete |
| DEV-04 | Phase 31 | Complete |
| DEV-05 | Phase 31 | Complete |
| DEV-06 | Phase 31 | Complete |
| DEV-07 | Phase 31 | Complete |
| CMD-01 | Phase 32 | Pending |
| CMD-02 | Phase 32 | Pending |
| CMD-03 | Phase 32 | Pending |
| CMD-04 | Phase 32 | Pending |
| CMD-05 | Phase 32 | Pending |
| CMD-06 | Phase 32 | Pending |

**Coverage:**
- v1.6 requirements: 17 total
- Mapped to phases: 17
- Unmapped: 0

---
*Requirements defined: 2026-03-13*
*Last updated: 2026-03-13 after Phase 31 execution — DEV-01 through DEV-07 complete*
