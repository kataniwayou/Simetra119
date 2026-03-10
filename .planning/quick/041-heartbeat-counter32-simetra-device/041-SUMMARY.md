---
quick: 041
name: heartbeat-counter32-simetra-device
subsystem: pipeline/telemetry
tags: [heartbeat, counter32, otel, snmp-gauge, simetra]
status: complete
completed: 2026-03-10
duration: ~5m

key-files:
  modified:
    - src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
    - src/SnmpCollector/Jobs/HeartbeatJob.cs
    - src/SnmpCollector/Pipeline/OidMapService.cs
    - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
    - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
    - tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs

commits:
  - hash: 1c3ad32
    message: "feat(quick-041): heartbeat as Counter32 with Simetra device name"
  - hash: 41c41b1
    message: "test(quick-041): update heartbeat tests for Counter32 export assertions"
---

# Quick Task 041: Heartbeat Counter32 + Simetra Device Summary

**One-liner:** Heartbeat promoted to first-class snmp_gauge metric with Counter32 incrementing value and device_name=Simetra, removing the OtelMetricHandler suppression gate.

## What Was Done

Heartbeat was previously a suppressed pipeline signal — `Integer32(1)` sent on each cycle with the device name "heartbeat", deliberately dropped by OtelMetricHandler before reaching Prometheus. This made collector liveness invisible in Grafana except through internal pipeline counters.

This task makes heartbeat a fully exported metric, visible on the same dashboards used for real device data.

## Changes by File

### HeartbeatJobOptions.cs
- `HeartbeatDeviceName` constant changed from `"heartbeat"` to `"Simetra"`
- Doc-comment updated to reflect new name

### HeartbeatJob.cs
- Added `private static long _counter` field
- Varbind changed from `new Integer32(1)` to `new Counter32((uint)Interlocked.Increment(ref _counter))`
- Added `using System.Threading` for `Interlocked`
- Community string is now "Simetra.Simetra" (derived via `CommunityStringHelper.DeriveFromDeviceName("Simetra")`)

### OidMapService.cs
- `MergeWithHeartbeatSeed` now seeds the heartbeat OID as `"Heartbeat"` (capital H) instead of `"heartbeat"`

### OtelMetricHandler.cs
- Removed `using SnmpCollector.Configuration` (was only needed for suppression check)
- Removed the suppression block that checked `HeartbeatDeviceName` and returned early
- Heartbeat Counter32 now flows through the existing `case SnmpType.Counter32` path and calls `RecordGauge`

## Test Updates

### OtelMetricHandlerTests.cs
- `Heartbeat_SuppressedFromMetricExport` renamed to `Heartbeat_ExportedAsGauge_WithSimetraDevice`
  - Value changed to `Counter32(1)`, TypeCode to `SnmpType.Counter32`
  - Assertion changed from `Assert.Empty(GaugeRecords)` to `Assert.Single(GaugeRecords)`
  - Added assertions for `MetricName="Heartbeat"` and `DeviceName="Simetra"`
- `HeartbeatDeviceName_SuppressedFromMetricExport` renamed to `HeartbeatDeviceName_ExportedAsGauge`
  - Value changed to `Counter32`, assertion changed to `Assert.Single`

### HeartbeatJobTests.cs
- `Constructor_DerivesCommunityString`: updated to use `HeartbeatJobOptions.HeartbeatDeviceName` and assert `"Simetra.Simetra"`

### OidResolutionBehaviorTests.cs
- `ResolvesHeartbeatOid_ViaOidMapService`: stub metricName changed to `"Heartbeat"`, value changed to `Counter32`, assertion updated to `"Heartbeat"`

## Verification Results

```
dotnet build src/SnmpCollector/SnmpCollector.csproj  -- 0 errors, 0 warnings
dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj  -- 207/207 passed
grep "SuppressedFromMetricExport" tests/  -- no matches
grep '"heartbeat"' src/SnmpCollector/  -- only Quartz job key strings (scheduler, not SNMP device)
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Stale doc-comment in HeartbeatJobOptions.cs**
- Found during: Task 1
- Issue: Doc-comment still referenced `"heartbeat"` string after constant was changed to `"Simetra"`
- Fix: Updated comment to say "Simetra device name"
- Files modified: src/SnmpCollector/Configuration/HeartbeatJobOptions.cs

**2. [Rule 2 - Missing Critical] OidResolutionBehaviorTests used "heartbeat" (lowercase)**
- Found during: Task 2 verification (grep scan)
- Issue: Stub configured with lowercase `"heartbeat"` and assertion expected lowercase; would diverge from real OidMapService behavior after the seed was changed to `"Heartbeat"`
- Fix: Updated stub and assertion to `"Heartbeat"` (capital H)
- Files modified: tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs

## Outcome

Heartbeat is now a first-class metric exported to Prometheus as `snmp_gauge` with:
- `device_name=Simetra`
- `metric_name=Heartbeat`
- `snmp_type=counter32`
- Value increments monotonically on each poll cycle

This enables collector liveness monitoring in Grafana via the same queries used for real device metrics.
