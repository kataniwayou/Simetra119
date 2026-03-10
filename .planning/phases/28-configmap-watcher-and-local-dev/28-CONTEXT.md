# Phase 28: ConfigMap Watcher and Local Dev - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

TenantVectorWatcherService that hot-reloads tenant vector configuration from a K8s ConfigMap in production and loads once from a local file in development. Includes structured diff logging on reload via the existing TenantVectorRegistry.Reload() method. No OID map change subscription — operator maintains consistency between configs.

</domain>

<decisions>
## Implementation Decisions

### Design Pattern — Mirror Existing Watchers Exactly

- TenantVectorWatcherService MUST follow the exact same design pattern as OidMapWatcherService and DeviceWatcherService
- BackgroundService with ExecuteAsync: initial direct ConfigMap read, then watch loop with auto-reconnect (5s delay)
- SemaphoreSlim for reload serialization
- ReadNamespace() from service account file, fallback "simetra"
- K8s-only: registered in AddSnmpConfiguration inside IsInCluster() block (concrete + hosted service pattern)
- Local dev: load-once in Program.cs after app.Build() — no FileSystemWatcher
- Same log message patterns, same error handling, same structure

### ConfigMap Naming

- ConfigMap name: `simetra-tenantvector`
- Config key: `tenantvector.json`
- Follows existing naming convention: simetra-oidmaps, simetra-devices, simetra-tenantvector

### Watcher Lifecycle

- Initial load via direct ConfigMap read before watch loop starts
- Watch loop with automatic reconnect on K8s watch timeout (~30 min)
- 5s delay on unexpected disconnect before reconnect
- On ConfigMap deletion: log warning, retain current config (same as OidMapWatcher/DeviceWatcher)
- Graceful shutdown via CancellationToken

### Local Dev File Strategy

- Load tenantvector.json once in Program.cs after app.Build() (same section as oidmaps.json and devices.json loading)
- Validate config via TenantVectorOptionsValidator before calling Reload()
- Call TenantVectorRegistry.Reload() with validated options
- No file watching in local dev — load-once pattern, restart to pick up changes

### Validation on Reload

- Watcher validates config via TenantVectorOptionsValidator before calling Reload()
- If validation fails: log Error, retain current registry (same as existing watchers on parse errors — "reload failed, previous config remains active")
- Same validation in both K8s and local dev paths

### OID Map Change Subscription — Not Needed

- No cross-watcher event subscription
- TenantVectorWatcher only rebuilds when its own ConfigMap (simetra-tenantvector) changes
- OID map metric name renames are the operator's responsibility — they must update both configs
- Each watcher is fully independent

### Diff Logging

- TenantVectorRegistry.Reload() already logs structured diff (added/removed/unchanged tenants, carried-over slots)
- Watcher logs the event trigger (ConfigMap Added/Modified) at Information level
- No additional per-tenant metric delta logging from the watcher — Reload() logging is sufficient
- Log levels match existing watchers: Information for events, Warning for errors/deletions, Error for parse/validation failures

</decisions>

<specifics>
## Specific Ideas

- The watcher should be structurally identical to OidMapWatcherService — if someone reads one, they can understand the other immediately
- The local dev Program.cs block should sit alongside the existing OID map and devices loading blocks
- ConfigMap JSON structure is the TenantVectorOptions shape (already defined in Phase 25)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 28-configmap-watcher-and-local-dev*
*Context gathered: 2026-03-10*
