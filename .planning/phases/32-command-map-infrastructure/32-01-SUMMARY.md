---
phase: 32-command-map-infrastructure
plan: 01
subsystem: pipeline
tags: [frozen-dictionary, command-map, snmp, hot-reload, bidirectional-lookup]

# Dependency graph
requires:
  - phase: 30-oid-map-infrastructure
    provides: OidMapService volatile FrozenDictionary atomic swap pattern and reverse map design
provides:
  - ICommandMapService interface with ResolveCommandName, ResolveCommandOid, GetAllCommandNames, Contains, Count, UpdateMap
  - CommandMapService singleton with volatile FrozenDictionary forward+reverse maps and atomic swap
  - commandmaps.json seed file with 12 real command entries (4 OBP bypass + 8 NPB counter reset)
  - 12 unit tests covering all lookup and hot-reload behaviors
affects:
  - 32-02 (CommandMapWatcherService will call UpdateMap)
  - 32-03 (DI registration and E2E validation)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Bidirectional FrozenDictionary lookup: forward (OID->name) + reverse (name->OID) as separate volatile fields"
    - "Null-return contract for unknown lookups (no sentinel value, unlike OidMapService.Unknown)"
    - "Empty map is valid initial state -- no heartbeat seed"

key-files:
  created:
    - src/SnmpCollector/Pipeline/ICommandMapService.cs
    - src/SnmpCollector/Pipeline/CommandMapService.cs
    - src/SnmpCollector/config/commandmaps.json
    - tests/SnmpCollector.Tests/Pipeline/CommandMapServiceTests.cs
  modified: []

key-decisions:
  - "No Unknown sentinel -- unknown OIDs/command names return null (differentiates from OidMapService)"
  - "No heartbeat seed -- empty map is valid, commands are optional infrastructure until SNMP SET is implemented"
  - "Reverse map uses StringComparer.Ordinal (command names are case-sensitive); forward map uses OrdinalIgnoreCase (OIDs)"
  - "GetAllCommandNames() returns _reverseMap.Keys as IReadOnlyCollection<string>"

patterns-established:
  - "CommandMapService follows OidMapService pattern exactly except: no MergeWithHeartbeatSeed, null return instead of Unknown sentinel"
  - "Log prefix 'CommandMap' mirrors 'OidMap' prefix in OidMapService structured log events"

# Metrics
duration: 2min
completed: 2026-03-13
---

# Phase 32 Plan 01: Command Map Infrastructure - Core Service Summary

**Bidirectional OID-to-command-name lookup service with volatile FrozenDictionary atomic swap, null-return contract, and 12-entry seed data (4 OBP bypass + 8 NPB counter reset)**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-13T10:02:43Z
- **Completed:** 2026-03-13T10:04:13Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- ICommandMapService interface declares all six methods/properties with XML documentation
- CommandMapService singleton with two volatile FrozenDictionary fields (forward OID->name, reverse name->OID), atomic double-swap on UpdateMap, structured diff logging
- commandmaps.json seed with 12 real device commands organized as array-of-objects format
- 12 unit tests covering forward/reverse lookups, GetAllCommandNames, Contains, Count, and UpdateMap hot-reload with additions, removals, and name changes

## Task Commits

Each task was committed atomically:

1. **Task 1: ICommandMapService interface, CommandMapService, commandmaps.json** - `37c50b0` (feat)
2. **Task 2: CommandMapServiceTests** - `58a970d` (test)

## Files Created/Modified
- `src/SnmpCollector/Pipeline/ICommandMapService.cs` - Interface with ResolveCommandName, ResolveCommandOid, GetAllCommandNames, Contains, Count, UpdateMap
- `src/SnmpCollector/Pipeline/CommandMapService.cs` - Sealed singleton, volatile FrozenDictionary forward+reverse maps, atomic swap, structured diff log
- `src/SnmpCollector/config/commandmaps.json` - 12 seed entries: 4 OBP bypass (L1-L4) + 8 NPB counter reset (P1-P8)
- `tests/SnmpCollector.Tests/Pipeline/CommandMapServiceTests.cs` - 12 unit tests, all passing

## Decisions Made
- No `Unknown` sentinel (unlike OidMapService) -- unknown lookups return null. Command map is used programmatically (not for Grafana label display) so null is the correct contract.
- No heartbeat seed -- commands are optional infrastructure with no mandatory entries. Empty map is valid.
- Forward map comparer is `OrdinalIgnoreCase` (OID strings); reverse map comparer is `Ordinal` (command names are case-sensitive by convention).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- ICommandMapService and CommandMapService are complete and tested, ready for Plan 32-02 (CommandMapWatcherService)
- commandmaps.json seed data provides local dev config for watcher integration in 32-02

---
*Phase: 32-command-map-infrastructure*
*Completed: 2026-03-13*
