# Quick Task 075: Add error sentinel filter to CommandWorkerService

## Objective

Add NoSuchObject/NoSuchInstance/EndOfMibView error sentinel filtering to CommandWorkerService SET response dispatch, matching the existing pattern in MetricPollJob.

## Tasks

### Task 1: Add error sentinel filter

**File:** `src/SnmpCollector/Services/CommandWorkerService.cs`

- Add TypeCode check before dispatching each response varbind
- Skip with debug log (same format as MetricPollJob)
- Pattern: `if (varbind.Data.TypeCode is SnmpType.NoSuchObject or ...)`

## Verification

- All 453 unit tests pass
- No behavioral change for normal SET responses
