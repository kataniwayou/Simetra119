# Phase 61: New E2E Suite Snapshot - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

Comprehensive E2E test suite covering all tenant evaluation state combinations and snapshot job advance gate logic. 4-tenant setup (2 groups × 2 tenants), per-tenant OIDs for independent control, per-OID HTTP endpoint on simulator for arbitrary value setting.

</domain>

<decisions>
## Implementation Decisions

### Test setup
- 4 tenants: G1-T1, G1-T2 (priority 1), G2-T3, G2-T4 (priority 2)
- SnapshotJob interval: 1 second
- Suppression window: 10 seconds
- Each tenant gets its own set of OIDs (resolved1, resolved2, evaluate) — no shared OIDs between tenants
- Same command OID for all tenants (e2e_command_response .999.4.4.0) is fine since command dispatch is per-tenant

### Per-tenant OIDs (new simulator OIDs)
- T1: .999.4.1 (eval), .999.4.2 (res1), .999.4.3 (res2) — existing OIDs
- T2: .999.5.1 (eval), .999.5.2 (res1), .999.5.3 (res2) — new
- T3: .999.6.1 (eval), .999.6.2 (res1), .999.6.3 (res2) — new
- T4: .999.7.1 (eval), .999.7.2 (res1), .999.7.3 (res2) — new
- All Gauge32, all with threshold Min:1 for resolved, Min:10 for evaluate
- OID map entries needed for all 12 OIDs (4 tenants × 3 metrics)

### Per-OID HTTP endpoint on simulator
- `POST /oid/{oid}/{value}` — set individual OID value directly
- Tests use this instead of predefined scenarios for full control
- Each test sets exactly the values it needs per tenant
- Existing scenario endpoint (`POST /scenario/{name}`) remains for backward compatibility

### Tenant thresholds (same for all tenants)
- Resolved holders: Min:1 (value 0 = violated, value >= 1 = not violated)
- Evaluate holder: Min:10 (value < 10 = violated, value >= 10 = not violated)

### Part 1 — Tenant evaluation states (5 results)

All possible single-tenant results from the 4-tier tree:

| # | Result | TierResult | Readiness | Staleness | Resolved Gate | Evaluate Gate | How to Produce |
|---|--------|-----------|-----------|-----------|--------------|--------------|----------------|
| 1 | Not Ready | Unresolved | In grace window | — | — | — | Apply tenant config, assert before grace ends |
| 2 | Stale → Commands | Unresolved | Past grace | Stale (NoSuchInstance) | — | — | Prime with data, switch to stale, wait for age-out |
| 3 | Resolved | Resolved | Past grace | Fresh | All violated (both=0) | — | Both resolved < Min:1 |
| 4 | Unresolved (commands) | Unresolved | Past grace | Fresh | Not all violated | All violated | Resolved in range, evaluate < Min:10 |
| 5 | Healthy | Healthy | Past grace | Fresh | Not all violated | Not all violated | Resolved in range, evaluate in range |

Threshold sub-cases for resolved gate:
- 3a: All resolved violated (both=0 < Min:1) → Resolved
- 3b: One resolved violated, one not (mixed) → continues to tier 3
- 3c: No resolved violated (both >= 1) → continues to tier 3
- 3b and 3c have same outcome (not ALL violated → proceed)

Staleness + source immunity:
- Poll source: affected by stale
- Synthetic source: affected (depends on poll sources)
- Trap source: NOT affected (HasStaleness skips Trap, Quick 070)
- Command source: NOT affected (HasStaleness skips Command, Quick 070)

### Part 2 — Advance gate logic (group interactions)

Gate rule: blocks if ANY group 1 tenant is Unresolved. Passes if ALL are Resolved or Healthy.

| # | Test | G1-T1 | G1-T2 | Gate | G2 | How (per-OID values) |
|---|------|-------|-------|------|----|---------------------|
| A1 | Both Resolved | Resolved | Resolved | PASS | Evaluated | All T1+T2 resolved=0 |
| A2 | Both Healthy | Healthy | Healthy | PASS | Evaluated | All T1+T2 resolved=50, eval=50 |
| A3 | Resolved+Healthy | Resolved | Healthy | PASS | Evaluated | T1 resolved=0; T2 resolved=50, eval=50 |
| B1 | Both Unresolved | Unresolved | Unresolved | BLOCK | Not evaluated | T1+T2 resolved=50, eval=5 |
| B2 | Both Not Ready | Not Ready | Not Ready | BLOCK | Not evaluated | Fresh config, assert before grace |
| B3 | Resolved+Unresolved | Resolved | Unresolved | BLOCK | Not evaluated | T1 resolved=0; T2 resolved=50, eval=5 |
| B4 | Healthy+Unresolved | Healthy | Unresolved | BLOCK | Not evaluated | T1 resolved=50, eval=50; T2 resolved=50, eval=5 |

Per-tenant OIDs make every combination trivial — just set each tenant's OID values independently.

### Claude's Discretion
- Script organization (one per combination or grouped)
- Simulator implementation details for per-OID endpoint
- Whether to add a bash helper like `sim_set_tenant_state T1 healthy`
- E2E report category naming and numbering
- Whether to test stale+trap immunity with a trap-sourced holder or document as covered by unit tests

</decisions>

<specifics>
## Specific Ideas

- Per-OID HTTP endpoint (`POST /oid/{oid}/{value}`) gives tests full control without predefined scenarios
- Different thresholds per tenant was considered but rejected — same thresholds + different OIDs per tenant is clearer
- Tests should verify both pod log evidence (tier= lines) and Prometheus counter deltas where applicable
- 1s snapshot interval means tests cycle fast — most assertions can use short timeouts

</specifics>

<deferred>
## Deferred Ideas

- Fixing the 6 pre-existing infrastructure E2E failures (scenarios 7, 15, 20, 21, 23) — separate effort, different root causes (community string, MetricNames migration, interval propagation)

</deferred>

---

*Phase: 61-new-e2e-suite-snapshot*
*Context gathered: 2026-03-19*
