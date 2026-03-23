# Phase 74: Grafana Dashboard Panel - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Add a per-tenant per-pod status table to the existing operations dashboard (`simetra-operations.json`), displaying evaluation state, tier counter rates, duration P99, and trend arrows. The table follows the exact same patterns as the business dashboard gauge/info tables.

</domain>

<decisions>
## Implementation Decisions

### Table density & readability
- Match business dashboard table style exactly: `cellHeight: "sm"`, full-width `w: 24`, `showHeader: true`
- Use `merge` + `organize` transformations (same pattern as gauge metrics table)
- Column ordering via `indexByName` in organize transform
- State column uses color-background thresholds with value mappings (same technique as trend arrows on business dashboard)
- 13 columns total: Host, Pod, Tenant, Priority, State, Dispatched, Failed, Suppressed, Stale, Resolved, Evaluate, P99 (ms), Trend

### Rate window & aggregation
- Claude's discretion — pick appropriate rate window based on expected counter frequency
- P99 uses `histogram_quantile(0.99, sum by (...) (rate(..._bucket{...}[$__rate_interval])))` — same pattern as business dashboard
- Counter columns use `rate()` not raw values
- All queries use `instant: true` with `format: "table"`

### Trend arrow logic
- Match business dashboard trend pattern exactly:
  - `delta(...)[30s]` query as separate refId
  - Value mappings: `▼` (dark-red) for negative, `—` (text) for near-zero, `▲` (dark-green) for positive
  - Threshold ranges: `[-1e9, -0.0001]` = down, `[-0.0001, 0.0001]` = flat, `[0.0001, 1e9]` = up
  - `color-background` display mode, column width ~80px
  - Null mapped to `"-"` with text color
- Delta applied to `tenant_command_dispatched` (most operationally relevant activity indicator)

### PromQL column
- **Removed** — user decision. No copyable PromQL column in this table.

### Filter integration
- Existing `$host` and `$pod` template variables must cascade to tenant table queries (same `service_instance_id=~"$host", k8s_pod_name=~"$pod"` pattern)

### State column color mapping
- Value mappings for TenantState enum:
  - 0 = NotReady → grey
  - 1 = Healthy → green
  - 2 = Resolved → yellow
  - 3 = Unresolved → red
- Display as text labels (not raw numbers) via value mappings

</decisions>

<specifics>
## Specific Ideas

- "Same as tables on business dashboard" — the gauge metrics table in `simetra-business.json` is the reference implementation for all table patterns (PromQL generation, trend arrows, P99, merge+organize transforms, column overrides)
- The existing `label_join`/`label_replace` pattern from business dashboard is NOT needed here since PromQL column was removed

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 74-grafana-dashboard-panel*
*Context gathered: 2026-03-23*
