---
phase: quick-072
plan: 01
subsystem: e2e-tests
tags: [e2e, counter-timing, poll_until, race-condition]
dependency-graph:
  requires: []
  provides: [reliable-adv-counter-assertions]
  affects: []
tech-stack:
  added: []
  patterns: [poll_until counter assertion pattern]
key-files:
  created: []
  modified:
    - tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh
    - tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh
decisions: []
metrics:
  duration: ~2 min
  completed: 2026-03-18
---

# Quick Task 072: Fix ADV Test Script Counter Timing

**One-liner:** Replace immediate counter snapshots with poll_until 45s/5s polling in ADV-01 and ADV-02 E2E scripts to eliminate async SNMP SET + Prometheus scrape race condition.

## What Changed

Both ADV-01 (scenario 36b) and ADV-02 (scenario 37b) used an immediate `snapshot_counter` call right after detecting the tier=4 log. Because the SNMP SET command is dispatched asynchronously via `CommandWorkerService` and Prometheus scrapes on a 15s interval, the counter often had not incremented yet -- causing false delta=0 failures.

The fix replaces the immediate snapshot with the `poll_until 45 5` pattern already established in STS-02 (scenario 30) and MTS-02 (scenario 35). This polls every 5 seconds for up to 45 seconds, waiting for the counter to exceed the baseline before asserting.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Replace immediate snapshot_counter with poll_until in both ADV scripts | ff5a88a | 36-adv-01-aggregate-evaluate.sh, 37-adv-02-depth3-allsamples.sh |

## Verification

- Both scripts pass `bash -n` syntax check
- Both scripts contain exactly 1 `poll_until 45 5` call
- Pattern matches STS-02 and MTS-02 structurally (if poll_until then record_pass else record_fail fi)
- No other sections modified (baseline capture, tier=4 log polling, source=synthetic check, recovery phase, cleanup)

## Deviations from Plan

None -- plan executed exactly as written.
