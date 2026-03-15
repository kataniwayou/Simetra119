# Requirements: SNMP Monitoring System

**Defined:** 2026-03-15
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.9 Requirements — Metric Threshold Structure & Validation

Requirements for adding threshold config model to tenant metric entries with load-time validation. Structural only — no runtime evaluation.

### Threshold Model

- [ ] **THR-01**: `MetricSlotOptions` gains optional `Threshold` property — a new `ThresholdOptions` sealed class with `Min` (double?) and `Max` (double?)
- [ ] **THR-02**: `ThresholdOptions` defaults to null when absent from JSON — existing metric entries without `Threshold` are completely unaffected (backward compatible)
- [ ] **THR-03**: Threshold values stored on `MetricSlotHolder` at load time for future runtime evaluation access

### Threshold Validation

- [ ] **THR-04**: `Threshold` object with both `Min` and `Max` null = valid (always-violated semantics — threshold gate always returns true)
- [ ] **THR-05**: `Min` > `Max` when both set = Error log, skip threshold (metric entry still loads, threshold ignored) — structured log with TenantName, MetricIndex, Min, Max
- [ ] **THR-06**: Threshold validation runs in `TenantVectorWatcherService.ValidateAndBuildTenants` — consistent with watcher-validates pattern from v1.7

### Config Files

- [ ] **THR-07**: Update tenant config files (local dev tenants.json, K8s simetra-tenants.yaml, production configmap.yaml) with example thresholds on select metrics
- [ ] **THR-08**: Threshold absent from tenant metric entry = null (no threshold) — backward compatible deserialization

## Out of Scope

| Feature | Reason |
|---------|--------|
| Runtime threshold evaluation | Deferred to future milestone — this is structural only |
| Threshold on command entries | Commands don't have metric values to evaluate |
| Threshold alerting/notifications | Evaluation must exist before alerting |
| Threshold on aggregated metrics | Can be added later when evaluation exists |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| THR-01 | Phase 41 | Pending |
| THR-02 | Phase 41 | Pending |
| THR-03 | Phase 41 | Pending |
| THR-04 | Phase 42 | Pending |
| THR-05 | Phase 42 | Pending |
| THR-06 | Phase 42 | Pending |
| THR-07 | Phase 42 | Pending |
| THR-08 | Phase 41 | Pending |

**Coverage:**
- v1.9 requirements: 8 total
- Mapped to phases: 8
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-15*
*Last updated: 2026-03-15 after phase mapping (Phases 41-42)*
