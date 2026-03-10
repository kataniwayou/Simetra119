---
phase: 28-configmap-watcher-and-local-dev
plan: 01
subsystem: infra
tags: [k8s, configmap, watcher, tenant-vector, di, hot-reload]

# Dependency graph
requires:
  - phase: 25-config-models-and-validation
    provides: TenantVectorOptions, TenantVectorOptionsValidator
  - phase: 26-tenant-vector-registry
    provides: TenantVectorRegistry with Reload() and volatile swap
  - phase: 15-oid-map-and-device-watchers
    provides: OidMapWatcherService pattern to mirror exactly
provides:
  - TenantVectorWatcherService: K8s ConfigMap watcher for simetra-tenantvector with auto-reconnect and validation gating
  - Concrete-first DI registration for TenantVectorOptionsValidator (single instance for both roles)
  - Local dev load-once block in Program.cs: tenantvector.json section extraction, validate, Reload()
affects:
  - phase: 29-e2e-tests (will need TenantVectorWatcherService to verify hot-reload in cluster)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Concrete-first singleton pattern: AddSingleton<ConcreteType>() then AddSingleton<IInterface>(sp => sp.GetRequiredService<ConcreteType>()) ensures single instance for multiple resolution paths
    - Validation-gated ConfigMap reload: deserialize -> null-check -> Validate() -> if !Failed -> Reload() -- invalid config logs Error, previous config retained
    - JsonDocument.Parse + TryGetProperty for section extraction in local dev (handles IConfiguration section-wrapped files)

key-files:
  created:
    - src/SnmpCollector/Services/TenantVectorWatcherService.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Program.cs

key-decisions:
  - "TenantVectorWatcherService injects TenantVectorRegistry (concrete) not ITenantVectorRegistry -- Reload() is not on the interface"
  - "Validation gates reload: if TenantVectorOptionsValidator.Validate() returns Failed, log Error and return without calling Reload()"
  - "Local dev uses JsonDocument.Parse + TryGetProperty('TenantVector') to extract inner section from IConfiguration-wrapped file format"
  - "JsonElement.Deserialize<T>() not available without explicit using -- used JsonSerializer.Deserialize(tvElement.GetRawText(), jsonOptions) instead"

patterns-established:
  - "Concrete-first validator DI: AddSingleton<TenantVectorOptionsValidator>() + AddSingleton<IValidateOptions<TenantVectorOptions>>(sp => sp.GetRequiredService<TenantVectorOptionsValidator>())"
  - "ConfigMap watcher structure: initial load -> watch loop -> WatchAsync -> Added/Modified -> HandleConfigMapChangedAsync -> Deleted logs Warning"

# Metrics
duration: 3min
completed: 2026-03-10
---

# Phase 28 Plan 01: ConfigMap Watcher and Local Dev Summary

**TenantVectorWatcherService added as K8s ConfigMap watcher for simetra-tenantvector with validation-gated reload, concrete-first DI registration, and local dev load-once block**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-10T20:17:04Z
- **Completed:** 2026-03-10T20:19:43Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- TenantVectorWatcherService created as structural mirror of OidMapWatcherService with added validation step before Reload()
- Concrete-first DI registration ensures TenantVectorOptionsValidator is a single instance for both IValidateOptions<T> framework validation and direct watcher injection
- Local dev Program.cs block extracts TenantVector section from the IConfiguration-wrapped file format, validates, and calls Reload()

## Task Commits

Each task was committed atomically:

1. **Task 1: TenantVectorWatcherService — K8s ConfigMap watcher** - `5fb5347` (feat)
2. **Task 2: DI registration and local dev loading** - `a547dd7` (feat)

**Plan metadata:** (this commit — docs: complete plan)

## Files Created/Modified
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` - K8s watch loop, auto-reconnect, validation-gated reload, SemaphoreSlim serialization
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - Concrete-first TenantVectorOptionsValidator registration; TenantVectorWatcherService in IsInCluster() block
- `src/SnmpCollector/Program.cs` - Local dev load block: parse tenantvector.json, extract TenantVector section, validate, Reload()

## Decisions Made
- TenantVectorWatcherService injects `TenantVectorRegistry` (concrete) not `ITenantVectorRegistry` — `Reload()` is only on the concrete type, not the interface
- Validation gates reload: `_validator.Validate(null, options)` called before `_registry.Reload()`. Failure logs Error and returns without reloading — previous config retained
- Local dev file format uses IConfiguration section wrapper `{ "TenantVector": { "Tenants": [...] } }`, so `JsonDocument.Parse` + `TryGetProperty("TenantVector")` extracts the inner object before `JsonSerializer.Deserialize<TenantVectorOptions>`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] JsonElement.Deserialize<T>() not available without explicit using directive**
- **Found during:** Task 2 (DI registration and local dev loading)
- **Issue:** `tvElement.Deserialize<TenantVectorOptions>(jsonOptions)` — `JsonElement.Deserialize<T>` extension method requires `using System.Text.Json` but Program.cs uses fully-qualified names throughout
- **Fix:** Used `System.Text.Json.JsonSerializer.Deserialize<TenantVectorOptions>(tvElement.GetRawText(), jsonOptions)` instead — functionally identical, no new using needed
- **Files modified:** src/SnmpCollector/Program.cs
- **Verification:** Build succeeded with 0 errors
- **Committed in:** a547dd7 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug)
**Impact on plan:** Fix was necessary for compilation. Functionally identical to planned code.

## Issues Encountered
None beyond the JsonElement.Deserialize fix above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- TenantVectorWatcherService is ready to watch the simetra-tenantvector ConfigMap in cluster
- Local dev loads tenantvector.json once at startup with full validation
- Concrete-first validator pattern in place for both K8s hot-reload and IOptions<T> framework validation

---
*Phase: 28-configmap-watcher-and-local-dev*
*Completed: 2026-03-10*
