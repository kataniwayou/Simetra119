# Requirements: SNMP Monitoring System — v3.0 Preferred Leader Election

**Defined:** 2026-03-25
**Core Value:** Pod co-located with SNMP devices gets leadership priority for lowest-latency monitoring, with full HA preserved when preferred pod is absent.

## v3.0 Requirements

### Configuration

- [ ] **CFG-01**: PreferredNode string field in config (feature disabled when absent/empty — backward compatible with existing deployments)
- [ ] **CFG-02**: Pod reads NODE_NAME env var from Downward API to determine if it is the preferred node
- [ ] **CFG-03**: Lease namespace resolved from pod's own namespace at runtime (not hardcoded "default")
- [ ] **CFG-04**: Startup validation: heartbeat lease name must differ from leadership lease name

### Preferred Heartbeat

- [ ] **HB-01**: PreferredHeartbeatService stamps a second K8s Lease resource (`snmp-collector-preferred`) with pod identity and renewTime
- [ ] **HB-02**: Stability gate: preferred pod begins stamping only after ReadinessHealthCheck passes
- [ ] **HB-03**: Heartbeat lease released on graceful shutdown (GracefulShutdownService extended — heartbeat released before/concurrent with leadership lease)
- [ ] **HB-04**: Heartbeat lease expires via TTL on preferred pod crash (non-preferred pods detect stale stamp)

### Election Behavior

- [ ] **ELEC-01**: Non-preferred pods back off (extended retry delay) when preferred heartbeat stamp is fresh
- [ ] **ELEC-02**: Non-preferred leader voluntarily yields (deletes leadership lease) when preferred pod's stamp becomes fresh
- [ ] **ELEC-03**: Fair fallback: when preferred stamp is stale or PreferredNode is absent, standard election with no backoff (identical to current behavior)
- [ ] **ELEC-04**: Preferred pod acquires leadership through normal LeaderElector flow (no force-acquire)

### Observability

- [ ] **OBS-01**: Structured INFO log on each preferred-election decision (backing off, yielding, competing normally, stamping started)

### Deployment

- [ ] **DEP-01**: Pod anti-affinity rule for one-pod-per-node scheduling across sites
- [ ] **DEP-02**: Downward API env vars in pod spec (NODE_NAME from spec.nodeName, POD_NAMESPACE from metadata.namespace)

## Future Requirements

### Observability (v3.x)

- **OBS-F01**: `is_preferred_node` metric label on leadership metrics
- **OBS-F02**: Configurable `PreferredStaleThresholdSeconds` for tuning freshness detection
- **OBS-F03**: Health check `preferredNodeIsLeading` field in readiness endpoint
- **OBS-F04**: K8s Event emitted on voluntary yield for cluster-level audit trail

### Advanced Election (v4+)

- **ELEC-F01**: Multi-level priority chain (site-1 > site-2 > site-3) for complex topologies
- **ELEC-F02**: Dynamic preferred-node based on traffic topology

## Out of Scope

| Feature | Reason |
|---------|--------|
| Hard preemption (preferred pod evicts leader) | Creates same leadership gap as voluntary yield but requires broader RBAC; preferred pod may not be ready |
| Weighted/multi-level priority | Binary preferred/non-preferred covers stated topology; adds coordination complexity |
| Dynamic preferred rotation | Static topology; operators update config on topology change |
| Non-preferred complete passivity during backoff | Would extend leadership gap if preferred fails during backoff window |
| Stamp on SNMP poll cadence | Poll intervals vary (15s-5min); unpredictable stamp timing |
| Remove fair election entirely | Breaks HA guarantee when preferred pod is absent |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CFG-01 | TBD | Pending |
| CFG-02 | TBD | Pending |
| CFG-03 | TBD | Pending |
| CFG-04 | TBD | Pending |
| HB-01 | TBD | Pending |
| HB-02 | TBD | Pending |
| HB-03 | TBD | Pending |
| HB-04 | TBD | Pending |
| ELEC-01 | TBD | Pending |
| ELEC-02 | TBD | Pending |
| ELEC-03 | TBD | Pending |
| ELEC-04 | TBD | Pending |
| OBS-01 | TBD | Pending |
| DEP-01 | TBD | Pending |
| DEP-02 | TBD | Pending |

**Coverage:**
- v3.0 requirements: 15 total
- Mapped to phases: 0
- Unmapped: 15 ⚠️

---
*Requirements defined: 2026-03-25*
*Last updated: 2026-03-25 after initial definition*
