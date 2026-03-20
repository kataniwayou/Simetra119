---
phase: quick-083
plan: 01
subsystem: e2e-fixtures
tags: [e2e, fixtures, schema-migration, PollOptions]

dependency-graph:
  requires: [quick-082]
  provides: [e2e-fixtures-metrics-schema-aligned]
  affects: []

tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - tests/e2e/fixtures/device-added-configmap.yaml
    - tests/e2e/fixtures/fake-device-configmap.yaml
    - tests/e2e/fixtures/device-modified-interval-configmap.yaml
    - tests/e2e/fixtures/device-removed-configmap.yaml
    - tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml
    - tests/e2e/scenarios/06-poll-unreachable.sh

decisions: []

metrics:
  duration: 4 minutes
  completed: 2026-03-20
---

# Quick Task 083: E2E MetricNames to Metrics Transform Summary

**One-liner:** Migrated all E2E fixture JSON from flat `"MetricNames": [...]` arrays to `"Metrics": [{"MetricName": "..."}]` object-wrapper arrays to match PollOptions C# model after quick-082 refactor.

## What Was Done

Completed the E2E side of the schema migration started in quick-082. All 5 YAML configmap fixture files and 1 shell script that contained the old `MetricNames` flat-string-array schema were transformed to the new `Metrics` object-wrapper format required by the updated `PollOptions.cs` model.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Transform 5 YAML fixture files (34 occurrences) | 2ebe071 | device-added-configmap.yaml, fake-device-configmap.yaml, device-modified-interval-configmap.yaml, device-removed-configmap.yaml, e2e-sim-unmapped-configmap.yaml |
| 2 | Transform inline JSON in shell script (1 occurrence) | d9b34ef | tests/e2e/scenarios/06-poll-unreachable.sh |

## Verification Results

- Zero `MetricNames` references remain in any tracked file under `tests/e2e/`
- 34 `"Metrics"` occurrences across 5 fixture files (8+7+7+6+6)
- 1 `"Metrics"` occurrence in `06-poll-unreachable.sh`
- All fixture files have valid JSON embedded in YAML pipe-literal blocks
- Shell script passes `bash -n` syntax check

## Decisions Made

None — mechanical transform with no ambiguity.

## Deviations from Plan

None — plan executed exactly as written.

The `.original-devices-configmap.yaml` dot-file in `tests/e2e/fixtures/` still contains old `MetricNames` schema, but this file is a runtime-generated, untracked backup created by the E2E runner's `save_configmap` function and is not part of the static fixture set. It is out of scope for this transformation.

## Next Phase Readiness

E2E fixtures now match the `PollOptions.Metrics` schema from quick-082. All E2E scenarios that load device configmaps via `kubectl apply` will deserialize correctly against the updated C# model.
