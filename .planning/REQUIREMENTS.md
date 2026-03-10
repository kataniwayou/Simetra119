# Requirements: SNMP Monitoring System

**Defined:** 2026-03-10
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.5 Requirements — Priority Vector Data Layer

Requirements for the stateful data layer that organizes SNMP metrics into prioritized tenants with independent value cells and fan-out routing from the existing pipeline.

### Configuration

- [ ] **CFG-01**: tenantvector.json config model with tenants (id, priority, metrics[]) where each metric has ip, port, metric_name, source, intervalSeconds
- [ ] **CFG-02**: IValidateOptions validator — unique tenant IDs, valid IP/port ranges, metric_name exists in OID map, no duplicate (ip, port, metric_name) within a tenant
- [ ] **CFG-03**: simetra-tenantvector ConfigMap with TenantVectorWatcherService (K8s API watch, hot-reload, local dev file fallback)

### Data Layer

- [ ] **DAT-01**: TenantVectorRegistry singleton — ordered priority groups, each containing tenants with metric slots
- [ ] **DAT-02**: MetricSlot value cell — immutable record (value + updated_at), atomic swap via Volatile.Write
- [ ] **DAT-03**: Routing index — FrozenDictionary keyed by (ip, port, metric_name) → list of (tenant_id, slot reference)
- [ ] **DAT-04**: Atomic rebuild on config change — full registry + routing index rebuilt, volatile swap

### Pipeline Integration

- [ ] **PIP-01**: TenantVectorFanOutBehavior in MediatR chain after OidResolution — looks up routing index, writes to matching slots
- [ ] **PIP-02**: Port resolved via DeviceRegistry.TryGetDeviceByName(DeviceName) — zero changes to SnmpOidReceived
- [ ] **PIP-03**: Skip heartbeat (IsHeartbeat) and Unknown (MetricName) samples — never routed to tenant slots
- [ ] **PIP-04**: Fan-out behavior catches own exceptions and always calls next() — never kills OTel export

### Observability

- [ ] **OBS-01**: Structured diff logging on reload — tenants added/removed/changed
- [ ] **OBS-02**: Pipeline counter snmp_tenantvector_routed_total — increments on successful fan-out

### Deployment

- [ ] **DEP-01**: K8s ConfigMap manifest for simetra-tenantvector
- [ ] **DEP-02**: Deployment.yaml updated with ConfigMap volume mount

## Out of Scope

| Feature | Reason |
|---------|--------|
| Decision/evaluation logic | Future milestone — data layer only |
| GraceMultiplier staleness detection | Evaluation engine concern, not data layer |
| External API (REST/gRPC) | Spec says internal only |
| Prometheus export of slot values | Would cause cardinality explosion (tenants x metrics) |
| Tenant-specific polling | Consumes existing pipeline data only |
| History/time-series buffer | Spec: "no per-metric history buffer" |
| Cross-tenant slot deduplication | Spec: "each tenant maintains its own independent slot" |
| Durable persistence | Ephemeral in-memory structure, rebuilt from config on startup |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CFG-01 | Phase 25 | Complete |
| CFG-02 | Phase 25 | Complete |
| CFG-03 | Phase 28 | Pending |
| DAT-01 | Phase 26 | Pending |
| DAT-02 | Phase 26 | Pending |
| DAT-03 | Phase 26 | Pending |
| DAT-04 | Phase 26 | Pending |
| PIP-01 | Phase 27 | Pending |
| PIP-02 | Phase 27 | Pending |
| PIP-03 | Phase 27 | Pending |
| PIP-04 | Phase 27 | Pending |
| OBS-01 | Phase 28 | Pending |
| OBS-02 | Phase 27 | Pending |
| DEP-01 | Phase 29 | Pending |
| DEP-02 | Phase 29 | Pending |

**Coverage:**
- v1.5 requirements: 15 total
- Mapped to phases: 15
- Unmapped: 0

---
*Requirements defined: 2026-03-10*
*Last updated: 2026-03-10 after roadmap creation*
