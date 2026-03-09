# Phase 23 Plan 01: OID Map Mutation Fixtures and Scenarios Summary

OID mutation E2E test infrastructure: 3 ConfigMap fixtures (rename/remove/add) and 3 scenario scripts (18-20) verifying runtime oidmap hot-reload propagates to Prometheus without pod restarts.

## Tasks Completed

| # | Task | Commit | Key Files |
|---|------|--------|-----------|
| 1 | Create OID mutation fixture files | aaf037a | tests/e2e/fixtures/oid-{renamed,removed,added}-configmap.yaml |
| 2 | Create OID mutation scenario scripts 18-20 | 16cd217 | tests/e2e/scenarios/{18-oid-rename,19-oid-remove,20-oid-add}.sh |

## What Was Built

### Fixture ConfigMaps (3 files)

Each fixture is a complete copy of `simetra-oidmaps` ConfigMap (all 99 OBP + NPB + E2E-SIM entries) with one targeted modification:

- **oid-renamed-configmap.yaml**: `.999.1.1.0` mapped to `e2e_renamed_gauge` (was `e2e_gauge_test`)
- **oid-removed-configmap.yaml**: `.999.1.1.0` entry removed entirely (OID will resolve as Unknown)
- **oid-added-configmap.yaml**: `.999.2.1.0` mapped to `e2e_unmapped_gauge` (new entry added)

### Scenario Scripts (3 files)

All follow the sourced-script pattern (no shebang, no `set -euo`, no `source` statements). All use `snapshot_configmaps`/`restore_configmaps` for isolation.

- **18-oid-rename.sh (MUT-01)**: Applies renamed oidmap, polls for `e2e_renamed_gauge` in Prometheus, verifies labels, restores, confirms `e2e_gauge_test` reappears
- **19-oid-remove.sh (MUT-02)**: Applies removed oidmap, polls for `metric_name="Unknown"` with specific OID, verifies labels, restores, confirms original metric reappears
- **20-oid-add.sh (MUT-03)**: Two-step mutation -- first applies unmapped device config to start polling `.999.2.1.0` (appears as Unknown), then applies added oidmap, polls for `e2e_unmapped_gauge`, verifies transition from Unknown to named metric

## Requirements Coverage

| Requirement | Scenario | Status |
|-------------|----------|--------|
| MUT-01: OID rename propagates | 18-oid-rename.sh | Ready for UAT |
| MUT-02: OID removal -> Unknown | 19-oid-remove.sh | Ready for UAT |
| MUT-03: OID addition resolves Unknown | 20-oid-add.sh | Ready for UAT |

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Full ConfigMap copies (not patches) | `kubectl apply` replaces entire ConfigMap; partial would lose entries |
| 60s deadline with 3s poll interval | Matches existing E2E patterns; covers ConfigMap detect ~5s + poll 10s + OTel 15s |
| Two-step mutation for scenario 20 | Must first establish Unknown baseline before proving mapping resolves it |
| `return 0` early exit in scenario 20 | Prevents cascading failures if prerequisite Unknown state not achieved |

## Deviations from Plan

None -- plan executed exactly as written.

## Files Created

- `tests/e2e/fixtures/oid-renamed-configmap.yaml`
- `tests/e2e/fixtures/oid-removed-configmap.yaml`
- `tests/e2e/fixtures/oid-added-configmap.yaml`
- `tests/e2e/scenarios/18-oid-rename.sh`
- `tests/e2e/scenarios/19-oid-remove.sh`
- `tests/e2e/scenarios/20-oid-add.sh`
