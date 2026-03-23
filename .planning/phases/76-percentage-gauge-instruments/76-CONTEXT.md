# Phase 76: Percentage Gauge Instruments - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace 6 counter instruments with 6 percentage gauge instruments in TenantMetricService. Fix resolved direction to measure violated holders. Preserve tenant_state gauge and tenant_evaluation_duration_milliseconds histogram unchanged. Update ITenantMetricService interface and unit tests.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion

**Gauge naming convention:**
- Follow existing OTel dot-separated convention: `tenant.stale.percent`, `tenant.resolved.percent`, `tenant.evaluate.percent`, `tenant.dispatched.percent`, `tenant.failed.percent`, `tenant.suppressed.percent`
- Prometheus auto-converts to: `tenant_stale_percent`, `tenant_resolved_percent`, etc.
- No `_total` suffix (gauges, not counters)
- Keep `tenant_` prefix consistent with existing `tenant_state` and `tenant_evaluation_duration_milliseconds`

**Edge case: zero denominator:**
- When denominator is 0 (e.g., tenant has no resolved-role metrics), record `0.0` — not NaN, not skip
- Rationale: 0% violation is the correct interpretation when there are no metrics to violate
- For commands: if total_commands = 0 and tenant reaches tier 4, record 0% for all command percentages (no commands to dispatch)
- This avoids NaN in Prometheus which breaks dashboard rendering and PromQL comparisons

**Method signature design:**
- Individual methods per metric: `RecordStalePercent(tenantId, priority, value)`, `RecordResolvedPercent(...)`, etc.
- Mirrors the existing pattern where each instrument has its own method (IncrementTier1Stale, IncrementTier2Resolved, etc.)
- Percentage calculation happens in SnapshotJob (caller), not in TenantMetricService (recorder) — service just records pre-computed doubles
- This keeps TenantMetricService as a thin recording layer (same role as PipelineMetricService)

</decisions>

<specifics>
## Specific Ideas

- Gauge values are doubles in range 0.0 to 100.0
- TenantMetricService constructor creates `Gauge<double>` instruments (not `Counter<long>`)
- Unit tests verify: gauge creation, method signatures accept double, correct instrument names, labels present
- The counting helper removal (CLN-02) means removing CountStaleHolders/CountResolvedNonViolated/CountEvaluateViolated and replacing with percentage-computing equivalents in Phase 77

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 76-percentage-gauge-instruments*
*Context gathered: 2026-03-23*
