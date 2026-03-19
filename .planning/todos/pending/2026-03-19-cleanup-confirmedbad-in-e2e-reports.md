---
created: 2026-03-19T18:20
title: Clean up ConfirmedBad references in old E2E reports
area: e2e-tests
files:
  - tests/e2e/reports/*.md
---

## Problem

Old E2E report files (generated before quick-076 and phase 59) still reference the obsolete `ConfirmedBad` terminology. Source code was renamed to `Violated` (quick-076) then to `Resolved` (phase 59), but the generated report archives retain the old name.

26 occurrences across ~10 report files in `tests/e2e/reports/`.

## Solution

Either delete old report files (they're generated output, not source of truth) or leave as historical artifacts. No functional impact — the reports are snapshots in time.
