---
phase: 57-deterministic-watcher-startup-order
verified: 2026-03-18T16:00:00Z
status: passed
score: 7/7 must-haves verified
---

# Phase 57: Deterministic Watcher Startup Order Verification Report

**Phase Goal:** Enforce sequential initial load order for ConfigMap watchers -- OID metric map, devices, command map, tenants -- so the tenant watcher always validates against fully populated registries, eliminating false-positive skips from startup race conditions.
**Verified:** 2026-03-18T16:00:00Z
**Status:** passed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | In K8s mode, OidMapWatcher loads before DeviceWatcher, DeviceWatcher before CommandMapWatcher, CommandMapWatcher before TenantVectorWatcher | VERIFIED | Program.cs lines 62-95: sequential `await` calls in exact order OidMap->Devices->CommandMap->Tenants inside `IsInCluster()` block |
| 2 | In local-dev mode, command map loads BEFORE tenants (fixing the current wrong order) | VERIFIED | Program.cs lines 148-183: `oid_command_map.json` block (lines 148-161) precedes `tenants.json` block (lines 163-183); comment on line 149 explains why |
| 3 | Pod startup logs show sequential load order with per-watcher entry counts and timing | VERIFIED | Program.cs lines 88-94: structured log `"Startup sequence: OidMap={OidCount} ({OidTime:F1}s) -> Devices=..."` with all 4 counts + times + total |
| 4 | A summary INFO line shows all 4 watchers with counts and elapsed times | VERIFIED | Same as truth 3 -- single LogInformation call with named parameters for all 4 watchers |
| 5 | Initial load failure crashes the pod immediately (no catch, no retry) | VERIFIED | No try/catch wraps the K8s startup block (lines 62-95). The only try/catch in Program.cs is at line 221 for OptionsValidationException around `app.RunAsync()`, not around InitialLoadAsync calls |
| 6 | Hot-reload watch loops are unaffected -- BackgroundService ExecuteAsync still runs after host starts | VERIFIED | All 4 ExecuteAsync methods contain only `while (!stoppingToken.IsCancellationRequested)` watch loops. No initial-load code. Watch loops start when host calls StartAsync (after `app.RunAsync()`) |
| 7 | All existing unit tests pass | VERIFIED | `dotnet test` output: Passed! Failed: 0, Passed: 453, Skipped: 0 |

**Score:** 7/7 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/SnmpCollector/Services/OidMapWatcherService.cs` | public InitialLoadAsync, watch-loop-only ExecuteAsync | VERIFIED | Lines 59-66: `public async Task<int> InitialLoadAsync(CancellationToken ct)` calls LoadFromConfigMapAsync, logs count, returns count. ExecuteAsync (line 69) starts with `while` loop, no initial-load block |
| `src/SnmpCollector/Services/DeviceWatcherService.cs` | public InitialLoadAsync, watch-loop-only ExecuteAsync | VERIFIED | Lines 76-83: same pattern. Returns `_deviceRegistry.AllDevices.Count`. ExecuteAsync (line 86) is watch-loop-only |
| `src/SnmpCollector/Services/CommandMapWatcherService.cs` | public InitialLoadAsync, watch-loop-only ExecuteAsync | VERIFIED | Lines 59-66: same pattern. Returns `_commandMapService.Count`. ExecuteAsync (line 69) is watch-loop-only |
| `src/SnmpCollector/Services/TenantVectorWatcherService.cs` | public InitialLoadAsync, watch-loop-only ExecuteAsync | VERIFIED | Lines 500-507: same pattern. Returns `_registry.TenantCount`. ExecuteAsync (line 510) is watch-loop-only |
| `src/SnmpCollector/Program.cs` | Sequential startup calls in K8s path, fixed load order in local-dev path | VERIFIED | K8s block (lines 58-95) with 4 sequential InitialLoadAsync calls + timing. Local-dev block (lines 97-184) with corrected order: OidMap->Devices->CommandMap->Tenants |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| Program.cs K8s block | OidMapWatcherService.InitialLoadAsync | GetRequiredService + await | WIRED | Line 68-69: `GetRequiredService<OidMapWatcherService>()` then `await oidWatcher.InitialLoadAsync(CancellationToken.None)` |
| Program.cs K8s block | DeviceWatcherService.InitialLoadAsync | GetRequiredService + await | WIRED | Line 73-74: same pattern |
| Program.cs K8s block | CommandMapWatcherService.InitialLoadAsync | GetRequiredService + await | WIRED | Line 78-79: same pattern |
| Program.cs K8s block | TenantVectorWatcherService.InitialLoadAsync | GetRequiredService + await | WIRED | Line 83-84: same pattern |
| InitialLoadAsync (all 4) | LoadFromConfigMapAsync | direct call | WIRED | Each InitialLoadAsync calls `await LoadFromConfigMapAsync(ct).ConfigureAwait(false)` -- verified in all 4 watcher files |
| Program.cs local-dev | command map before tenants | code ordering | WIRED | `oid_command_map.json` block at line 148, `tenants.json` block at line 163 |

### Requirements Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| OidMap completes before Devices | SATISFIED | Sequential awaits enforce ordering |
| Devices completes before CommandMap | SATISFIED | Sequential awaits enforce ordering |
| CommandMap completes before Tenants | SATISFIED | Sequential awaits enforce ordering |
| Tenant validation runs against populated registries | SATISFIED | All 3 dependencies loaded before TenantVectorWatcher.InitialLoadAsync |
| Watch loops independent after startup | SATISFIED | ExecuteAsync is watch-loop-only in all 4 watchers |
| Startup logs with timing | SATISFIED | Structured summary log with per-watcher counts and elapsed seconds |
| No behavioral change after initial load | SATISFIED | 453/453 tests pass |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | - | - | - | No anti-patterns detected |

No TODO, FIXME, placeholder, or stub patterns found in modified files. No empty implementations. All InitialLoadAsync methods have real logic (call LoadFromConfigMapAsync, log, return count).

### Human Verification Required

### 1. K8s Pod Startup Log Visibility
**Test:** Deploy to a K8s cluster and check pod logs for the startup sequence summary line
**Expected:** `"Startup sequence: OidMap=N (Xs) -> Devices=N (Xs) -> CommandMap=N (Xs) -> Tenants=N (Xs) -- total Xs"` appears as a single INFO line
**Why human:** Requires actual K8s deployment to verify end-to-end log output

### 2. Crash-on-Failure Behavior
**Test:** Deploy with a missing/malformed ConfigMap and observe pod behavior
**Expected:** Pod crashes immediately at the failing watcher step, K8s restarts it
**Why human:** Requires K8s environment with intentional failure injection

### Gaps Summary

No gaps found. All 7 observable truths verified against actual codebase. All artifacts exist, are substantive, and are properly wired. Build succeeds with zero warnings, all 453 tests pass unchanged.

---

_Verified: 2026-03-18T16:00:00Z_
_Verifier: Claude (gsd-verifier)_
