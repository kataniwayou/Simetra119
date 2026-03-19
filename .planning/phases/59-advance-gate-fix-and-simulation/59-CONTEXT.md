# Phase 59: Advance Gate Fix & Priority Starvation Simulation - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix advance gate semantics to use three tenant visit results (Healthy, Resolved, Unresolved) and prove P2 starvation via E2E simulation with P1 in active command cycle.

</domain>

<decisions>
## Implementation Decisions

### Tenant visit results (replaces current TierResult enum)
- **Healthy**: Not stale + any evaluate metric NOT violated (tier=3 stop). Advances gate.
- **Resolved**: All resolved metrics violated (tier=2 stop). Advances gate.
- **Unresolved**: Stale (tier=1 → commands) OR all evaluate violated → tier=4 commands. Blocks gate. Regardless of suppression — command intent = unresolved.

### Advance gate rule
- Gate advances to next priority group ONLY when ALL tenants in the group are Healthy or Resolved
- ANY Unresolved tenant blocks the gate — lower priority groups are not evaluated
- SnapshotJob is stateless — every cycle reads current holder values, no memory of previous result

### TierResult rename
- `Healthy` stays `Healthy`
- `Violated` becomes `Resolved` (all resolved metrics violated = device state confirmed)
- `Commanded` becomes `Unresolved` (command intent = device state not yet confirmed)
- Tier=4 with all commands suppressed returns `Unresolved` (was: `Violated` which incorrectly advanced gate)
- Tier=1 stale → commands returns `Unresolved` (unchanged behavior, renamed)

### Simulation fixture
- Reuse existing `tenant-cfg03-two-diff-prio.yaml` (P1 priority=1, P2 priority=2, SuppressionWindowSeconds=10)
- Use `command_trigger` simulator scenario (evaluate violated, resolved not violated)
- SnapshotJob IntervalSeconds=1 for observable frequency
- Expected: P1 always Unresolved (commands fire or suppressed), P2 never evaluated

### Claude's Discretion
- E2E scenario script structure (dedicated script vs manual observation)
- Whether to add Unresolved to cycle summary log
- Unit test updates for renamed enum values

</decisions>

<specifics>
## Specific Ideas

- The fix is in `EvaluateTenant` return value: tier=4 should ALWAYS return `Unresolved`, not `Violated` when count=0
- The advance gate check changes from `if (results[i] == TierResult.Commanded)` to `if (results[i] == TierResult.Unresolved)`
- Live simulation proved P2 was evaluated on 86/90 cycles (wrong) — should be 0/90

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 59-advance-gate-fix-and-simulation*
*Context gathered: 2026-03-19*
