# Quick 067: Flatten tenants.json to Bare Array Format

**One-liner:** Flatten tenants.json from `{ "Tenants": [...] }` wrapper to bare `[...]` array, matching devices.json pattern across all config loading paths.

**Completed:** 2026-03-17
**Duration:** ~5 min

## Changes Made

### 1. Config file (tenants.json)
- Removed `{ "Tenants": [...] }` wrapper
- File now starts with `[` like devices.json

### 2. Program.cs (local dev loading)
- Removed `AddJsonFile` for tenants.json (bare array has no section key for config provider)
- Changed deserialization from `Deserialize<TenantVectorOptions>` to `Deserialize<List<TenantOptions>>`
- Wraps result into `TenantVectorOptions { Tenants = rawTenants }` before validation
- Matches devices.json loading pattern exactly

### 3. TenantVectorWatcherService (K8s ConfigMap loading)
- Changed `HandleConfigMapChangedAsync` deserialization from `Deserialize<TenantVectorOptions>` to `Deserialize<List<TenantOptions>>`
- Wraps result into `TenantVectorOptions` before validation and reload

### 4. K8s ConfigMap (simetra-tenants.yaml)
- Flattened embedded JSON from `{ "Tenants": [...] }` to bare `[...]`

### 5. E2E test fixture (28-tenantvector-routing.sh)
- Flattened inline 4-tenant ConfigMap from `{ "Tenants": [...] }` to bare `[...]`

## DI Binding (unchanged)

The `ServiceCollectionExtensions.cs` binding remains:
```csharp
services.AddOptions<TenantVectorOptions>()
    .Configure<IConfiguration>((opts, config) =>
        config.GetSection(TenantVectorOptions.SectionName).Bind(opts.Tenants))
```

This binding works via the K8s `appsettings.k8s.json` overlay (if it has a "Tenants" section) or defaults to an empty list. In local dev mode, Program.cs loads tenants manually -- the DI binding is a no-op, same as for devices.

## Verification

- `dotnet build`: 0 errors, 0 warnings
- `dotnet test`: 416/416 passed
- `head -3 tenants.json`: starts with `[`
- `head -3 devices.json`: starts with `[`
- Both files have consistent bare array structure

## Commits

| Hash | Description |
|------|-------------|
| 0bd9329 | Flatten tenants.json to bare array format |
| eaf9ace | Update Program.cs to load tenants.json as bare array |
| 5249bd2 | Update TenantVectorWatcherService for bare array deserialization |
| f5d902b | Flatten K8s simetra-tenants ConfigMap to bare array |
| acdde9b | Flatten E2E tenant ConfigMap fixture to bare array |

## Deviations from Plan

None -- plan executed exactly as written.

## Key Files

- `src/SnmpCollector/config/tenants.json`
- `src/SnmpCollector/Program.cs`
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs`
- `deploy/k8s/snmp-collector/simetra-tenants.yaml`
- `tests/e2e/scenarios/28-tenantvector-routing.sh`
