# Phase 77: Gather-Then-Decide Evaluation Flow - Context

**Gathered:** 2026-03-23
**Status:** Complete (updated with final behavior after quick-088)

<domain>
## Phase Boundary

Refactor EvaluateTenant to gather all tier results before deciding state, compute percentages, and record metrics at exit. Dispatch only when state = Unresolved.

</domain>

<decisions>
## Implementation Decisions

### State determination rules
- Same tier priority order: stale → resolved → evaluate → healthy
- Gathered before deciding — no early returns except NotReady

### NotReady special handling
- Early return, state + duration only. No percentage gauges recorded.

### Stale path behavior
- Compute stale%, skip resolved/evaluate gathering (stale data unreliable)
- Dispatch commands (stale → Unresolved → dispatch loop runs)
- Record: stale% + dispatched% + suppressed% + failed% (command outcomes are real)
- Do NOT record: resolved%, evaluate% (stale data unreliable)

### Dispatch timing
- Dispatch AFTER state decision, only when state == Unresolved
- Prevents commands firing on Resolved/Healthy paths

### Final behavior table

| State | Stale(%) | Resolved(%) | Evaluate(%) | Dispatched(%) | Suppressed(%) | Failed(%) |
|-------|----------|-------------|-------------|---------------|---------------|-----------|
| NotReady | — | — | — | — | — | — |
| Healthy | computed | computed | computed | computed | computed | computed |
| Resolved | computed | computed | computed | computed | computed | computed |
| Unresolved (stale) | computed | — | — | computed | computed | computed |
| Unresolved (evaluate) | computed | computed | computed | computed | computed | computed |

</decisions>

<specifics>
## Specific Ideas

- "computed" means the percentage is calculated from actual gathered data and recorded as a gauge
- "—" means the gauge is NOT recorded that cycle (Prometheus keeps last known value)
- Healthy/Resolved command percentages will always be 0 (dispatch loop doesn't run), but they ARE recorded

</specifics>

<deferred>
## Deferred Ideas

None.

</deferred>

---

*Phase: 77-gather-then-decide-evaluation-flow*
*Context updated: 2026-03-23 (post quick-088 dispatch fix)*
