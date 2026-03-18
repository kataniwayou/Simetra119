---
status: complete
phase: 56-tenant-validation-hardening
source: 56-01-SUMMARY.md, 56-02-SUMMARY.md
started: 2026-03-18T16:30:00Z
updated: 2026-03-18T16:35:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Threshold Min>Max skips metric
expected: A tenant metric where Threshold.Min > Threshold.Max logs an Error and is skipped entirely (not kept with cleared threshold).
result: pass

### 2. IntervalSeconds=0 skips metric
expected: A metric that resolves to IntervalSeconds=0 (OID not found in any device poll group) logs an Error and is skipped.
result: pass

### 3. IP resolution failure skips metric
expected: A metric with a hostname that doesn't match any device in AllDevices and isn't a valid IP address logs an Error and is skipped.
result: pass

### 4. SuppressionWindowSeconds clamping
expected: SuppressionWindowSeconds=0 is clamped to the snapshot interval (15s). SuppressionWindowSeconds=-1 is accepted as-is (disables suppression). Values < -1 are clamped. Values between 1 and interval-1 are clamped up to interval.
result: pass

### 5. Duplicate tenant Name detection
expected: Two tenants with the same Name in config produce a Warning log. The second tenant is skipped entirely, the first is kept with its metrics intact.
result: pass

### 6. Duplicate metric detection (post-IP-resolution)
expected: Two metrics within one tenant having the same resolved Ip+Port+MetricName produce a Warning. The duplicate is skipped (first kept). The dedup key uses resolved IP.
result: pass

### 7. Duplicate command detection (post-IP-resolution)
expected: Two commands within one tenant having the same resolved Ip+Port+CommandName produce a Warning. The duplicate is skipped.
result: pass

### 8. CommandName not in command map skips command
expected: A command with a CommandName that returns null from ResolveCommandOid logs an Error and is skipped (not passed through as-is).
result: pass

### 9. Command IP resolution mirrors metric pattern
expected: Command entries resolve their Ip field through the AllDevices loop (same as metrics). An unresolved hostname logs Error and skips the command.
result: pass

### 10. All 453 unit tests pass
expected: Running `dotnet test` produces 453 passed, 0 failed, 0 skipped. All existing behavior preserved alongside new validation checks.
result: pass

## Summary

total: 10
passed: 10
issues: 0
pending: 0
skipped: 0

## Gaps

[none]
