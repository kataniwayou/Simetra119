# Quick Task 061: Remove DeviceName from CommandRequest Summary

**One-liner:** Remove DeviceName from CommandRequest record; resolve device identity from DeviceRegistry in CommandWorkerService, consistent with MetricPollJob pattern.

**Duration:** ~3 min
**Completed:** 2026-03-16

## Changes Made

### 1. CommandRequest.cs
- Removed `DeviceName` (6th field) from the sealed record
- Record now has 5 fields: `Ip`, `Port`, `CommandName`, `Value`, `ValueType`

### 2. SnapshotJob.cs
- Removed `tenant.Id` argument from `new CommandRequest(...)` call in Tier 4 dispatch

### 3. CommandWorkerService.cs
- After `TryGetByIpPort` succeeds, all references use `device.Name`:
  - `SnmpOidReceived.DeviceName = device.Name`
  - `IncrementCommandSent(device.Name)`
  - Success/timeout log messages use `device.Name`
  - Leader-skip log uses `device.Name`
- Pre-lookup failure paths (OID not found, outer catch) use `$"{req.Ip}:{req.Port}"`
- Device-not-found failure path uses `$"{req.Ip}:{req.Port}"`

### 4. CommandWorkerServiceTests.cs
- Removed `deviceName` parameter from `MakeRequest()` helper
- Renamed test `SetsDeviceNameFromCommandRequest` to `SetsDeviceNameFromDeviceRegistry`
- Updated to verify `device.Name` from registry ("registry-name") instead of request
- Updated log assertion tests: success log checks `RegistryDeviceName`, failure log checks `$"{TestIp}:{TestPort}"`

### 5. SnapshotJobTests.cs
- Removed `DeviceName` assertion from `Execute_CommandNotSuppressed_TryWriteWithCorrectFields`
- Updated channel-full test to use 5-arg CommandRequest constructor
- Updated suppression key test to assert on `CommandName` instead of removed `DeviceName`

## Commits

| Hash | Description |
|------|-------------|
| 88e2f8c | refactor(061): remove DeviceName from CommandRequest, resolve from DeviceRegistry |

## Verification

- `dotnet build` -- 0 errors, 0 warnings
- `dotnet test` -- 415 passed, 0 failed

## Deviations from Plan

None -- plan executed exactly as written.

## Key Files

- `src/SnmpCollector/Pipeline/CommandRequest.cs`
- `src/SnmpCollector/Services/CommandWorkerService.cs`
- `src/SnmpCollector/Jobs/SnapshotJob.cs`
- `tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs`
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs`
