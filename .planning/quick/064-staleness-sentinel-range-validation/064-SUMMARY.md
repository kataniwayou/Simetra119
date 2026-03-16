# Quick 064: Staleness Sentinel Timestamp + Range Validation + SnapshotJob Config

**One-liner:** Sentinel MetricSlot at construction starts staleness clock immediately; Range attributes on GraceMultiplier/TimeoutMultiplier; SnapshotJob appsettings section.

**Duration:** ~8 min
**Completed:** 2026-03-16

## Changes

### Fix 1: Sentinel Timestamp in MetricSlotHolder Constructor
- `MetricSlotHolder` constructor now initializes the time series with a sentinel `MetricSlot(0, null, DateTimeOffset.UtcNow)`
- ReadSlot() never returns null -- always returns at least the sentinel
- Staleness clock starts from holder creation, not first data arrival
- If no real data arrives within grace window, holder is correctly detected as stale
- Updated 5 tests across MetricSlotHolderTests, SnapshotJobTests, TenantVectorRegistryTests

### Fix 2-3: Range Validation on PollOptions
- `[Range(0.1, 0.9)]` on `TimeoutMultiplier`
- `[Range(2.0, 5.0)]` on `GraceMultiplier`
- Added `using System.ComponentModel.DataAnnotations`

### Fix 4: Tighten LivenessOptions.GraceMultiplier
- Changed from `[Range(1.0, 100.0)]` to `[Range(2.0, 5.0)]` for consistency

### Fix 5: SnapshotJob appsettings Section
- Added `"SnapshotJob": { "IntervalSeconds": 15, "TimeoutMultiplier": 0.8 }` after Liveness section

## Key Files Modified

| File | Change |
|------|--------|
| `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` | Sentinel initialization in constructor |
| `src/SnmpCollector/Configuration/PollOptions.cs` | Range attributes on TimeoutMultiplier and GraceMultiplier |
| `src/SnmpCollector/Configuration/LivenessOptions.cs` | Tightened GraceMultiplier range |
| `src/SnmpCollector/appsettings.json` | SnapshotJob section |
| `tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs` | Updated for sentinel behavior |
| `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` | Updated null-slot tests for sentinel |
| `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` | Updated new-metric test for sentinel |

## Commits

| Hash | Description |
|------|-------------|
| `bf41158` | Sentinel timestamp in MetricSlotHolder constructor |
| `6667898` | Range validation on PollOptions |
| `9e89a0b` | Tighten LivenessOptions.GraceMultiplier range |
| `6738f73` | SnapshotJob section in appsettings.json |

## Verification

- `dotnet build`: 0 errors, 0 warnings
- `dotnet test`: 415 passed, 0 failed

## Deviations from Plan

None -- plan executed exactly as written.
