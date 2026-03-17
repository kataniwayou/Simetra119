# Phase 55: Advanced Scenarios - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Two scenario scripts: ADV-01 (aggregate metric as evaluate — synthetic pipeline feeds threshold check) and ADV-02 (time series depth-3 all-samples check with recovery assertion). Uses existing tenant-cfg04-aggregate.yaml fixture. Last phase of v2.1.

</domain>

<decisions>
## Implementation Decisions

### Depth-3 recovery assertion (ADV-02)
- Recovery proof requires **BOTH** tier-3 healthy log AND counter delta == 0 over observation window
- After 3 violated samples fill the series and tier-4 fires, switch one sample back in-range, then assert recovery with both log and counter

### Aggregate evaluate proof (ADV-01)
- Same as STS-02 (tier-4 log + sent counter) **PLUS** verify the metric has `source=synthetic` in Prometheus
- This proves the synthetic pipeline path is working, not just the threshold check

### Claude's Discretion
- Exact timing for depth-3 fill (minimum 3 poll cycles × 10s = 30s, but with SnapshotJob timing need ~75s+)
- How to query Prometheus for source=synthetic label verification
- Scenario numbering (36+) and file naming
- Whether report.sh range needs another extension (currently |28|34|, indices 35-36 need |28|36|)
- Wait times and poll timeouts

</decisions>

<specifics>
## Specific Ideas

- ADV-02 has three phases: (1) fill series with 3 violated samples → tier-4 fires, (2) switch one sample to in-range → tier-3 healthy (recovery), (3) assert recovery with both log and counter
- The depth-3 fill takes ~75s minimum (3 polls × 10s interval + SnapshotJob 15s cycle + OTel export delay)
- ADV-01 uses tenant-cfg04-aggregate.yaml (e2e-tenant-agg) with e2e_total_util as Evaluate metric

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 55-advanced-scenarios*
*Context gathered: 2026-03-17*
