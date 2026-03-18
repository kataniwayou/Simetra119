# Quick Task 073: TimeSeriesSize validation must be >= 1

## Objective

Add per-entry skip validation for `MetricSlotOptions.TimeSeriesSize` in `ValidateAndBuildTenants`. A zero or negative value creates an empty time series that silently prevents tier-4 from ever firing.

## Tasks

### Task 1: Add validation + unit tests

**Files:**
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — add TimeSeriesSize < 1 check after Role validation
- `tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs` — add tests for 0 and -1

**Acceptance:**
- Metric with TimeSeriesSize=0 is skipped with error log
- Metric with TimeSeriesSize=-1 is skipped with error log
- Metric with TimeSeriesSize=1 (default) passes
- All existing tests pass
