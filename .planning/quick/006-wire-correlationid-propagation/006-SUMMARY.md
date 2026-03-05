# Quick Task 006: Wire CorrelationId Propagation — SUMMARY

**Status:** Complete
**Commit:** 0b48083

## What Was Done

Wired `OperationCorrelationId` (AsyncLocal-backed) propagation into all three operation entry points so that every log emitted during an operation carries a consistent correlationId, even if the global ID rotates mid-execution.

### Changes Made

| File | Change |
|------|--------|
| `src/SnmpCollector/Jobs/MetricPollJob.cs` | Added `ICorrelationService` injection; set/clear `OperationCorrelationId` in Execute lifecycle |
| `src/SnmpCollector/Services/ChannelConsumerService.cs` | Added `ICorrelationService` injection; set `OperationCorrelationId` before each envelope dispatch |
| `src/SnmpCollector/Jobs/CorrelationJob.cs` | Added `OperationCorrelationId` capture at start, update after rotation, clear in finally |
| `tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs` | Added `new RotatingCorrelationService()` to CreateJob helper |
| `tests/SnmpCollector.Tests/Services/ChannelConsumerServiceTests.cs` | Added `new RotatingCorrelationService()` to CreateService helper |

### Propagation Pattern

```
Operation start → _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId
  ↓ all logs during operation carry this ID (via SnmpLogEnrichmentProcessor)
  ↓ even if CorrelationJob rotates the global ID mid-operation
Operation end (finally) → _correlation.OperationCorrelationId = null
```

### Coverage

All three operation entry points now propagate:
- **MetricPollJob.Execute** — SNMP GET poll path
- **ChannelConsumerService.ConsumeDeviceAsync** — Trap processing path
- **CorrelationJob.Execute** — Correlation rotation path

### Verification

- Build: 0 warnings, 0 errors
- Tests: 137/137 passed
