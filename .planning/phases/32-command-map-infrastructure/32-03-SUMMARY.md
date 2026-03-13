---
phase: 32-command-map-infrastructure
plan: 03
subsystem: pipeline
tags: [k8s, configmap, watcher, command-map, validation, json, di, background-service]

# Dependency graph
requires:
  - phase: 32-01
    provides: CommandMapService and ICommandMapService implementation with UpdateMap
  - phase: 30-oid-map-watcher
    provides: OidMapWatcherService pattern (watch loop, validate, hot-reload)
provides:
  - CommandMapWatcherService: BackgroundService watching simetra-commandmaps ConfigMap via K8s API
  - ValidateAndParseCommandMap: internal static 3-pass duplicate detection (OID keys + command names)
  - DI registration for CommandMapService (two-step singleton) and CommandMapWatcherService (K8s-only hosted service)
  - Local dev fallback in Program.cs loading commandmaps.json via ValidateAndParseCommandMap
  - 10 unit tests covering all validation paths
affects: [any future code calling ICommandMapService, command execution pipeline, operator tooling]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Two-step DI singleton: AddSingleton<Concrete>(factory) + AddSingleton<IFace>(sp => sp.GetRequired<Concrete>())"
    - "K8s ConfigMap watcher: initial load + watch loop + auto-reconnect on 30-min timeout"
    - "3-pass duplicate validation: pass 1 detect OID dups, pass 2 detect name dups, pass 3 build clean dict"
    - "Local dev fallback: ValidateAndParseCommandMap(File.ReadAllText(...)) then UpdateMap"

key-files:
  created:
    - src/SnmpCollector/Services/CommandMapWatcherService.cs
    - tests/SnmpCollector.Tests/Services/CommandMapWatcherValidationTests.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Program.cs

key-decisions:
  - "Empty map after validation logs WARNING only if rawEntries.Count > 0; empty JSON array [] is silent (locked decision from CONTEXT.md)"
  - "ValidateAndParseCommandMap returns null only on JSON parse failure; empty dictionary is a valid return"
  - "CommandMapWatcherService injected with ICommandMapService (interface), not CommandMapService (concrete)"
  - "OID forward map uses OrdinalIgnoreCase; command name reverse map uses Ordinal (case-sensitive names)"

patterns-established:
  - "All ConfigMap watchers follow the same pattern: initial load -> watch loop -> reconnect on timeout"
  - "Validation is always internal static to allow reuse from Program.cs local dev path"

# Metrics
duration: 12min
completed: 2026-03-13
---

# Phase 32 Plan 03: Command Map Watcher Service Summary

**CommandMapWatcherService with K8s watch loop, 3-pass OID+command-name duplicate validation, two-step DI registration, and local dev commandmaps.json fallback**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-03-13T~16:07:39Z
- **Completed:** 2026-03-13
- **Tasks:** 3
- **Files modified:** 4 (2 created, 2 modified)

## Accomplishments

- Created `CommandMapWatcherService` as a BackgroundService mirroring `OidMapWatcherService`, watching `simetra-commandmaps` ConfigMap with initial load, watch loop, and auto-reconnect
- Implemented `ValidateAndParseCommandMap` with 3-pass duplicate detection: pass 1 collects raw entries and detects duplicate OID keys, pass 2 detects duplicate command names, pass 3 builds clean dictionary with per-skip warning logs
- Wired `CommandMapService` via two-step DI singleton pattern (matching `OidMapService`) and registered `CommandMapWatcherService` as hosted service in K8s mode only
- Added `commandmaps.json` loading in `Program.cs` local dev block using `ValidateAndParseCommandMap` for validation consistency
- 10 unit tests covering all validation paths pass; 252 total tests pass with zero regressions

## Task Commits

1. **Task 1: Create CommandMapWatcherService** - `78ff750` (feat)
2. **Task 2: Wire DI registration and local dev fallback** - `b42251b` (feat)
3. **Task 3: Add validation unit tests** - `f00ffa4` (test)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `src/SnmpCollector/Services/CommandMapWatcherService.cs` - Background service watching simetra-commandmaps ConfigMap; `internal static ValidateAndParseCommandMap` with 3-pass validation
- `tests/SnmpCollector.Tests/Services/CommandMapWatcherValidationTests.cs` - 10 unit tests for all validation paths
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Two-step singleton for CommandMapService + CommandMapWatcherService as K8s-only hosted service
- `src/SnmpCollector/Program.cs` - Local dev fallback loading commandmaps.json

## Decisions Made

- Empty map after validation logs WARNING only if `rawEntries.Count > 0`; an empty JSON array `[]` is silent and valid (matches locked decision from CONTEXT.md)
- `ValidateAndParseCommandMap` returns `null` only on JSON parse failure -- empty dictionary is a valid return for an empty or fully-filtered map
- `CommandMapWatcherService` takes `ICommandMapService` (interface) rather than `CommandMapService` (concrete) to keep the watcher decoupled from the implementation detail

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None - `dotnet build` produced a spurious cache-file error on first run (`-q` flag + cached `--no-restore`); resolved by running without `--no-restore`. Zero actual compilation errors.

## Next Phase Readiness

- Command map infrastructure is fully wired: `ICommandMapService` resolves OIDs to command names and back in all environments
- `CommandMapWatcherService` handles hot-reload from K8s ConfigMap; local dev uses commandmaps.json
- Ready for any command execution pipeline that needs `ICommandMapService` injection
- No blockers or concerns

---
*Phase: 32-command-map-infrastructure*
*Completed: 2026-03-13*
