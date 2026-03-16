# Phase 47: CommandWorkerService - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Channel-backed background worker that executes SNMP SET commands and dispatches responses through the full MediatR pipeline. Includes CommandRequest record, ICommandChannel, and CommandWorkerService. Does NOT include SnapshotJob (Phase 48) or dashboard panels (Phase 49).

</domain>

<decisions>
## Implementation Decisions

### Command channel design
- **CommandRequest record**: minimal — carries `CommandSlotOptions` fields (Ip, Port, CommandName, Value, ValueType) + `DeviceName` for counter tags. Worker resolves everything else at execution time. Consistent with how MetricPollJob works.
- **ICommandChannel**: same pattern as `ITrapChannel` — Writer/Reader properties, DI singleton
- **Channel capacity**: small bounded (16-32), `BoundedChannelFullMode.Wait` not used
- **Backpressure**: `TryWrite` returns false → increment `snmp.command.failed`, log Warning. Non-blocking. Command is lost. No DropOldest — explicit failure path in SnapshotJob.

### SET response dispatch
- **Community string**: resolved from `IDeviceRegistry` by Ip+Port at execution time. Tenant config loading validates at load time that CommandSlotOptions.Ip+Port matches a registered device — so "device not found" should never happen at execution time.
- **DeviceName**: set on SnmpOidReceived from CommandRequest.DeviceName. Consistent with how MetricPollJob sets DeviceName.
- **MetricName resolution**: CommandWorker tries `ICommandMapService.ResolveCommandName(responseOid)` first. If found, pre-sets MetricName (OidResolutionBehavior guard fires, skips metric map). If NOT found, leaves MetricName null — OidResolutionBehavior falls through to metric map, gets "Unknown". Same philosophy as traps: try to resolve, otherwise Unknown.
- **Full pipeline**: SET response varbinds dispatched via `ISender.Send` with `Source=SnmpSource.Command`. All 6 behaviors execute. Fan-out to tenant slots works normally.

### Worker lifecycle
- **Shutdown**: stop immediately on cancellation — do NOT drain remaining commands. Queued commands are lost (SnapshotJob re-queues next cycle).
- **Error isolation**: SetAsync failure → log Warning, increment `snmp.command.failed`, continue to next command. One bad device doesn't block others.
- **Startup log**: "Command channel worker started" at Information level on ExecuteAsync entry. Same as ChannelConsumerService.
- **Correlation ID**: set `OperationCorrelationId = CurrentCorrelationId` before each command execution. Same as ChannelConsumerService.
- **DI registration**: Singleton-then-HostedService pattern (same as K8sLeaseElection). Single instance injected and running.

### Claude's Discretion
- Exact CommandRequest record field naming
- Whether to use `ReadAllAsync` or manual `WaitToReadAsync`/`TryRead` loop
- Error logging format (structured fields)
- Test fixture organization

</decisions>

<specifics>
## Specific Ideas

- CommandWorkerService should mirror ChannelConsumerService as closely as possible — same `await foreach` drain loop, same try/catch per-item, same correlation ID wiring
- SET timeout: `SnapshotJobOptions.IntervalSeconds × SnapshotJobOptions.TimeoutMultiplier` (12s default) — use CancellationTokenSource.CancelAfter around SetAsync, same pattern as MetricPollJob line 93

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 47-commandworkerservice*
*Context gathered: 2026-03-16*
