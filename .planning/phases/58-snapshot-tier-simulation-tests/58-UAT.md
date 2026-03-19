---
status: complete
phase: 58-snapshot-tier-simulation-tests
source: 58-01-SUMMARY.md, 58-02-SUMMARY.md, 58-03-SUMMARY.md
started: 2026-03-19T11:07:00Z
updated: 2026-03-19T11:07:32Z
---

## Current Test

[testing complete]

## Tests

### 1. Scenario 31 (STS-03) fixed log pattern
expected: Scenario 31 polls for "tier=2 — all resolved violated, no commands" (not the obsolete "device confirmed bad" text). Passes with zero command counters.
result: pass

### 2. Scenario 33 (STS-05) staleness dispatches commands
expected: Scenario 33 detects tier=1 stale log and asserts snmp_command_sent_total DOES increment (delta > 0). Reversed from old behavior (was: delta == 0).
result: pass

### 3. Scenario 38 (STS-06) Poll staleness to commands
expected: New scenario validates full tier=1 stale → tier=4 command dispatch path. Three sub-assertions: stale+skip log, commands enqueued log, sent counter increment.
result: pass

### 4. Scenario 39 (STS-07) Synthetic staleness to commands
expected: New scenario validates synthetic-sourced metric staleness triggers commands. Uses aggregate tenant fixture (e2e_total_util). Three sub-assertions: stale log, commands enqueued log, sent counter increment.
result: pass

### 5. report.sh range covers new scenarios
expected: Snapshot Evaluation category range updated to include scenarios 38-39. New scenarios appear in the generated E2E report.
result: pass

### 6. All existing scenarios (1-37) unchanged
expected: No regressions in existing scenarios from phase 58 changes. Same 6 pre-existing legacy timing flakes as before (07, 15a/b, 20, 21, 23).
result: pass

## Summary

total: 6
passed: 6
issues: 0
pending: 0
skipped: 0

## Gaps

[none]
