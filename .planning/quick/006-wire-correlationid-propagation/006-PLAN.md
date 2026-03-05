---
quick_task: 006
description: Wire CorrelationId propagation into job and trap paths
---

# Quick Task 006: Wire CorrelationId Propagation

## Task

Wire `OperationCorrelationId` propagation into all job and trap processing paths so that every log emitted during an operation carries a consistent correlationId even if the global one rotates mid-execution.

## Changes

1. **MetricPollJob.cs** — Inject `ICorrelationService`; set `OperationCorrelationId = CurrentCorrelationId` at Execute start; clear to null in finally
2. **ChannelConsumerService.cs** — Inject `ICorrelationService`; set `OperationCorrelationId = CurrentCorrelationId` before each envelope dispatch
3. **CorrelationJob.cs** — Capture old correlationId at start, update to new after rotation, clear in finally
4. **MetricPollJobTests.cs** — Add `new RotatingCorrelationService()` to CreateJob
5. **ChannelConsumerServiceTests.cs** — Add `new RotatingCorrelationService()` to CreateService
