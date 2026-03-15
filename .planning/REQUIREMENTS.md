# Requirements: SNMP Monitoring System

**Defined:** 2026-03-15
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.8 Requirements — Combined Metrics

Requirements for computing aggregate metrics from SNMP poll group responses and dispatching them as synthetic gauge metrics through the existing pipeline.

### Config Model

- [ ] **CM-01**: `PollOptions` gains optional `AggregatedMetricName` (string) and `Aggregator` (string) properties — both null = disabled, current behavior unchanged
- [ ] **CM-02**: `Aggregator` validated at load time against allowed set `{ "sum", "subtract", "absDiff", "mean" }` — invalid value = skip poll group with Error log
- [ ] **CM-03**: Cross-field co-presence validation: `AggregatedMetricName` and `Aggregator` must both be present or both absent — partial = Error log, skip poll group

### Pipeline Integration

- [ ] **CM-04**: `SnmpSource` enum gains `Synthetic` member — synthetic dispatches carry `Source = SnmpSource.Synthetic`
- [ ] **CM-05**: `OidResolutionBehavior` bypasses OID→MetricName resolution when source is Synthetic (MetricName already pre-set)
- [ ] **CM-06**: `ValidationBehavior` handles synthetic messages — sentinel OID or source-based guard to pass regex validation
- [ ] **CM-07**: Synthetic metric dispatched as `snmp_gauge` with labels: `metric_name=AggregatedMetricName`, `oid=""` or sentinel, `source="synthetic"`, `snmp_type` based on computed result type

### Computation

- [ ] **CM-08**: MetricPollJob computes aggregate after all individual varbind dispatches — `sum()` adds all values, `subtract()` subtracts sequentially (m1-m2-m3), `absDiff()` takes absolute value of sequential subtraction (|m1-m2-m3|), `mean()` divides sum by count (double, not integer truncation)
- [ ] **CM-09**: All inputs must respond successfully AND be numeric (snmp_gauge type) — any missing response or snmp_info input = skip aggregated metric for this cycle with Warning log
- [ ] **CM-10**: Combined metric dispatched through full MediatR pipeline (Logging → Exception → Validation → OidResolution [bypassed] → ValueExtraction → TenantVectorFanOut → OtelMetricHandler)

### Validation at Load Time

- [ ] **CM-11**: `AggregatedMetricName` collision with existing OID map entry produces structured Error log — aggregated metric skipped, real metric takes priority
- [ ] **CM-12**: Combined metric config validated in `DeviceWatcherService.ValidateAndBuildDevicesAsync` (BuildPollGroups) — invalid = skip aggregated metric definition, poll group still loads for individual metrics

### Observability

- [ ] **CM-13**: New `snmp.aggregated.computed` pipeline counter increments each time a aggregated metric is successfully computed and dispatched
- [ ] **CM-14**: Warning log when aggregated metric computation is skipped (partial response, non-numeric input) with device name, poll group index, and reason

### Tenant Routing

- [ ] **CM-15**: Synthetic aggregated metrics route to tenant vector slots via (ip, port, metricName) — tenants can register AggregatedMetricName in their Metrics[] array

## Out of Scope

| Feature | Reason |
|---------|--------|
| Ratio aggregation | Scale factor design not settled — defer to v1.9 |
| SNMP SET execution | Command entries loaded/validated only; execution deferred |
| Combined metrics from trap data | Traps are async events, not multi-value responses |
| Cross-poll-group aggregation | Combined metric operates within a single poll group only |
| Custom aggregation functions | Sum/diff/mean covers the use cases; extensibility deferred |
| Combined metric for snmp_info | String aggregation has no meaningful semantics |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CM-01 | Phase 37 | Complete |
| CM-02 | Phase 38 | Complete |
| CM-03 | Phase 38 | Complete |
| CM-04 | Phase 39 | Complete |
| CM-05 | Phase 39 | Complete |
| CM-06 | Phase 39 | Complete |
| CM-07 | Phase 40 | Complete |
| CM-08 | Phase 40 | Complete |
| CM-09 | Phase 40 | Complete |
| CM-10 | Phase 40 | Complete |
| CM-11 | Phase 38 | Complete |
| CM-12 | Phase 38 | Complete |
| CM-13 | Phase 40 | Complete |
| CM-14 | Phase 40 | Complete |
| CM-15 | Phase 40 | Complete |

**Coverage:**
- v1.8 requirements: 15 total
- Mapped to phases: 15
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-15*
*Last updated: 2026-03-15 after v1.8 roadmap created (Phases 37-40)*
