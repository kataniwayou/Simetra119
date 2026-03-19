# Phase 60: Readiness Window for Holders - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the sentinel sample in MetricSlotHolder with a readiness grace window. Tenants are not evaluated until all holders have passed their grace window. After grace, empty holders are treated as stale.

</domain>

<decisions>
## Implementation Decisions

### Sentinel removal
- MetricSlotHolder constructor no longer creates a sentinel sample (Value=0, Timestamp=now)
- Series starts empty (ImmutableArray.Empty)
- Add `ConstructedAt` (DateTimeOffset.UtcNow) property set at construction

### Readiness grace window
- Per-holder: `ReadinessGrace = TimeSeriesSize × IntervalSeconds × GraceMultiplier`
- Holder is "ready" when `now - ConstructedAt > ReadinessGrace`
- Tenant is "ready" when ALL its holders are ready
- Not-ready tenant blocks the advance gate (same effect as Unresolved)

### Staleness check (3 states after readiness)
- **In grace window** → skip (not ready, don't judge)
- **Grace ended + has data** → check `age > IntervalSeconds × GraceMultiplier` from newest sample (unchanged)
- **Grace ended + no data (ReadSlot is null)** → stale (device never responded)

### Threshold evaluation
- Empty holders (no real samples) are skipped — do not participate in threshold checks
- Only real samples participate in all-samples / newest-only evaluation
- No sentinel Value=0 corrupting threshold results

### Advance gate behavior
- Not-ready tenant → blocks gate (same as Unresolved)
- Ready tenant with no data → stale → Unresolved (commands) → blocks gate
- Ready tenant with data → normal 4-tier evaluation

### SnapshotJob remains stateless
- Readiness is time-based (ConstructedAt + grace), checked every cycle
- No state tracking between cycles

### Claude's Discretion
- Whether readiness check is in SnapshotJob (pre-tier) or in HasStaleness
- Unit test restructuring approach
- Whether to add a "not ready" log message vs silent skip

</decisions>

<specifics>
## Specific Ideas

- The MTS-03 startup race is eliminated by this change — P1 is "not ready" during fill window, not falsely Healthy from sentinel
- The MTS-03 priming fix todo becomes unnecessary after this phase
- Staleness detection for "device never responded" is a new capability enabled by sentinel removal

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 60-readiness-window-for-holders*
*Context gathered: 2026-03-19*
