---
phase: quick-084
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - tests/e2e/scenarios/65-pss-17-all-g1-unresolved.sh
  - tests/e2e/scenarios/68-pss-20-all-g1-not-ready.sh
autonomous: true

must_haves:
  truths:
    - "PSS-17c --since window matches sleep 10 observation period (no 2s overlap)"
    - "PSS-20c --since window matches sleep 10 observation period (no 2s overlap)"
    - "Header comments and record_pass messages reflect accurate 10s window"
  artifacts:
    - path: "tests/e2e/scenarios/65-pss-17-all-g1-unresolved.sh"
      provides: "PSS-17c log absence check with aligned --since=10s"
      contains: "--since=10s"
    - path: "tests/e2e/scenarios/68-pss-20-all-g1-not-ready.sh"
      provides: "PSS-20c log absence check with aligned --since=10s"
      contains: "--since=10s"
  key_links: []
---

<objective>
Align PSS-17c and PSS-20c `--since` flags from 12s to 10s to match the 10s observation sleep.

Purpose: Eliminates 2s overlap that could capture logs from before the observation window, causing false failures. This is the same fix already applied to PSS-18c/19c in Phase 65.
Output: Two corrected E2E scenario scripts with consistent sleep/since alignment.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@tests/e2e/scenarios/65-pss-17-all-g1-unresolved.sh
@tests/e2e/scenarios/68-pss-20-all-g1-not-ready.sh
</context>

<tasks>

<task type="auto">
  <name>Task 1: Fix --since=12s to --since=10s in both scenarios and correct comments/messages</name>
  <files>
    tests/e2e/scenarios/65-pss-17-all-g1-unresolved.sh
    tests/e2e/scenarios/68-pss-20-all-g1-not-ready.sh
  </files>
  <action>
Apply identical changes to both scenario files:

**Scenario 65 (65-pss-17-all-g1-unresolved.sh):**
1. Line 18: Change header comment from `--since=15s` to `--since=10s`
2. Line 104: Change `--since=12s` to `--since=10s` in the kubectl logs command
3. Line 114: Change record_pass detail string from `"G2 tier logs absent in 15s window"` to `"G2 tier logs absent in 10s window"`

**Scenario 68 (68-pss-20-all-g1-not-ready.sh):**
1. Line 22: Change header comment from `--since=12s` to `--since=10s`
2. Line 108: Change `--since=12s` to `--since=10s` in the kubectl logs command
3. Line 118: Change record_pass detail string from `"G2 tier logs absent in 12s window"` to `"G2 tier logs absent in 10s window"`

These are exact line-level edits. No other lines should change.
  </action>
  <verify>
    grep -n "since=" tests/e2e/scenarios/65-pss-17-all-g1-unresolved.sh tests/e2e/scenarios/68-pss-20-all-g1-not-ready.sh
    # All --since references should show 10s, none should show 12s or 15s
    grep -c "since=12s\|since=15s" tests/e2e/scenarios/65-pss-17-all-g1-unresolved.sh tests/e2e/scenarios/68-pss-20-all-g1-not-ready.sh
    # Both files should return 0 matches
  </verify>
  <done>
    All --since flags in PSS-17c and PSS-20c are 10s, matching sleep 10 observation windows.
    All header comments and record_pass messages reference correct 10s window size.
    No remaining references to 12s or 15s windows in either file.
  </done>
</task>

</tasks>

<verification>
- `grep -n "since=" tests/e2e/scenarios/65-pss-17-all-g1-unresolved.sh` shows only `--since=10s`
- `grep -n "since=" tests/e2e/scenarios/68-pss-20-all-g1-not-ready.sh` shows only `--since=10s`
- `grep -rn "since=12s\|since=15s" tests/e2e/scenarios/65-pss-17-all-g1-unresolved.sh tests/e2e/scenarios/68-pss-20-all-g1-not-ready.sh` returns no matches
- Header comments on line 18 (scenario 65) and line 22 (scenario 68) reference `--since=10s`
</verification>

<success_criteria>
PSS-17c and PSS-20c --since flags aligned to 10s matching their sleep 10 observation windows, eliminating the 2s overlap that could cause flaky test failures. All comments and messages updated to reflect correct window size.
</success_criteria>

<output>
After completion, create `.planning/quick/084-pss-17c-20c-since-alignment/084-SUMMARY.md`
</output>
