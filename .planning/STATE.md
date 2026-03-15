# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-15)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.8 Combined Metrics — Phase 37 (Config and Runtime Models)

## Current Position

Phase: 37 of 40 (Config and Runtime Models)
Plan: —
Status: Ready to plan
Last activity: 2026-03-15 — v1.8 roadmap created (Phases 37-40)

Progress: [####################] v1.0-v1.7 complete | [░░░░░░░░░░] 0/4 v1.8 phases

## Performance Metrics

**Velocity:**
- Total plans completed: 88 (v1.0 through v1.7)
- Average duration: ~25 min
- Total execution time: ~36.7 hours

**Recent Trend:**
- Last milestone (v1.7): 8 plans, 4 phases
- Trend: Stable

*Updated after each plan completion*

## Accumulated Context

### Key Architectural Facts (v1.8 relevant)

- All pods maintain tenant vector state (no leader gating) — synthetic metrics route identically to poll metrics
- OidResolutionBehavior: unconditional `msg.MetricName = _oidMapService.Resolve(msg.Oid)` — NO bypass guard exists yet (critical: Phase 39 must add it before any synthetic dispatch)
- DeviceWatcherService.ValidateAndBuildDevicesAsync: internal static async — all per-entry validation happens here (mirrors v1.7 watcher pattern)
- MetricPollInfo is a positional record — CombinedMetrics optional parameter with default `[]` is source-compatible with all existing construction sites
- `SnmpSource` enum currently has only `Poll` and `Trap` — `Synthetic` must be added in Phase 39
- Aggregator terms: `"sum"`, `"subtract"`, `"absDiff"`, `"mean"` (lowercase; case-insensitive matching at load time)
- TypeCode selection: Subtract/AbsDiff → Integer32 (signed, result can be negative); Sum/Mean → Gauge32 (unsigned)
- Sentinel OID: `"0.0"` passes existing ValidationBehavior OID regex without guard; Prometheus label will show `oid="0.0"`
- Bypass guard decision: Option B — guard on `Source == SnmpSource.Synthetic` (consistent, unambiguous, future-safe)
- Ratio aggregation: excluded from v1.8 — `AggregationKind.Ratio` may exist in enum but BuildPollGroups treats it as invalid Action

### v1.8 Pre-Phase Decisions (record in plans)

- Phase 39: Name the bypass guard option (Option B: `Source == SnmpSource.Synthetic`) as a named decision
- Phase 39: Name the sentinel OID value (`"0.0"`) as a named decision
- Phase 40: Ratio is an invalid Action value in v1.8 — BuildPollGroups skips with Error log (same as unknown string)

### Known Tech Debt

None.

### Blockers/Concerns

None. All four phases have HIGH confidence per research summary. Phase 39 must complete before Phase 40.

## Session Continuity

Last session: 2026-03-15
Stopped at: v1.8 roadmap written — ROADMAP.md, STATE.md, REQUIREMENTS.md traceability updated
Resume file: None
