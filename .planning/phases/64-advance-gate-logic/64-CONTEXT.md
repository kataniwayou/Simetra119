# Phase 64: Advance Gate Logic - Context

**Gathered:** 2026-03-20
**Status:** Ready for planning

<domain>
## Phase Boundary

Verify all 7 advance gate combinations (3 pass, 4 block) with a 4-tenant 2-group fixture. Gate-pass means G2 is evaluated; gate-block means G2 is never evaluated. Covers PSS-14 through PSS-20.

</domain>

<decisions>
## Implementation Decisions

### 4-Tenant Fixture Design
- G1: T1+T2 (priority 1), G2: T3+T4 (priority 2) -- clean split, G1 gates G2
- Reuse existing OID ranges: .999.4.x=T1, .999.5.x=T2, .999.6.x=T3, .999.7.x=T4
- Each tenant gets both resolved-role and evaluate-role metrics -- full evaluation pipeline per tenant
- Follow existing naming convention from Stage 1/2 (not gate-specific names)
- Pre-prime all 4 tenants to healthy before scenarios run -- scenarios only manipulate the state they need

### Scenario Organization
- One script per scenario (7 scripts total, PSS-14 through PSS-20)
- Pass scenarios first (PSS-14/15/16), then block scenarios (PSS-17/18/19/20)
- Gate-pass assertions verify G2 tenant tier values (e.g., Healthy), not just log presence -- stronger proof evaluation ran

### Gate-Block Verification
- 15-second observation window for verifying G2 absence
- Dual proof: both log absence AND metric non-increment for G2 tenants
- Gate-block scenarios also assert G1 tenants WERE evaluated (positive assertion alongside negative G2) -- avoids false passes from idle system
- PSS-20 (Not Ready blocks gate): verify G1 shows "Not Ready" in logs AND G2 is absent -- confirms blocking reason

### Stage 3 Runner & Gating
- run-stage3.sh applies the 4-tenant fixture itself (self-contained)
- Runner primes all 4 tenants to healthy once after fixture apply, before running scenarios
- FAIL_COUNT gating from Stage 2 (same pattern as Stage 2 gates on Stage 1)
- run-all.sh prints cross-stage summary (total pass/fail across Stage 1, 2, and 3) at the end

### Plan Structure
- 3 plans: Plan 1 (fixture + runner), Plan 2 (pass scenarios PSS-14/15/16), Plan 3 (block scenarios PSS-17/18/19/20)

### Claude's Discretion
- Fixture file structure (single vs separate configmap files)
- Threshold values for all 4 tenants
- Script numbering continuation from Stage 2

</decisions>

<specifics>
## Specific Ideas

No specific requirements -- open to standard approaches matching Stage 1/2 patterns.

</specifics>

<deferred>
## Deferred Ideas

None -- discussion stayed within phase scope.

</deferred>

---

*Phase: 64-advance-gate-logic*
*Context gathered: 2026-03-20*
