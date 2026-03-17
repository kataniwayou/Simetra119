# Phase 53: Single-Tenant Scenarios - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

5 bash scenario scripts validating every branch of the 4-tier evaluation tree with a single tenant: healthy baseline (STS-01), evaluate violated (STS-02), resolved gate (STS-03), suppression window (STS-04), and staleness (STS-05). Each script uses the sim.sh library and tenant-cfg01-single.yaml fixture from Phase 52.

</domain>

<decisions>
## Implementation Decisions

### Assertion strategy
- Every test must assert **BOTH** logs AND Prometheus counters. Both must pass for the test to pass.
- STS-01 (healthy): assert BOTH the tier-3 "not all evaluate metrics violated" log line (positive proof) AND counter delta == 0.
- STS-03 (resolved gate): Claude's discretion on whether to also assert absence of tier-3/tier-4 logs alongside tier-2 ConfirmedBad presence.
- STS-04 (suppression): one continuous test script with 3 sequential assertion windows (sent → suppressed → sent again). NOT split into sub-tests.
- Staleness test uses poll_until_log with timeout (not fixed sleep).

### Scenario flow timing
- Claude's discretion: number of SnapshotJob cycles to wait before asserting (research recommends 2-3 cycles + OTel buffer).
- Claude's discretion: ConfigMap reload wait strategy (15s fixed sleep vs poll_until_log for reload).
- Claude's discretion: global timeout per scenario vs individual poll timeouts only.

### Report output format
- Snapshot scenarios get their own report category: **"Snapshot Evaluation"** — separate from existing E2E categories.
- Each test result includes **log excerpt + counter values** as evidence in the report. Full evidence trail, not just pass/fail.

### Claude's Discretion
- Exact wait times between scenario switch and assertion
- ConfigMap reload wait strategy
- Global vs individual timeouts
- Whether STS-03 asserts absence of tier-3 log
- Scenario numbering (29+) and file naming convention

</decisions>

<specifics>
## Specific Ideas

- The "default" scenario has resolved metrics violated (value=0, threshold Min=1.0) → idle state is ConfirmedBad (Tier 2 stops, no commands)
- The "command_trigger" scenario clears resolved (.4.2=2, .4.3=2) AND breaches evaluate (.4.1=90, threshold Max=80) → reaches Tier 4
- STS-01 uses "default" scenario (resolved violated → ConfirmedBad, but that's the baseline — no commands is expected)
- Wait: STS-01 actually needs a scenario where resolved is NOT violated but evaluate is also NOT violated → use "command_trigger" values for resolved (.4.2=2, .4.3=2) but keep evaluate in-range (.4.1 stays default 0 or use "threshold_clear" .4.1=5). The planner needs to determine which simulator scenario produces "healthy" (resolved in-range + evaluate in-range).
- STS-02 uses "command_trigger" (resolved cleared + evaluate breached → Tier 4 commands)
- STS-03 uses "default" (resolved violated → ConfirmedBad at Tier 2)
- STS-05 uses "stale" scenario

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 53-single-tenant-scenarios*
*Context gathered: 2026-03-17*
