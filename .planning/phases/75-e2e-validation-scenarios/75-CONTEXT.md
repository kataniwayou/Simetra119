# Phase 75: E2E Validation Scenarios - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

E2E scenario scripts that verify the full tenant metrics path — from SnapshotJob evaluation through OTel export to Prometheus. Proves all 8 instruments exist, export on all instances (not just leader), and carry correct label values. Uses the existing simulator HTTP control endpoint to set up evaluation fixtures.

</domain>

<decisions>
## Implementation Decisions

### Scenario coverage strategy
- One scenario per evaluation path (NotReady, Resolved, Healthy, Unresolved) — 4 separate scenarios
- A dedicated smoke test scenario runs first to verify all 8 metric instruments are present in Prometheus with correct `tenant_id` and `priority` labels
- Use the existing simulator `/api/control` endpoint to set up OID values that trigger specific evaluation paths

### Follower vs leader export verification
- Compare all pods: query tenant metrics on all 3 replicas, verify non-zero values on each
- Verify `snmp_gauge`/`snmp_info` remain absent on follower pods (negative proof)
- Query by `k8s_pod_name` label to distinguish leader from followers

### Counter increment verification
- Claude's discretion on approach — verify counters increment by holder count, not by 1
- Known fixture with exact holder counts or delta comparison — Claude picks most reliable method

### Scenario naming & numbering
- Continue from 107 (existing suite ends at 106)
- Same `tests/e2e/` directory alongside existing scenarios — run-all.sh picks them up automatically via `sort -V`
- Follow existing scenario script patterns and naming conventions

### P99 histogram verification
- Claude's discretion — verify exists and is reasonable (> 0)

### Command counter coverage
- Claude's discretion on whether to test all 3 command counters (dispatched, suppressed, failed) in one scenario or separate

</decisions>

<specifics>
## Specific Ideas

- Smoke test first (all 8 instruments present) before path-specific scenarios
- Existing E2E infrastructure: simulator HTTP control, Prometheus queries, run-all.sh with sort -V
- 106 existing scenarios — new ones continue the numbering seamlessly

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 75-e2e-validation-scenarios*
*Context gathered: 2026-03-23*
