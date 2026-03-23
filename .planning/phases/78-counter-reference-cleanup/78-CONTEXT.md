# Phase 78: Counter Reference Cleanup - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Remove all residual dead code from the counter-to-gauge migration. Only `CountResolvedNonViolated` remains as orphaned dead code — it's defined but never called, superseded by `CountResolvedViolated` in Phase 77.

Note: `_pipelineMetrics.Increment*` calls in SnapshotJob and CommandWorkerService are pipeline-level metrics (IPipelineMetricService) — NOT tenant metrics. These stay.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
- Remove `CountResolvedNonViolated` method (dead code)
- Remove any orphaned comments referencing old counter behavior
- Verify no other dead code from the migration exists
- Build must compile cleanly

</decisions>

<specifics>
## Specific Ideas

No specific requirements — straightforward dead code removal.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 78-counter-reference-cleanup*
*Context gathered: 2026-03-23*
