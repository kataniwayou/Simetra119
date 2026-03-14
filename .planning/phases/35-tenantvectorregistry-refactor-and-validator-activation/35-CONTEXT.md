# Phase 35: TenantVectorRegistry Refactor & Validator Activation - Context

**Gathered:** 2026-03-15
**Status:** Ready for planning

<domain>
## Phase Boundary

Align ALL watchers to the "watcher validates, registry stores" pattern. Move all validation and resolution logic from DeviceRegistry and TenantVectorRegistry into their respective watcher services. Both registries become pure data stores (FrozenDictionary swap + index build). Both options validators simplified to minimal (JSON structural checks only). Remove all cross-service dependencies from registries.

This is an architectural consistency refactor that also fulfills TEN-04, TEN-06, TEN-08, CLN-01, CLN-02.

</domain>

<decisions>
## Implementation Decisions

### Architectural Pattern — Watcher Validates, Registry Stores
- ALL four watchers follow the same pattern: parse → validate → resolve → pass clean data → registry swaps indexes
- OidMapWatcher and CommandMapWatcher already follow this pattern (no changes needed)
- DeviceWatcherService and TenantVectorWatcherService are refactored to follow this pattern
- Registries become pure stores — no validation, no cross-service dependencies, no DNS resolution

### DeviceWatcherService Refactor
- Validation moves from DeviceRegistry constructor/ReloadAsync into DeviceWatcherService
- DeviceWatcherService gains a `ValidateAndBuildDevices()` method that:
  - Extracts device name from CommunityString via TryExtractDeviceName (skip invalid with Error log)
  - Detects duplicate IP+Port (skip second with Error log)
  - Detects duplicate CommunityString with different IP+Port (Warning log, both load)
  - Resolves DNS names to IPs
  - Calls BuildPollGroups + filters zero-OID groups (Warning log)
  - Returns clean `List<DeviceInfo>` ready for registry consumption
- `DeviceRegistry.ReloadAsync()` signature changes to accept `List<DeviceInfo>` instead of `DevicesOptions`
- DeviceRegistry constructor changes to accept initial `List<DeviceInfo>` (or empty)
- DeviceRegistry no longer needs CommunityStringHelper, IOidMapService, or DNS resolution
- Same change applies to DeviceRegistry constructor (initial load path)

### TenantVectorWatcherService Refactor
- All Phase 34 validation logic moves from TenantVectorRegistry.Reload() into TenantVectorWatcherService
- TenantVectorWatcherService gains a `ValidateAndBuildTenants()` method that:
  - Per metric entry: structural checks (Ip, port, MetricName), Role validation, MetricName in OidMap, IP+Port in DeviceRegistry, resolve CommunityString
  - Per command entry: structural checks (Ip, port, CommandName), ValueType validation, non-empty Value, IP+Port in DeviceRegistry
  - TEN-13 completeness gate: ≥1 Resolved + ≥1 Evaluate metric + ≥1 command — skip tenant if not met
  - Returns clean tenant data ready for registry consumption
- TenantVectorWatcherService injects IOidMapService and IDeviceRegistry (watcher is the validation layer)
- `TenantVectorRegistry.Reload()` receives pre-validated, pre-resolved data — no raw config
- TenantVectorRegistry constructor: only ILogger (no IDeviceRegistry, no IOidMapService)

### CommandName Handling (TEN-06)
- Unresolvable CommandName: watcher logs Debug, passes entry through to registry as-is
- CommandName resolution deferred to execution time — watcher does NOT skip entries for unresolvable CommandName
- Empty CommandName is a structural error — skipped with Error log (different from unresolvable)

### Validator Simplification
- Both DevicesOptionsValidator and TenantVectorOptionsValidator simplified to minimal
- They check only: JSON parsed correctly, required arrays exist, basic structural sanity
- All per-entry validation lives in the watcher's ValidateAndBuild method
- This avoids duplication between validator and watcher

### Dead Code Removal
- TenantVectorRegistry.ResolveIp() — deleted (DNS resolution moves to watcher)
- TenantVectorRegistry.DeriveIntervalSeconds() — already deleted in Phase 33
- DeviceRegistry internal validation methods — moved to watcher, deleted from registry
- DevicesOptionsValidator.ValidateNoDuplicates() — moved to watcher

### Local Dev Path Consistency
- Program.cs local dev fallback must also call the validation/build methods
- Same ValidateAndBuild logic runs in both K8s watcher path and local dev path
- The methods should be static or accessible from both contexts

### Claude's Discretion
- Exact method signatures for ValidateAndBuild methods
- Whether ValidateAndBuild returns a new DTO type or reuses existing types
- How to structure the clean data passed to registries (List<DeviceInfo> for devices is natural; tenant data shape TBD)
- Test organization for moved validation logic
- Whether to keep BuildPollGroups in DeviceRegistry or move entirely to watcher

</decisions>

<specifics>
## Specific Ideas

- Pattern consistency table must hold after this phase:
  | Watcher | Validates | Service role |
  |---------|-----------|-------------|
  | OidMapWatcher | 3-pass duplicate detection | Pure store |
  | CommandMapWatcher | 3-pass duplicate detection | Pure store |
  | DeviceWatcher | CS extraction, dup IP+Port, zero-OID, DNS | Pure store |
  | TenantVectorWatcher | Structural, Role, MetricName, IP+Port, TEN-13 | Pure store |

- DeviceRegistry.ReloadAsync(List<DeviceInfo>) is the target — registry just builds FrozenDictionaries + notifies DynamicPollScheduler
- TenantVectorRegistry.Reload() receives clean data — just builds routing index + groups

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 35-tenantvectorregistry-refactor-and-validator-activation*
*Context gathered: 2026-03-15*
