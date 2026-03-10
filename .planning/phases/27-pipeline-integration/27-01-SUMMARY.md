---
phase: 27-pipeline-integration
plan: 01
subsystem: pipeline
tags: [mediatr, snmp, value-extraction, heartbeat, otel, c#]

# Dependency graph
requires:
  - phase: 25-config-models-and-validation
    provides: OidMapService with FrozenSet, IOidMapService interface
  - phase: 26-core-data-types-and-registry
    provides: TenantVectorRegistry, MetricSlot, MetricSlotHolder infrastructure
provides:
  - IsHeartbeat flag removed from SnmpOidReceived; heartbeat flows as normal metric via OidMapService
  - ExtractedValue + ExtractedStringValue properties on SnmpOidReceived
  - ValueExtractionBehavior: single TypeCode switch sets pre-extracted values once
  - OidMapService seeds heartbeat OID at construction and on every UpdateMap reload
  - OtelMetricHandler refactored to read pre-extracted values (no duplicate ISnmpData casting)
  - ValueExtractionBehavior registered as 5th in pipeline chain (after OidResolution)
affects:
  - 27-02 (TenantVectorFanOutBehavior reads ExtractedValue/ExtractedStringValue)
  - any future pipeline behavior that needs typed values

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Progressive pipeline enrichment: ValueExtractionBehavior enriches SnmpOidReceived in-place; downstream behaviors/handlers read pre-extracted properties
    - Heartbeat OID seeded internally in OidMapService.MergeWithHeartbeatSeed, not in configurable JSON
    - Open generic behavior registration order: Logging → Exception → Validation → OidResolution → ValueExtraction → Handler

key-files:
  created:
    - src/SnmpCollector/Pipeline/Behaviors/ValueExtractionBehavior.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/ValueExtractionBehaviorTests.cs
  modified:
    - src/SnmpCollector/Pipeline/SnmpOidReceived.cs
    - src/SnmpCollector/Pipeline/OidMapService.cs
    - src/SnmpCollector/Services/ChannelConsumerService.cs
    - src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
    - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
    - tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs
    - tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs

key-decisions:
  - "Heartbeat OID seeded via OidMapService.MergeWithHeartbeatSeed — survives every UpdateMap reload without appearing in configurable JSON"
  - "TypeCode switch lives only in ValueExtractionBehavior; OtelMetricHandler reads pre-extracted values — no duplicate switch"
  - "String truncation to 128 chars stays in OtelMetricHandler only — tenant vector gets full string value"
  - "OtelMetricHandler uses TypeCode.ToString().ToLowerInvariant() for snmpType label — equivalent to prior per-case literals"

patterns-established:
  - "ValueExtractionBehavior pattern: check notification is SnmpOidReceived, switch on TypeCode, set ExtractedValue/ExtractedStringValue, always call next()"
  - "OidMapService always seeds heartbeat OID before building FrozenDictionary — MergeWithHeartbeatSeed called in both constructor and UpdateMap"

# Metrics
duration: 7min
completed: 2026-03-10
---

# Phase 27 Plan 01: Pipeline Integration Summary

**Heartbeat normalization (IsHeartbeat removed), shared value extraction via ValueExtractionBehavior, and OtelMetricHandler refactored to read pre-extracted ExtractedValue/ExtractedStringValue from SnmpOidReceived**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-10T19:23:17Z
- **Completed:** 2026-03-10T19:30:21Z
- **Tasks:** 2
- **Files modified:** 10

## Accomplishments
- IsHeartbeat flag fully removed from SnmpOidReceived and all consumers; heartbeat OID now resolves to "heartbeat" metric name via normal OID resolution
- ValueExtractionBehavior created: single TypeCode switch (Integer32, Gauge32, TimeTicks, Counter32, Counter64, OctetString, IPAddress, ObjectIdentifier) sets ExtractedValue and ExtractedStringValue once per message
- OtelMetricHandler collapsed from 7-case per-type switch to 2-block switch reading pre-extracted values; registered ValueExtractionBehavior as 5th in MediatR chain

## Task Commits

Each task was committed atomically:

1. **Task 1: Heartbeat normalization — remove IsHeartbeat, seed OidMapService** - `c9fec12` (feat)
2. **Task 2: ValueExtractionBehavior and OtelMetricHandler refactor** - `8a6d64e` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/SnmpCollector/Pipeline/Behaviors/ValueExtractionBehavior.cs` - New behavior: TypeCode switch sets ExtractedValue + ExtractedStringValue
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` - IsHeartbeat removed; ExtractedValue + ExtractedStringValue added
- `src/SnmpCollector/Pipeline/OidMapService.cs` - MergeWithHeartbeatSeed added; seeds heartbeat OID in constructor and UpdateMap
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` - IsHeartbeat guard removed; always resolves OID
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` - IsHeartbeat early-return removed; reads pre-extracted values
- `src/SnmpCollector/Services/ChannelConsumerService.cs` - IsHeartbeat assignment removed + unused using removed
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - ValueExtractionBehavior registered as 5th behavior
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/ValueExtractionBehaviorTests.cs` - New: 6 tests for all TypeCode cases + pass-through
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs` - SkipsResolution_WhenIsHeartbeat replaced with ResolvesHeartbeatOid_ViaOidMapService
- `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` - Heartbeat test rewritten; MakeNotification adds extractedValue/extractedStringValue params

## Decisions Made
- Heartbeat OID seeded via `MergeWithHeartbeatSeed` private method — called in both constructor and `UpdateMap` so seed survives every ConfigMap hot-reload
- `OtelMetricHandler` uses `notification.TypeCode.ToString().ToLowerInvariant()` for snmpType label — produces identical lowercase strings to prior per-case literals (`"integer32"`, `"gauge32"`, etc.)
- String truncation at 128 chars stays in `OtelMetricHandler` only; `ValueExtractionBehavior` stores the full string in `ExtractedStringValue` for tenant vector fan-out in Plan 02

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed stale OidMapServiceTests.EntryCount_MatchesDictionarySize**
- **Found during:** Task 2 (full test suite run)
- **Issue:** Test expected `EntryCount == 3` but heartbeat seed adds 1 entry, making actual count 4
- **Fix:** Updated assertion to `Assert.Equal(4, sut.EntryCount)` with explanatory comment
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs`
- **Verification:** Test passes after fix
- **Committed in:** `8a6d64e` (Task 2 commit)

**2. [Rule 1 - Bug] Fixed stale OidMapAutoScanTests.ObpOidMapHas24Entries**
- **Found during:** Task 2 (full test suite run)
- **Issue:** oidmaps.json was updated in commit `3939ba9` (quick-027) adding 3 static OBP info OIDs (`obp_device_type`, `obp_sw_version`, `obp_serial`), making total 27; test still expected 24
- **Fix:** Updated count assertion from 24 to 27
- **Files modified:** `tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs`
- **Verification:** Test passes after fix
- **Committed in:** `8a6d64e` (Task 2 commit)

**3. [Rule 1 - Bug] Fixed stale OidMapAutoScanTests.ObpOidNamingConventionIsConsistent**
- **Found during:** Task 2 (full test suite run)
- **Issue:** Naming convention regex `^obp_(link_state|channel|r[1-4]_power)_L[1-4]$` did not include the 3 new static info names (`obp_device_type`, `obp_sw_version`, `obp_serial`)
- **Fix:** Extended regex to `^obp_(link_state|channel|r[1-4]_power)_L[1-4]$|^obp_(device_type|sw_version|serial)$`
- **Files modified:** `tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs`
- **Verification:** Test passes after fix
- **Committed in:** `8a6d64e` (Task 2 commit)

**4. [Rule 1 - Bug] Fixed stale OtelMetricHandlerTests (integration tests) expecting direct ISnmpData cast results**
- **Found during:** Task 2 (full test suite run)
- **Issue:** `PipelineIntegrationTests.SendInteger32_GaugeRecorded_WithCorrectLabels` and `SendCounter32_GaugeRecorded` expected value from direct cast; after refactor handler reads `ExtractedValue` (0 if not set by ValueExtractionBehavior)
- **Fix:** Two-part: (1) registered `ValueExtractionBehavior` in `ServiceCollectionExtensions.AddSnmpPipeline` so integration tests run full pipeline; (2) updated unit test `MakeNotification` to accept `extractedValue`/`extractedStringValue` params
- **Files modified:** `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs`, `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs`
- **Verification:** All 197 tests pass
- **Committed in:** `8a6d64e` (Task 2 commit)

---

**Total deviations:** 4 auto-fixed (all Rule 1 — pre-existing stale tests made visible by plan changes)
**Impact on plan:** All fixes necessary for correctness. The ValueExtractionBehavior DI registration was an implicit requirement — integration tests confirmed it. No scope creep.

## Issues Encountered
- Three OidMapAutoScan tests were already broken (pre-existing failure from quick-027 oidmaps.json update) — surfaced by running full suite. Fixed all three in Task 2 commit.

## Next Phase Readiness
- Plan 02 (TenantVectorFanOutBehavior) can now read `msg.ExtractedValue` and `msg.ExtractedStringValue` directly — no extraction needed in fan-out behavior
- ValueExtractionBehavior is registered at position 5 in chain; Plan 02 adds FanOutBehavior at position 6
- `SnmpOidReceived` has no more IsHeartbeat — all remaining code paths treat heartbeat as a normal metric with MetricName="heartbeat"

---
*Phase: 27-pipeline-integration*
*Completed: 2026-03-10*
