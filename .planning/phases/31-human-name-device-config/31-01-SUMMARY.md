---
phase: 31
plan: 01
subsystem: oidmap-parsing
tags: [oidmap, array-format, rename, config, PollOptions, EnumerateArray]
depends-on:
  requires: [30-02]
  provides: [array-format-oidmap-parsing, PollOptions-model]
  affects: [31-02, 31-03]
tech-stack:
  added: []
  patterns: [array-of-objects-oidmap, JsonDocument-EnumerateArray]
key-files:
  created:
    - src/SnmpCollector/Configuration/PollOptions.cs
  modified:
    - src/SnmpCollector/Services/OidMapWatcherService.cs
    - src/SnmpCollector/Program.cs
    - src/SnmpCollector/config/oidmaps.json
    - deploy/k8s/snmp-collector/simetra-oidmaps.yaml
    - src/SnmpCollector/Configuration/DeviceOptions.cs
    - src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - src/SnmpCollector/Pipeline/MetricPollInfo.cs
    - src/SnmpCollector/appsettings.Development.json
    - tests/SnmpCollector.Tests/Services/OidMapWatcherValidationTests.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
    - tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
    - tests/e2e/fixtures/oid-added-configmap.yaml
    - tests/e2e/fixtures/oid-removed-configmap.yaml
    - tests/e2e/fixtures/oid-renamed-configmap.yaml
    - tests/e2e/fixtures/invalid-json-oidmaps-schema-configmap.yaml
  deleted:
    - src/SnmpCollector/Configuration/MetricPollOptions.cs
decisions:
  - "Array-format oidmap: [{ Oid, MetricName }] replaces flat { OID: name } dictionary"
  - "ValidateAndParseOidMap returns null for non-array JSON (new ValueKind.Array guard)"
  - "Program.cs uses ValidateAndParseOidMap for local dev loading (single source of truth)"
  - "MetricPollOptions -> PollOptions, MetricPolls -> Polls, Oids -> MetricNames (full rename)"
  - "MetricPollInfo.Oids retains its name -- holds resolved OIDs at runtime, not config names"
  - "6 previously K8s-missing entries added to simetra-oidmaps.yaml (obp_device_type/sw_version/serial + npb_model/serial/sw_version)"
  - "invalid-schema E2E fixture changed from string-array to object to test ValueKind.Array guard"
metrics:
  duration: "9m 35s"
  completed: "2026-03-13"
---

# Phase 31 Plan 01: OidMap Array Format + Model Rename Summary

**One-liner:** OidMapWatcherService rewritten to parse array-of-objects `[{Oid, MetricName}]` format; full mechanical rename of MetricPollOptions->PollOptions/MetricPolls->Polls/Oids->MetricNames throughout codebase.

## What Was Built

### Task 1: OidMap Array-of-Objects Format

**OidMapWatcherService.ValidateAndParseOidMap** was rewritten from `EnumerateObject` (flat dictionary) to `EnumerateArray` with `Oid`/`MetricName` property access. The 3-pass duplicate detection logic (OID dedup, name dedup, clean build) was preserved exactly. A new early guard checks `ValueKind.Array` before enumeration.

**Program.cs** local dev loading now calls `ValidateAndParseOidMap` instead of `JsonSerializer.Deserialize<Dictionary<string,string>>`, establishing a single source of truth for oidmap parsing.

**config/oidmaps.json** was converted from flat dict to array format (98 entries). **simetra-oidmaps.yaml** was also converted, and the 6 previously-missing production entries were added (obp_device_type, obp_sw_version, obp_serial, npb_model, npb_serial, npb_sw_version), bringing the total to 105 entries.

**All 10 OidMapWatcher validation tests** pass with the new array JSON format (9 rewritten + 1 new `NonArrayJson_ReturnsNull` test for the ValueKind.Array guard).

**E2E fixtures** (oid-added, oid-removed, oid-renamed, invalid-schema) were all updated to array format. The invalid-schema fixture was changed from a string array `["this","is",...]` to `{ "not": "an array" }` to specifically test the new non-array guard.

### Task 2: Mechanical C# Model Rename

Pure mechanical rename with zero behavioral changes:

| Old Name | New Name | Location |
|----------|----------|----------|
| `MetricPollOptions` (class) | `PollOptions` | `Configuration/PollOptions.cs` (renamed from MetricPollOptions.cs) |
| `DeviceOptions.MetricPolls` | `DeviceOptions.Polls` | `Configuration/DeviceOptions.cs` |
| `PollOptions.Oids` | `PollOptions.MetricNames` | `Configuration/PollOptions.cs` |
| `ValidateMetricPoll()` | `ValidatePoll()` | `DevicesOptionsValidator.cs` |
| `"MetricPolls"` (JSON key) | `"Polls"` | `appsettings.Development.json` |
| `"Oids"` (JSON key) | `"MetricNames"` | `appsettings.Development.json` |

`MetricPollInfo.Oids` retains its name - it holds resolved OIDs at runtime (not config-level metric names). At this stage, MetricNames from config are passed directly to MetricPollInfo.Oids (name resolution happens in Plan 02).

## Verification Results

- Build: zero errors, zero warnings
- Tests: 225/225 pass (10 OidMapWatcher tests, 17 DeviceRegistry tests, pipeline integration tests)
- `grep "EnumerateObject" OidMapWatcherService.cs` - no matches
- `grep -c "MetricName" config/oidmaps.json` - 98
- `grep -c "MetricName" simetra-oidmaps.yaml` - 105
- `grep "Deserialize<Dictionary" Program.cs` - no matches
- `grep -rn "MetricPollOptions|\.MetricPolls\b" src/ --include="*.cs"` - no matches
- `grep -rn "MetricPolls" src/ --include="*.cs"` - no matches

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] OidMapAutoScanTests.LoadOidMap() broken by array format change**

- **Found during:** Task 2 test run (after Task 1 converted oidmaps.json)
- **Issue:** `OidMapAutoScanTests.LoadOidMap()` called `JsonSerializer.Deserialize<Dictionary<string,string>>` which fails on array JSON. 3 tests failed.
- **Fix:** Updated `LoadOidMap()` to use `JsonDocument.Parse` + `EnumerateArray` with Oid/MetricName property access - matching production parsing pattern.
- **Files modified:** `tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs`
- **Commit:** `5336842`

## Next Phase Readiness

Plan 02 (device name resolution) can proceed:
- `PollOptions.MetricNames` is the new config property Plan 02 reads
- `IOidMapService.ResolveToOid()` will be called in DeviceWatcherService to resolve each MetricName to an OID at config load time
- `DeviceRegistry` passes MetricNames directly to MetricPollInfo.Oids for now (Plan 02 adds the resolution step)
- All E2E fixtures are on the new array format baseline with 6 previously-missing OIDs now present
