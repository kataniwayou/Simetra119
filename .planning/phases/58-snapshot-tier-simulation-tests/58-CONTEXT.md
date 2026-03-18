# Phase 58: SnapshotJob Tier Simulation Tests - Context

**Gathered:** 2026-03-18
**Status:** Ready for planning

<domain>
## Phase Boundary

E2E scenario scripts validating every SnapshotJob tier path by source type. Proves staleness-to-commands, resolved gate, evaluate gate, suppression, and source-aware threshold checks are observable in pod logs and Prometheus counters.

</domain>

<decisions>
## Implementation Decisions

### Approach
- Reuse existing E2E infrastructure: simulator HTTP scenarios, sim.sh helpers, poll_until_log, snapshot_counter, tenant fixture YAMLs
- Same patterns as phases 53-55 (STS/MTS/ADV scripts)
- Overlap with existing scenarios is acceptable — new scripts target tier paths not yet covered (staleness→commands, source-aware threshold checks)

### Claude's Discretion
- Script structure and naming
- Which existing scenarios can be extended vs which need new scripts
- Simulator scenario additions if needed for new tier paths
- Fixture design for source-aware testing

</decisions>

<specifics>
## Specific Ideas

No specific requirements — follow established E2E patterns from phases 53-55.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 58-snapshot-tier-simulation-tests*
*Context gathered: 2026-03-18*
