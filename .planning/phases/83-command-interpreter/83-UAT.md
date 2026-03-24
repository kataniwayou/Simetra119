---
status: complete
phase: 83-command-interpreter
source: [83-01-SUMMARY.md]
started: 2026-03-24T15:30:00Z
updated: 2026-03-24T16:15:00Z
---

## Current Test

[testing complete]

## Tests

### 1. V-mode violates correct metrics and resets others
expected: `./sim_command.sh -v T1_P1-V-1E-1R` shows 4 POST calls — 1E violated, 1E healthy, 1R violated, 1R healthy
result: pass

### 2. S-mode stales correct metrics and resets others
expected: `./sim_command.sh -v T2_P1-S-2E-1R` shows 2E stale, 2E healthy, 1R stale, 3R healthy (8 calls total)
result: pass

### 3. Zero counts reset all to healthy
expected: `./sim_command.sh -v T1_P1-V-0E-0R` sets all 4 OIDs to healthy values (eval→10, res→1)
result: pass

### 4. S-mode then V-mode cancels stale
expected: Run `./sim_command.sh T1_P1-S-2E-0R` then `./sim_command.sh -v T1_P1-V-0E-0R` — all OIDs set to healthy values (stale cancelled)
result: pass

### 5. Silent on success
expected: `./sim_command.sh T1_P1-V-0E-0R` produces no stdout (only stderr if -v)
result: pass

### 6. Error: unknown tenant
expected: `./sim_command.sh FAKE-V-1E-0R` produces red error listing T1_P1, T2_P1, T1_P2, T2_P2
result: pass

### 7. Error: count exceeds limit
expected: `./sim_command.sh T1_P1-V-3E-0R` produces red error saying T1_P1 has 2 Evaluate metrics
result: pass

### 8. Error: malformed pattern
expected: `./sim_command.sh GARBAGE` produces red error showing expected format with example
result: pass

## Summary

total: 8
passed: 8
issues: 0
pending: 0
skipped: 0

## Gaps

[none]
