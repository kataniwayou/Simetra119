---
status: complete
phase: 65-e2e-runner-fixes
source: 65-01-SUMMARY.md
started: 2026-03-22T10:25:00Z
updated: 2026-03-22T10:26:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Stage 1 filenames corrected in run-stage2.sh
expected: run-stage2.sh Stage 1 scenario loop lists: 53-pss-01-not-ready.sh, 54-pss-02-stale-to-commands.sh, 55-pss-03-resolved.sh. No references to old names.
result: pass

### 2. Cleanup trap resets OID overrides
expected: run-stage2.sh cleanup() calls reset_oid_overrides || true before stop_port_forwards.
result: pass

### 3. PSS-18c log-absence window aligned
expected: Scenario 66 uses --since=10s matching the 10s observation sleep.
result: pass

### 4. PSS-19c log-absence window aligned
expected: Scenario 67 uses --since=10s matching the 10s observation sleep.
result: pass

### 5. Standalone run-stage2.sh report categories
expected: _REPORT_CATEGORIES overridden at 2 generate_report call sites with 0-based indices.
result: pass

### 6. Standalone run-stage3.sh report categories
expected: _REPORT_CATEGORIES overridden at 3 generate_report call sites with 0-based indices.
result: pass

## Summary

total: 6
passed: 6
issues: 0
pending: 0
skipped: 0

## Gaps

[none]
