# Phase 76: Percentage Gauge Instruments - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace 6 counter instruments with 6 percentage gauge instruments in TenantMetricService. Fix resolved direction to measure violated holders. Rename tenant.state to tenant.evaluation.state. Update ITenantMetricService interface and unit tests.

</domain>

<decisions>
## Implementation Decisions

### Gauge naming convention
- Metric category: `tenant.metric.stale.percent`, `tenant.metric.resolved.percent`, `tenant.metric.evaluate.percent`
- Command category: `tenant.command.dispatched.percent`, `tenant.command.failed.percent`, `tenant.command.suppressed.percent`
- State renamed: `tenant.state` → `tenant.evaluation.state` (consistent namespace for future additions)
- Duration unchanged: `tenant.evaluation.duration.milliseconds` (already has evaluation prefix)

### Edge case: zero denominator
- Record 0.0 — tenant validation prevents this case in practice (rejects tenants with missing role metrics)

### Resolved metric direction
- Numerator = violated resolved holders (not non-violated) — higher % = worse, consistent with evaluate

### Method signatures
- Claude's discretion — pick whatever is cleanest

</decisions>

<specifics>
## Specific Ideas

- Gauge values are doubles in range 0.0 to 100.0
- Percentage calculation happens in caller (SnapshotJob), not in TenantMetricService
- tenant.evaluation.state rename means dashboard and E2E scenarios must update (Prometheus: tenant_evaluation_state instead of tenant_state)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 76-percentage-gauge-instruments*
*Context gathered: 2026-03-23*
