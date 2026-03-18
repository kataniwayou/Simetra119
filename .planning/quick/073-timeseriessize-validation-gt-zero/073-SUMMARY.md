# Quick Task 073 Summary: TimeSeriesSize validation >= 1

## What was done

Added validation in `ValidateAndBuildTenants` that `MetricSlotOptions.TimeSeriesSize` must be >= 1. Metrics with TimeSeriesSize <= 0 are now skipped with a structured error log, following the same per-entry skip pattern as other validation checks.

## Files changed

| File | Change |
|------|--------|
| `src/SnmpCollector/Services/TenantVectorWatcherService.cs` | Added TimeSeriesSize < 1 check between Role and TEN-05 validation |
| `tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs` | Added 2 tests: TimeSeriesSizeZero_MetricSkipped, TimeSeriesSizeNegative_MetricSkipped |

## Test results

- 439/439 unit tests pass (437 existing + 2 new)
- Commit: 385daf2
