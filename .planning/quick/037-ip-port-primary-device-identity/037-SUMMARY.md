---
phase: quick-037
plan: 01
subsystem: pipeline
tags: [device-registry, quartz, identity, snmp]
dependency-graph:
  requires: []
  provides: [ip-port-primary-device-identity, duplicate-ip-port-validation]
  affects: [device-watcher, trap-listener]
tech-stack:
  added: []
  patterns: [composite-key-identity, dual-dictionary-lookup]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/IDeviceRegistry.cs
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - src/SnmpCollector/Pipeline/MetricPollInfo.cs
    - src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Services/DynamicPollScheduler.cs
    - src/SnmpCollector/Jobs/MetricPollJob.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
    - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
    - tests/SnmpCollector.Tests/Services/DynamicPollSchedulerTests.cs
decisions:
  - id: Q037-D1
    description: "IP+Port as primary device identity, Name as secondary for trap listener"
    rationale: "IP+Port is the true SNMP endpoint identity; Name is a human label"
  - id: Q037-D2
    description: "Underscore separator in Quartz job keys (ip_port) instead of colon"
    rationale: "Colons are problematic in Quartz job key names on some systems"
  - id: Q037-D3
    description: "Duplicate Name no longer rejected -- only IP+Port must be unique"
    rationale: "Same name with different IP+Port is valid (e.g., after device rename)"
metrics:
  duration: ~8 minutes
  completed: 2026-03-10
---

# Quick Task 037: IP+Port as Primary Device Identity

DeviceRegistry primary key changed from Name to (IP, Port) composite key with dual FrozenDictionary lookup and duplicate IP+Port rejection at startup and reload.

## Tasks Completed

| Task | Name | Commit | Key Changes |
|------|------|--------|-------------|
| 1 | DeviceRegistry keyed by IP+Port with duplicate validation | de2027b | IDeviceRegistry.TryGetByIpPort, dual dictionaries, validator, 14 tests |
| 2 | Job keys, MetricPollJob, DynamicPollScheduler use IP+Port | 674e054 | JobDataMap ipAddress+port, TryGetByIpPort lookup, IP-based job keys |

## What Changed

### DeviceRegistry (Primary Key: IP+Port)
- `_byIpPort` FrozenDictionary as primary lookup (keyed by `"{ip}:{port}"`)
- `_byName` FrozenDictionary preserved as secondary lookup (trap listener compatibility)
- Constructor and `ReloadAsync` both validate no duplicate IP+Port exists
- `ReloadAsync` returns added/removed sets using IP:Port keys instead of device names

### MetricPollInfo.JobKey
- Signature changed from `JobKey(string deviceName)` to `JobKey(string ipAddress, int port)`
- Format: `metric-poll-{ip}_{port}-{pollIndex}` (underscore between IP and port)

### MetricPollJob
- Reads `ipAddress` + `port` from JobDataMap instead of `deviceName`
- Resolves device via `TryGetByIpPort` instead of `TryGetDeviceByName`
- `device.Name` still used for logging, metrics, and unreachability tracking

### ServiceCollectionExtensions + DynamicPollScheduler
- Job keys and trigger identities use `{ip}_{port}` format
- JobDataMap passes `ipAddress` and `port` instead of `deviceName`

### DevicesOptionsValidator
- `ValidateNoDuplicates` checks `"{ip}:{port}"` composite key
- Duplicate Name check removed entirely

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added TryGetByIpPort to StubDeviceRegistry in MetricPollJobTests early**
- **Found during:** Task 1
- **Issue:** Adding TryGetByIpPort to IDeviceRegistry interface caused compile error in test stub
- **Fix:** Added minimal TryGetByIpPort implementation to StubDeviceRegistry during Task 1 (plan scheduled it for Task 2)
- **Files modified:** tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs

**2. [Rule 1 - Bug] Fixed stale `deviceName` variable references in MetricPollJob**
- **Found during:** Task 2
- **Issue:** After renaming the variable from `deviceName` to `ipAddress`/`port`, five remaining references to `deviceName` in the try/catch/finally block caused compile errors
- **Fix:** Changed to `device.Name` since these are used for unreachability tracking and metrics (which key by human-readable device name)
- **Files modified:** src/SnmpCollector/Jobs/MetricPollJob.cs

## Verification

- `dotnet build src/SnmpCollector/` -- 0 errors, 0 warnings
- `dotnet test` -- 27/27 affected tests pass (2 pre-existing failures in OidMapAutoScanTests unrelated)
- No `UsingJobData("deviceName"` in src/ -- confirmed removed
- No `TryGetDeviceByName` in src/SnmpCollector/Jobs/ -- confirmed removed
- `TryGetDeviceByName` preserved in Pipeline/ (IDeviceRegistry + DeviceRegistry) for trap listener
