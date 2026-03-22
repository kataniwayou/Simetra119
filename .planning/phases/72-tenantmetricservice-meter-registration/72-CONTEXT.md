# Phase 72: TenantMetricService & Meter Registration - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Create a new TenantMetricService singleton with 8 OTel instruments (6 counters, 1 gauge, 1 histogram) on a separate "SnmpCollector.Tenant" meter that exports on all instances — unblocking all downstream instrumentation work in Phase 73.

</domain>

<decisions>
## Implementation Decisions

### Service design
- New TenantMetricService class (NOT extending PipelineMetricService) — separate singleton mirroring PipelineMetricService pattern
- ITenantMetricService interface for testability — consistent with existing IPipelineMetricService pattern
- Direct injection into SnapshotJob constructor alongside existing IPipelineMetricService

### Instrument naming
- Dot-separated convention matching pipeline metrics (snmp.event.published pattern)
- `tenant.` prefix for namespace separation from snmp.* metrics
- Full instrument names:
  - tenant.tier1.stale (counter)
  - tenant.tier2.resolved (counter)
  - tenant.tier3.evaluate (counter)
  - tenant.command.dispatched (counter)
  - tenant.command.failed (counter)
  - tenant.command.suppressed (counter)
  - tenant.state (gauge)
  - tenant.evaluation.duration.milliseconds (histogram) — renamed from tenant.gauge.duration.milliseconds for clarity
- Labels: tenant_id, priority (snake_case, consistent with existing label convention)

### State enum design
- Extend existing TierResult enum (SnapshotJob.cs:33) by adding NotReady
- Rename from TierResult to TenantState — matches the metric name
- Move to shared Models/Enums location — both TenantMetricService and SnapshotJob reference it
- Values: TenantState { NotReady = 0, Healthy = 1, Resolved = 2, Unresolved = 3 }

### Histogram bucket boundaries
- Default OTel buckets — same pattern as existing snmp.snapshot.cycle_duration_ms and snmp_gauge_duration histograms
- No AddView or ExplicitBucketHistogramConfiguration — consistent with codebase, no custom bucket pattern exists

### Claude's Discretion
- Method signatures for counter increments (one method per counter vs grouped per tier)
- TelemetryConstants placement for TenantMeterName

</decisions>

<specifics>
## Specific Ideas

- MetricRoleGatedExporter filters by meter name "SnmpCollector.Leader" — the new "SnmpCollector.Tenant" meter passes ungated with zero exporter changes
- Existing TierResult enum at SnapshotJob.cs:33 is `internal enum TierResult { Resolved, Healthy, Unresolved }` — needs NotReady added and values reordered to match gauge encoding

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 72-tenantmetricservice-meter-registration*
*Context gathered: 2026-03-23*
