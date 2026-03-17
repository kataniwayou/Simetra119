# Quick 066: Fix tenants.json config binding to match devices.json pattern

**One-liner:** Removed double-nested tenants.json format and switched DI binding to delegate pattern matching devices.json

## Changes Made

### Step 1: DI binding (ServiceCollectionExtensions.cs)
Changed `TenantVectorOptions` registration from standard `.Bind()` to delegate binding:
```csharp
// Before: .Bind(configuration.GetSection(TenantVectorOptions.SectionName))
// After:
.Configure<IConfiguration>((opts, config) =>
    config.GetSection(TenantVectorOptions.SectionName).Bind(opts.Tenants))
```
This matches the `DevicesOptions` pattern at line 276-278.

### Step 2: Config files (tenants.json)
Flattened local dev config from `{ "Tenants": { "Tenants": [...] } }` to `{ "Tenants": [...] }`.
K8s ConfigMaps (`simetra-tenants.yaml`, `production/configmap.yaml`) already used the flat format.

### Step 3: Program.cs local dev loading
Removed `JsonDocument` workaround that extracted the inner "Tenants" object.
Now deserializes directly into `TenantVectorOptions` -- same as `TenantVectorWatcherService.HandleConfigMapChangedAsync`.

## Files Modified

| File | Change |
|------|--------|
| `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` | Delegate binding for TenantVectorOptions |
| `src/SnmpCollector/config/tenants.json` | Flattened from double-nested to flat format |
| `src/SnmpCollector/Program.cs` | Simplified tenant loading (removed JsonDocument workaround) |

## Verification

- `dotnet build`: 0 warnings, 0 errors
- `dotnet test`: 416/416 passed
- `grep "Tenants.*Tenants"` across `src/`: zero matches

## Commits

| Hash | Description |
|------|-------------|
| c0b85d7 | fix(066): change TenantVectorOptions DI binding to delegate pattern |
| 94c4791 | fix(066): flatten tenants.json to match devices.json pattern |
| 49e9a2b | fix(066): simplify Program.cs tenant loading -- remove JsonDocument workaround |

## Deviations from Plan

None -- plan executed exactly as written.

## Duration

~3 minutes
