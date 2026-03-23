# Phase 79: Dashboard Percentage Update - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Update the operations dashboard Tenant Status table to display percentage gauge values instead of counter-based increase() values. Update PromQL queries for the 6 renamed gauge instruments and the renamed state gauge. Import updated dashboard via Grafana API.

</domain>

<decisions>
## Implementation Decisions

### Percentage display format
- Raw number, 0 decimal places (e.g., "75" not "75%" or "75.0")
- Column headers include (%): Stale(%), Resolved(%), Evaluate(%), Dispatched(%), Failed(%), Suppressed(%)

### State column metric rename
- PromQL: `tenant_state` → `tenant_evaluation_state` (all queries + zero-fallback)
- Color mappings and value mappings unchanged (0=NotReady/grey, 1=Healthy/green, 2=Resolved/yellow, 3=Unresolved/red)

### Column order
- Keep current: Host, Pod, Tenant, Priority, Stale(%), Resolved(%), Evaluate(%), Dispatched(%), Suppressed(%), Failed(%), State, P99 (ms), Trend

### PromQL simplification
- 6 counter queries (`increase()` with zero-fallback) replaced with direct gauge queries (no rate/increase needed — gauges return current value)
- Zero-fallback pattern may no longer be needed (gauges always have a value after first recording), but keep for safety during first cycles
- Trend column delta: `delta(tenant_command_dispatched_total...)` → need to update to reference the new gauge name `tenant_command_dispatched_percent` (delta on a gauge that changes each cycle)

</decisions>

<specifics>
## Specific Ideas

- Gauge queries are simpler: just `tenant_metric_stale_percent{...}` — no sum by() or increase() wrapper
- The `max by()` wrapper from v2.4 is still needed to strip extra labels for merge compatibility
- Import via Grafana API (manual management since quick-087)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 79-dashboard-percentage-update*
*Context gathered: 2026-03-23*
