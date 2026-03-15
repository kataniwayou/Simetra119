# Requirements: SNMP Monitoring System

**Defined:** 2026-03-15
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.10 Requirements — Heartbeat Refactor & Pipeline Liveness

Requirements for removing hardcoded heartbeat special cases and adding pipeline-arrival-based liveness detection.

### Heartbeat Cleanup

- [ ] **HB-01**: Remove hardcoded heartbeat tenant from `TenantVectorRegistry.Reload` — heartbeat holder, heartbeat tenant, `int.MinValue` priority bucket, heartbeat carry-over logic
- [ ] **HB-02**: Remove heartbeat bypass from `TenantVectorFanOutBehavior` — delete `if (DeviceName == HeartbeatDeviceName)` block and its routing logic; "Simetra" not in DeviceRegistry → `TryGetDeviceByName` returns false → fan-out naturally skipped
- [ ] **HB-03**: `TenantCount` property adjusted — no longer includes hardcoded heartbeat tenant in count; reflects only config-driven tenants

### Pipeline Liveness

- [ ] **HB-04**: New `IHeartbeatLivenessService` interface + implementation — holds a `DateTimeOffset` timestamp of last heartbeat pipeline arrival
- [ ] **HB-05**: Stamp point in pipeline — when terminal handler (`OtelMetricHandler`) processes a message with `DeviceName == HeartbeatJobOptions.HeartbeatDeviceName`, call `IHeartbeatLivenessService.Stamp()`
- [ ] **HB-06**: Liveness health check — reads pipeline arrival timestamp; `now - lastArrival > staleness window` → unhealthy
- [ ] **HB-07**: Staleness window uses `HeartbeatJobOptions.DefaultIntervalSeconds` (15) × default GraceMultiplier (2.0) = 30s

### Preserved Behavior

- [ ] **HB-08**: `ILivenessVectorService.Stamp()` in HeartbeatJob.finally remains unchanged — all scheduled jobs continue stamping on completion
- [ ] **HB-09**: HeartbeatJob unchanged — sends real SNMP trap with OID `1.3.6.1.4.1.9999.1.1.1.0`, Source=Trap, community `"Simetra.Simetra"`
- [ ] **HB-10**: OidMapService heartbeat seed unchanged — `"Heartbeat"` metric name persists through all hot-reloads

## Out of Scope

| Feature | Reason |
|---------|--------|
| Runtime threshold evaluation | Separate milestone — depends on threshold structure (v1.9) |
| Heartbeat as configurable tenant metric | Heartbeat is infrastructure, not a business metric |
| Changing heartbeat OID to "0.0" | Real trap needs valid wire OID |
| Changing heartbeat Source to Synthetic | Heartbeat is a real trap over UDP — Source=Trap is correct |
| Removing ILivenessVectorService entirely | Job completion stamping serves a different purpose (scheduler liveness) |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| HB-01 | Phase 43 | Pending |
| HB-02 | Phase 43 | Pending |
| HB-03 | Phase 43 | Pending |
| HB-04 | Phase 44 | Pending |
| HB-05 | Phase 44 | Pending |
| HB-06 | Phase 44 | Pending |
| HB-07 | Phase 44 | Pending |
| HB-08 | Phase 44 | Pending |
| HB-09 | Phase 44 | Pending |
| HB-10 | Phase 44 | Pending |

**Coverage:**
- v1.10 requirements: 10 total
- Mapped to phases: 10
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-15*
*Last updated: 2026-03-15 after v1.10 roadmap created*
