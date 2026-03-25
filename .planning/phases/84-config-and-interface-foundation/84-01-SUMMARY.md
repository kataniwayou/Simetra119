---
phase: 84-config-and-interface-foundation
plan: 01
subsystem: telemetry
tags: [preferred-leader, lease-election, di-registration, options-pattern, null-object]

# Dependency graph
requires:
  - phase: 65-e2e-runner-fixes (leader election infra already in place via K8sLeaseElection)
    provides: K8sLeaseElection, ILeaderElection, AlwaysLeaderElection, ServiceCollectionExtensions DI patterns
provides:
  - LeaseOptions.PreferredNode nullable string field
  - IPreferredStampReader interface (single bool IsPreferredStampFresh)
  - NullPreferredStampReader null-object (local dev, always false)
  - PreferredLeaderService stub (identity resolution at construction, IsPreferredStampFresh=false)
  - DI wiring: K8s path concrete-first singleton, local dev NullPreferredStampReader
  - 8 unit tests covering identity resolution, stub, DI patterns, null-object
affects:
  - phase 85 (heartbeat writer: injects PreferredLeaderService, adds IHostedService, writes lease)
  - phase 86 (readiness gate: reads IPreferredStampReader.IsPreferredStampFresh)
  - phase 87 (non-preferred reader: updates IsPreferredStampFresh in memory)
  - phase 88 (voluntary yield: checks IsPreferredPod for yield decision)
  - phase 89 (validator: CFG-04 heartbeat lease name differs from leadership lease name)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Concrete-first DI singleton: AddSingleton<Concrete>() then forward lambda to avoid dual instances"
    - "Null-object pattern: NullPreferredStampReader returns false for all local dev paths"
    - "Readonly bool resolved once at construction from env var — no re-evaluation per call"
    - "Optional nullable config field: no DataAnnotations, no validator changes, backward compatible"

key-files:
  created:
    - src/SnmpCollector/Telemetry/IPreferredStampReader.cs
    - src/SnmpCollector/Telemetry/NullPreferredStampReader.cs
    - src/SnmpCollector/Telemetry/PreferredLeaderService.cs
    - tests/SnmpCollector.Tests/Telemetry/PreferredLeaderServiceTests.cs
  modified:
    - src/SnmpCollector/Configuration/LeaseOptions.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "PreferredNode over PreferredNodeName: shorter, matches K8s concept"
  - "_isPreferredPod owned by PreferredLeaderService (not a shared wrapper): natural owner"
  - "Registration after K8sLeaseElection block, before ConfigMap watchers: grouped with election services"
  - "No AddHostedService in Phase 84: no background loop until Phase 85"
  - "PHYSICAL_HOSTNAME (not NODE_NAME): matches existing deployment manifest injection"

patterns-established:
  - "IPreferredStampReader in SnmpCollector.Telemetry namespace (not Configuration): runtime state contract"
  - "Environment variable read once in constructor, stored as readonly bool"
  - "Warn-and-disable pattern: PHYSICAL_HOSTNAME empty when PreferredNode configured logs Warning, sets false, no crash"

# Metrics
duration: 2min
completed: 2026-03-25
---

# Phase 84 Plan 01: Config and Interface Foundation Summary

**LeaseOptions.PreferredNode field, IPreferredStampReader interface, NullPreferredStampReader null-object, and PreferredLeaderService identity-resolution stub wired into DI — v3.0 preferred leader foundation locked in code**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-25T21:33:17Z
- **Completed:** 2026-03-25T21:35:28Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- LeaseOptions extended with optional nullable PreferredNode string (no validation, fully backward compatible)
- IPreferredStampReader interface and NullPreferredStampReader null-object establish the stable contract downstream phases build against
- PreferredLeaderService resolves _isPreferredPod once at construction from PHYSICAL_HOSTNAME — logs identity at Information level, warns and disables gracefully when env var missing
- DI wired: K8s path uses concrete-first singleton pattern; local dev registers NullPreferredStampReader
- 8 unit tests pass covering all identity resolution branches, stub behavior, DI singleton correctness, and null-object

## Task Commits

Each task was committed atomically:

1. **Task 1: Interface, implementations, and LeaseOptions extension** - `b2b388e` (feat)
2. **Task 2: DI registration and unit tests** - `fcc6555` (feat)

**Plan metadata:** (docs commit follows this summary)

## Files Created/Modified

- `src/SnmpCollector/Configuration/LeaseOptions.cs` - Added PreferredNode nullable string property
- `src/SnmpCollector/Telemetry/IPreferredStampReader.cs` - Single-bool interface with thread-safety doc
- `src/SnmpCollector/Telemetry/NullPreferredStampReader.cs` - Sealed null-object, always returns false
- `src/SnmpCollector/Telemetry/PreferredLeaderService.cs` - Startup identity resolver + IPreferredStampReader stub
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - DI registration in K8s and local dev branches
- `tests/SnmpCollector.Tests/Telemetry/PreferredLeaderServiceTests.cs` - 8 unit tests

## Decisions Made

- **PreferredNode vs PreferredNodeName:** Chose `PreferredNode` — shorter, matches K8s "node" concept, less verbose
- **_isPreferredPod ownership:** Placed in `PreferredLeaderService` (natural owner of identity at startup)
- **Registration placement:** After K8sLeaseElection block, before ConfigMap watchers — grouped with election services
- **No AddHostedService in Phase 84:** Background loop is Phase 85 scope only; singleton registration is sufficient
- **PHYSICAL_HOSTNAME:** Confirmed correct env var name from deployment manifest (not NODE_NAME)

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Phase 85 (heartbeat writer): ready — PreferredLeaderService.IsPreferredPod provides identity; AddHostedService<PreferredLeaderService>() + real IsPreferredStampFresh logic is the only addition needed
- Phase 86 (readiness gate): ready — IPreferredStampReader interface stable; gate consumers can resolve it from DI today
- Phase 89 (CFG-04 validator): ready — LeaseOptions.PreferredNode exists; validator can derive heartbeat lease name and check for collision

Concerns carried forward (unchanged from STATE.md):
- Phase 86: Readiness gate mechanism not yet selected (ApplicationStarted vs IHealthCheckService poll vs TaskCompletionSource)
- Phase 88: LeaderElector behavior after mid-renewal cancellation unconfirmed

---
*Phase: 84-config-and-interface-foundation*
*Completed: 2026-03-25*
