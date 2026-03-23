---
phase: 75-e2e-validation-scenarios
plan: "01"
subsystem: testing
tags: [bash, e2e, prometheus, tenant-metrics, tvm, snapshotjob]

# Dependency graph
requires:
  - phase: 73-tenant-vector-metrics
    provides: TenantMetricService with 8 OTel instruments (tenant_id + priority labels)
  - phase: 74-grafana-dashboard-panel
    provides: Grafana dashboard confirming metric name conventions
provides:
  - Scenario 107 (TVM-01): smoke test asserting all 8 tenant metric instruments present with tenant_id/priority labels + tier1_stale_total delta assertion (TE2E-02 stale path)
  - Scenario 108 (TVM-02): NotReady path verification (tenant_state=0, duration>0, zero tier counter deltas)
  - report.sh Tenant Metric Validation category (indices 106-111, scenarios 107-112)
affects:
  - 75-02: Resolved/Healthy path scenarios use same fixture and assertion patterns established here
  - 75-03: Unresolved/all-instances scenarios depend on stale path pattern from TVM-01J

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "_tvm01_assert_metric helper function pattern: query_prometheus with label filter + jq result length check"
    - "Snapshot tier counters before fixture reload to capture NotReady delta window"
    - "poll_until_exists + sleep 5 guard pattern for NotReady timing"

key-files:
  created:
    - tests/e2e/scenarios/107-tvm01-smoke.sh
    - tests/e2e/scenarios/108-tvm02-notready.sh
  modified:
    - tests/e2e/lib/report.sh

key-decisions:
  - "Used _tvm01_assert_metric helper instead of bare assert_exists: assert_exists wraps metric in {__name__='...'} which breaks label filter PromQL; direct query_prometheus with label selector is correct"
  - "Stale path counter verification (TVM-01J) folded into smoke test: covers TE2E-02 without a dedicated scenario, keeping smoke test comprehensive"
  - "NotReady scenario (108) snapshots tier counter baselines immediately after reload, before any evaluation cycle, to get the cleanest delta window"

patterns-established:
  - "Label-filtered metric presence check: query_prometheus 'metric{tenant_id=...}' | jq '.data.result | length' > 0"
  - "Tier counter negative proof: snapshot before fixture apply, sleep 10s, assert delta==0"

# Metrics
duration: 3min
completed: 2026-03-23
---

# Phase 75 Plan 01: E2E Validation Scenarios (Smoke + NotReady) Summary

**Smoke test verifying all 8 TenantMetricService instruments in Prometheus with label filters plus tier1_stale_total delta proof, and NotReady path scenario asserting state=0 + zero tier counter deltas during grace window**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-23T15:14:25Z
- **Completed:** 2026-03-23T15:18:10Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Created `107-tvm01-smoke.sh` with 10 sub-assertions: TVM-01A through TVM-01H (8 instruments with tenant_id label), TVM-01I (priority label = "1"), TVM-01J (tier1_stale_total delta > 0 after stale OID trigger, covering TE2E-02)
- Created `108-tvm02-notready.sh` with 5 sub-assertions: TVM-02A (tenant_state=0), TVM-02B (duration_count>0), TVM-02C/D/E (tier counter deltas all == 0 during grace window)
- Updated `report.sh` with "Tenant Metric Validation|106|111" category and adjusted "Negative Proofs" end index from 108 to 105

## Task Commits

Each task was committed atomically:

1. **Task 1: Create scenario 107-tvm01-smoke.sh and update report.sh** - `03df2f8` (feat)
2. **Task 2: Create scenario 108-tvm02-notready.sh** - `473c209` (previously committed by Phase 75-02 execution; content verified identical)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/e2e/scenarios/107-tvm01-smoke.sh` - Smoke test: 8 instrument label checks + priority label + stale counter delta (TVM-01A through TVM-01J)
- `tests/e2e/scenarios/108-tvm02-notready.sh` - NotReady path: state=0, duration>0, three tier counter delta==0 assertions (TVM-02A through TVM-02E)
- `tests/e2e/lib/report.sh` - Added "Tenant Metric Validation|106|111" category; adjusted "Negative Proofs" end index 108→105

## Decisions Made

- **assert_exists cannot filter by labels:** `assert_exists` in common.sh wraps the metric name in `{__name__="..."}` PromQL, making it impossible to add additional label filters. Used `query_prometheus` directly with a local helper `_tvm01_assert_metric` that checks result count > 0. This is functionally equivalent and more accurate for label-specific existence checks.
- **Stale path (TE2E-02) folded into smoke test:** Rather than a dedicated scenario, TVM-01J appends the stale counter delta check at the end of the smoke test while the tenant is already in Healthy state — clean and efficient.
- **NotReady timing approach:** `poll_until_exists` (timeout=30, interval=3) waits for first `tenant_state` series, then `sleep 5` ensures one scrape cycle before querying. This is robust without relying on exact grace window timing.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] assert_exists incompatible with label filters**
- **Found during:** Task 1 (scenario 107 creation)
- **Issue:** Plan specified `assert_exists "<metric>{tenant_id=\"e2e-pss-tenant\"}"` but `assert_exists` uses `{__name__="<arg>"}` PromQL which would produce invalid syntax with embedded braces
- **Fix:** Created local `_tvm01_assert_metric` helper that calls `query_prometheus` directly with `metric{tenant_id="e2e-pss-tenant"}` and checks result length > 0
- **Files modified:** tests/e2e/scenarios/107-tvm01-smoke.sh
- **Verification:** bash -n passes; 8 helper calls confirmed; equivalent pass/fail recording
- **Committed in:** 03df2f8

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug)
**Impact on plan:** Essential fix — the plan's proposed assert_exists usage would produce invalid PromQL. The helper pattern is more precise and directly asserting label presence.

## Issues Encountered

- Scenario 108 was already committed in a prior session under commit 473c209 (Phase 75-02 execution). The Write tool produced identical content, confirmed by `git diff HEAD` showing no diff. No action needed — the file is correct.

## Next Phase Readiness

- 107 and 108 are complete and committed
- report.sh category map updated for all 6 new scenarios (107-112)
- Scenarios 109-112 were already committed in Phase 75-02 and 75-03 prior sessions
- Phase 75 plan 02 and 03 execution artifacts already exist in the repository

---
*Phase: 75-e2e-validation-scenarios*
*Completed: 2026-03-23*
