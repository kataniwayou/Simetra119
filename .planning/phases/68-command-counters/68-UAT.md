---
status: complete
phase: 68-command-counters
source: 68-01-SUMMARY.md, 68-02-SUMMARY.md
started: 2026-03-22T19:00:00Z
updated: 2026-03-22T19:30:00Z
---

## Current Test

[testing complete]

## Tests

### 1. CCV-01: command.dispatched increments at tier=4
expected: snmp_command_dispatched_total delta >= 1 after triggering tier=4
result: pass

### 2. CCV-02A: command.dispatched increments on first tier=4 (Window 1)
expected: dispatched delta > 0 in Window 1
result: pass

### 3. CCV-02B: command.suppressed increments within suppression window
expected: suppressed delta > 0 in Window 2
result: pass

### 4. CCV-03: command.dispatched unchanged while suppressed
expected: dispatched delta == 0 in Window 2
result: issue
reported: "dispatched_delta=3 in Window 2 — dispatched fires on EVERY tier=4 enqueue, including when suppression also fires. dispatched and suppressed are not mutually exclusive. The SnapshotJob enqueues commands (dispatched++) and THEN checks suppression (suppressed++). Both counters increment on every suppressed cycle."
severity: major

### 5. CCV-04A: command.dispatched increments for unmapped command
expected: dispatched delta >= 1 for e2e-ccv-failed tenant
result: issue
reported: "Tenant 'e2e-ccv-failed' is ENTIRELY SKIPPED during TenantVectorWatcherService validation because CommandName 'e2e_set_unknown' is not found in the command map. The tenant never enters SnapshotJob evaluation at all. Validation-time rejection happens BEFORE runtime — dispatched never fires."
severity: major

### 6. CCV-04B: command.failed increments for unmapped CommandName
expected: failed delta >= 1
result: issue
reported: "Same root cause as CCV-04A — tenant skipped at validation. command.failed fires only in CommandWorkerService at runtime for OIDs that pass load-time validation. An unmapped CommandName is caught by TenantVectorWatcherService at load time, not by CommandWorkerService at runtime."
severity: major

## Summary

total: 6
passed: 3
issues: 3
pending: 0
skipped: 0

## Gaps

- truth: "command.dispatched does NOT increment when command is suppressed"
  status: failed
  reason: "dispatched fires on every tier=4 enqueue regardless of suppression. Suppression blocks worker execution, not enqueue. dispatched and suppressed both increment on suppressed cycles."
  severity: major
  test: 4
  root_cause: "CCV-03 requirement misunderstands the dispatch/suppression boundary. SnapshotJob calls TryWrite (dispatched++) then TrySuppress (suppressed++). They are independent counters, not mutually exclusive states."
  artifacts:
    - path: "tests/e2e/scenarios/84-ccv02-03-command-suppressed.sh"
      issue: "CCV-03 assertion expects dispatched_delta == 0, but dispatched always fires at tier=4"
  missing:
    - "Redefine CCV-03: test that suppressed cycles do NOT produce command.sent (worker-side execution), or accept that dispatched fires on every tier=4"

- truth: "command.failed increments for unmapped CommandName"
  status: failed
  reason: "TenantVectorWatcherService validates CommandName at load time, rejecting tenant entirely. CommandWorkerService never receives the command. command.failed is a runtime counter, not a validation counter."
  severity: major
  test: 5
  root_cause: "CCV-04 cannot be triggered via unmapped CommandName because validation prevents the tenant from being evaluated. command.failed only fires for runtime failures (timeout, device-not-found) on commands that passed validation."
  artifacts:
    - path: "tests/e2e/fixtures/tenant-cfg09-ccv-failed.yaml"
      issue: "Fixture with unmapped CommandName causes tenant skip, not runtime failure"
  missing:
    - "CCV-04 needs a different trigger: timeout (unreachable command target IP) or device-not-found (IP not in device registry)"
