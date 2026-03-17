# Phase 54: Multi-Tenant Scenarios - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Two scenario scripts: MTS-01 (same-priority parallel evaluation) and MTS-02 (different-priority advance gate). Uses existing tenant fixtures from Phase 52 (tenant-cfg02-two-same-prio.yaml, tenant-cfg03-two-diff-prio.yaml) and the sim.sh + prometheus.sh libraries from Phase 52-53.

</domain>

<decisions>
## Implementation Decisions

### Advance gate assertion (MTS-02)
- MTS-02A (gate blocked): assert **BOTH** log absence for group-2 tenant AND counter delta == 0 for group-2
- MTS-02B (gate passed): both groups in command_trigger scenario; assert sent counters increment for **both** groups (not just group-2 tier logs)
- Follow the "both logs AND counters" assertion strategy from Phase 53

### Same-priority independence (MTS-01)
- Claude's discretion: both tenants see the same OIDs and same scenario values, so they'll have the same tier outcome. Proof of independence is showing per-tenant log lines for both tenant IDs.

### Claude's Discretion
- How to prove same-priority independence given identical OID values
- Exact scenario numbering (34+) and file naming
- Wait times and poll timeouts (follow Phase 53 patterns)
- Whether MTS-02A and MTS-02B are in one script or separate

</decisions>

<specifics>
## Specific Ideas

- MTS-02 advance gate test: priority-1 tenant blocks group advance when it's Commanded/Stale. Priority-2 tenant should NOT be evaluated. This requires checking that the priority-2 tenant's ID does NOT appear in tier log lines during the observation window.
- MTS-02B (gate passed): all priority-1 tenants must be in a state that allows advance (Healthy or ConfirmedBad — NOT Commanded or Stale). Then priority-2 tenants should be evaluated.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 54-multi-tenant-scenarios*
*Context gathered: 2026-03-17*
