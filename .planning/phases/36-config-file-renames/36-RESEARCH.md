# Phase 36: Config File Renames - Research

**Researched:** 2026-03-15
**Domain:** Mechanical rename — config file names, ConfigMap names, C# constants, K8s manifests, E2E scripts
**Confidence:** HIGH — all findings are direct grep results from the codebase

---

## Summary

This is a pure find-and-replace phase. There are no logic changes. Three sets of renames, each touching the same categories of files: local dev config JSON, standalone K8s ConfigMap YAML, production combined configmap.yaml, C# watcher service constants, Program.cs local dev loading, and E2E test fixtures.

The critical question for planning is: **exactly which files and which lines change?** This research answers that exhaustively. No library research needed — the technique is grep-and-replace.

**Primary recommendation:** Treat each of the three renames as an independent, atomic task. Each rename touches ~5–7 files. Commit after each rename so rollback is trivial.

---

## Complete Change Inventory

### Rename 1: `tenantvector.json` → `tenants.json` / `simetra-tenantvector` → `simetra-tenants`

#### C# source files

| File | What changes |
|------|-------------|
| `src/SnmpCollector/Services/TenantVectorWatcherService.cs` | `ConfigMapName = "simetra-tenantvector"` → `"simetra-tenants"` (line 31); `ConfigKey = "tenantvector.json"` → `"tenants.json"` (line 36) |
| `src/SnmpCollector/Program.cs` | Variable names `tenantVectorConfig`/`tenantVectorPath` (strings only — rename vars too for clarity); string literals `"tenantvector.json"` (lines 33, 112) → `"tenants.json"`; comment `// tenantvector.json uses...` (lines 111, 116); `TryGetProperty("TenantVector", ...)` (line 119) — see SectionName discussion below |
| `src/SnmpCollector/Configuration/TenantVectorOptions.cs` | `SectionName = "TenantVector"` — **see Open Questions** |

#### Local dev config files (physical rename + content)

| File | What changes |
|------|-------------|
| `src/SnmpCollector/config/tenantvector.json` | **Rename file** to `tenants.json`; root JSON key `"TenantVector"` → `"Tenants"` if SectionName changes (see Open Questions) |
| `src/SnmpCollector/bin/Debug/net9.0/config/tenantvector.json` | Build artifact — will be regenerated; no manual change needed (excluded from scope) |
| `tests/SnmpCollector.Tests/bin/Debug/net9.0/config/tenantvector.json` | Build artifact — will be regenerated; no manual change needed (excluded from scope) |

#### K8s manifests

| File | What changes |
|------|-------------|
| `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` | **Rename file** to `simetra-tenants.yaml`; `name: simetra-tenantvector` → `name: simetra-tenants`; key `tenantvector.json:` → `tenants.json:` |
| `deploy/k8s/snmp-collector/deployment.yaml` | `name: simetra-tenantvector` → `name: simetra-tenants` (line 86, volume projection) |
| `deploy/k8s/production/deployment.yaml` | `name: simetra-tenantvector` → `name: simetra-tenants` (line 87, volume projection) |
| `deploy/k8s/production/configmap.yaml` | ConfigMap `name: simetra-tenantvector` → `name: simetra-tenants` (line 415); key `tenantvector.json:` → `tenants.json:` (line 438); comment references on lines 422–435 |

#### E2E test scripts

| File | What changes |
|------|-------------|
| `tests/e2e/lib/kubectl.sh` | `save_configmap "simetra-tenantvector"` → `save_configmap "simetra-tenants"` (line 114); `.original-tenantvector-configmap.yaml` → `.original-tenants-configmap.yaml` (lines 114, 126, 127) |
| `tests/e2e/scenarios/28-tenantvector-routing.sh` | Multiple references to `simetra-tenantvector`, `tenantvector.json`, `.original-tenantvector-configmap.yaml` throughout (lines 11, 13, 15–16, 37–40, 105, 108, 174–181); inline ConfigMap YAML at line 105: `name: simetra-tenantvector` → `name: simetra-tenants`; line 108: `tenantvector.json:` → `tenants.json:` |
| `tests/e2e/fixtures/.original-tenantvector-configmap.yaml` | **Rename file** to `.original-tenants-configmap.yaml` |

#### DEPLOY.md

| File | What changes |
|------|-------------|
| `deploy/k8s/snmp-collector/DEPLOY.md` | No direct reference to `simetra-tenantvector` found; no change needed |

---

### Rename 2: `oidmaps.json` → `oid_metric_map.json` / `simetra-oidmaps` → `simetra-oid-metric-map`

#### C# source files

| File | What changes |
|------|-------------|
| `src/SnmpCollector/Services/OidMapWatcherService.cs` | `ConfigMapName = "simetra-oidmaps"` → `"simetra-oid-metric-map"` (line 29); `ConfigKey = "oidmaps.json"` → `"oid_metric_map.json"` (line 34) |
| `src/SnmpCollector/Program.cs` | `var oidmapsPath = Path.Combine(configDir, "oidmaps.json")` (line 77) → `"oid_metric_map.json"`; comment on line 76 |
| `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` | Comment only: `// In local dev mode, populated after DI build from oidmaps.json.` (line 320) → `oid_metric_map.json` |

#### Local dev config files (physical rename)

| File | What changes |
|------|-------------|
| `src/SnmpCollector/config/oidmaps.json` | **Rename file** to `oid_metric_map.json`; internal comment on line 4 (`// Local development fallback -- same format as ConfigMap key "oidmaps.json"`) → update comment to `"oid_metric_map.json"` |
| `src/SnmpCollector/bin/Debug/net9.0/config/oidmaps.json` | Build artifact — excluded from scope |
| `tests/SnmpCollector.Tests/bin/Debug/net9.0/config/oidmaps.json` | Build artifact — excluded from scope |

#### Unit test files

| File | What changes |
|------|-------------|
| `tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs` | Hard-coded path `"oidmaps.json"` in `GetOidMapsPath()` (line 24) → `"oid_metric_map.json"`; comments on lines 12, 17–18 |

#### K8s manifests

| File | What changes |
|------|-------------|
| `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` | **Rename file** to `simetra-oid-metric-map.yaml`; `name: simetra-oidmaps` → `name: simetra-oid-metric-map` (line 4); key `oidmaps.json:` → `oid_metric_map.json:` (line 7) |
| `deploy/k8s/snmp-collector/deployment.yaml` | `name: simetra-oidmaps` → `name: simetra-oid-metric-map` (line 82, volume projection) |
| `deploy/k8s/production/deployment.yaml` | `name: simetra-oidmaps` → `name: simetra-oid-metric-map` (line 83, volume projection) |
| `deploy/k8s/production/configmap.yaml` | `name: simetra-oidmaps` → `name: simetra-oid-metric-map` (line 115); key `oidmaps.json:` → `oid_metric_map.json:` (line 126); comment references on lines 248, 432 |

#### E2E test scripts and fixtures

| File | What changes |
|------|-------------|
| `tests/e2e/lib/kubectl.sh` | `save_configmap "simetra-oidmaps"` → `save_configmap "simetra-oid-metric-map"` (line 113); `.original-oidmaps-configmap.yaml` → `.original-oid-metric-map-configmap.yaml` (lines 113, 123, 124) |
| `tests/e2e/fixtures/oid-renamed-configmap.yaml` | `name: simetra-oidmaps` → `name: simetra-oid-metric-map` (line 4); key `oidmaps.json:` → `oid_metric_map.json:` (line 7) |
| `tests/e2e/fixtures/oid-removed-configmap.yaml` | Same pattern: ConfigMap name and key |
| `tests/e2e/fixtures/oid-added-configmap.yaml` | Same pattern: ConfigMap name and key |
| `tests/e2e/fixtures/invalid-json-oidmaps-syntax-configmap.yaml` | `name: simetra-oidmaps` → `name: simetra-oid-metric-map`; key rename |
| `tests/e2e/fixtures/invalid-json-oidmaps-schema-configmap.yaml` | Same pattern |
| `tests/e2e/fixtures/.original-oidmaps-configmap.yaml` | **Rename file** to `.original-oid-metric-map-configmap.yaml`; update ConfigMap name inside |
| `tests/e2e/scenarios/26-invalid-json.sh` | References `"invalid-json-oidmaps-syntax-configmap.yaml"` and `"invalid-json-oidmaps-schema-configmap.yaml"` — update fixture filename strings (lines 12–13) |

#### DEPLOY.md

| File | What changes |
|------|-------------|
| `deploy/k8s/snmp-collector/DEPLOY.md` | Line 69: `kubectl apply -f deploy/k8s/snmp-collector/simetra-oidmaps.yaml` → `simetra-oid-metric-map.yaml` |

---

### Rename 3: `commandmaps.json` → `oid_command_map.json` / `simetra-commandmaps` → `simetra-oid-command-map`

#### C# source files

| File | What changes |
|------|-------------|
| `src/SnmpCollector/Services/CommandMapWatcherService.cs` | `ConfigMapName = "simetra-commandmaps"` → `"simetra-oid-command-map"` (line 29); `ConfigKey = "commandmaps.json"` → `"oid_command_map.json"` (line 34) |
| `src/SnmpCollector/Program.cs` | `var commandmapsPath = Path.Combine(configDir, "commandmaps.json")` (line 137) → `"oid_command_map.json"`; comment on line 136 |
| `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` | Comment only: `// In local dev mode, populated after DI build from commandmaps.json.` (line 326) → `oid_command_map.json`; comment on line 362 mentioning `commandmaps` |

#### Local dev config files (physical rename)

| File | What changes |
|------|-------------|
| `src/SnmpCollector/config/commandmaps.json` | **Rename file** to `oid_command_map.json`; internal comments on lines 4–5 (`// Local development fallback -- same format as ConfigMap key "commandmaps.json"` / `// ConfigMap name: simetra-commandmaps`) → update both |
| `src/SnmpCollector/bin/Debug/net9.0/config/commandmaps.json` | Build artifact — excluded from scope |
| `tests/SnmpCollector.Tests/bin/Debug/net9.0/config/commandmaps.json` | Build artifact — excluded from scope |

#### K8s manifests

| File | What changes |
|------|-------------|
| `deploy/k8s/snmp-collector/simetra-commandmaps.yaml` | **Rename file** to `simetra-oid-command-map.yaml`; `name: simetra-commandmaps` → `name: simetra-oid-command-map` (line 4); key `commandmaps.json:` → `oid_command_map.json:` (line 7) |
| `deploy/k8s/production/configmap.yaml` | `name: simetra-commandmaps` → `name: simetra-oid-command-map` (line 474); key `commandmaps.json:` → `oid_command_map.json:` (line 485) |
| `deploy/k8s/snmp-collector/deployment.yaml` | **No change needed** — commandmaps is not in the volume projections (only oidmaps, devices, tenantvector are projected) |
| `deploy/k8s/production/deployment.yaml` | **No change needed** — same, commandmaps absent from projected volumes |

**Note:** The commandmaps ConfigMap is NOT mounted into the pod via the deployment volume projection. `CommandMapWatcherService` fetches it via the K8s API at runtime, not via a mounted volume file. This is intentional — only the three read-at-startup configs are projected. Confirm this design holds (no deployment.yaml change needed for REN-04).

#### E2E test scripts

No E2E scenario scripts reference `simetra-commandmaps` or `commandmaps.json` directly. The `kubectl.sh` `snapshot_configmaps`/`restore_configmaps` functions do NOT snapshot commandmaps. **No E2E script changes needed for Rename 3.**

---

## Open Questions

### 1. Does `TenantVectorOptions.SectionName = "TenantVector"` change to `"Tenants"`?

**REQUIREMENTS.md REN-01 says:** `TenantVectorOptions.SectionName` → `"Tenants"`

This is significant:
- `TenantVectorOptions.SectionName` is `"TenantVector"` today
- `ServiceCollectionExtensions.cs` binds with `.Bind(configuration.GetSection(TenantVectorOptions.SectionName))` — this affects the IConfiguration path
- `Program.cs` line 119: `TryGetProperty("TenantVector", out var tvElement)` — must change to `"Tenants"`
- The local dev `tenantvector.json` root wrapper key `"TenantVector"` → `"Tenants"`
- The K8s tenantvector configmap's IConfiguration-loaded path changes

**However:** The watcher services (`TenantVectorWatcherService`) deserialize the ConfigMap content **directly** (not via IConfiguration), so the section name does NOT affect the K8s ConfigMap JSON content (which has no wrapper key — it's `{ "Tenants": [...] }` already).

**Recommendation:** Change `SectionName` from `"TenantVector"` to `"Tenants"`, update the local dev `tenants.json` wrapper key, and update `Program.cs` line 119. Verify against `ServiceCollectionExtensions.cs` binding.

### 2. What about the `TenantVector` class/type names themselves?

The CONTEXT.md says this is a "mechanical rename" of **file and ConfigMap names only**. Class names like `TenantVectorOptions`, `TenantVectorRegistry`, `TenantVectorWatcherService`, `TenantVectorFanOutBehavior`, etc. are **not** renamed by this phase — they are code names, not config file names. Do not rename C# types.

### 3. Prometheus metric `snmp.tenantvector.routed`

`PipelineMetricService.cs` line 64: `_meter.CreateCounter<long>("snmp.tenantvector.routed")`. This is a Prometheus metric name, not a config file name. CONTEXT.md says this is only file/ConfigMap renames. **Do not change the metric name** in this phase.

---

## Architecture Patterns

### How the watcher services use ConfigMap names (HIGH confidence)

Each watcher service (OidMapWatcherService, TenantVectorWatcherService, CommandMapWatcherService) has two `internal const string` fields:

```csharp
internal const string ConfigMapName = "simetra-oidmaps";  // K8s ConfigMap name
internal const string ConfigKey = "oidmaps.json";          // key within ConfigMap.data
```

These two constants are the **only runtime coupling** between the watcher service and the ConfigMap name. Change both and the watcher will watch the new ConfigMap.

### How local dev loading works (HIGH confidence)

`Program.cs` has three separate local dev loading blocks:

1. **TenantVector** (lines 33, 111–130): Loads `tenantvector.json`, wraps through IConfiguration section `"TenantVector"`, then deserializes as `TenantVectorOptions`
2. **OID map** (lines 76–86): Loads `oidmaps.json`, parses directly as JSON array
3. **Command map** (lines 136–147): Loads `commandmaps.json`, parses directly as JSON array

Each block has a hard-coded filename string that must be updated.

### How K8s ConfigMaps are projected (HIGH confidence)

The deployment.yaml projected volumes mount three ConfigMaps into `/app/config`:
- `snmp-collector-config` (appsettings)
- `simetra-oidmaps` → rename to `simetra-oid-metric-map`
- `simetra-devices` (not renamed)
- `simetra-tenantvector` → rename to `simetra-tenants`

`simetra-commandmaps` is **not projected** — it is watched via K8s API only.

When the ConfigMap is mounted as a projected volume, the key name becomes the filename on disk. So renaming `oidmaps.json` → `oid_metric_map.json` in the ConfigMap data key also renames the file that appears at `/app/config/oid_metric_map.json`, which is what `Program.cs` loads in local dev mode (but in K8s mode the file path is irrelevant — the watcher service reads via API).

---

## Common Pitfalls

### Pitfall 1: Renaming physical files without updating .csproj copy-to-output
**What goes wrong:** `src/SnmpCollector/config/tenantvector.json` is likely set to copy to output directory. After renaming the file, the build still works because the .csproj copies the new filename.
**How to avoid:** Check `SnmpCollector.csproj` for `<Content Include="config/tenantvector.json" ...>` entries. Grep confirmed no references — the config directory copies as a glob. No .csproj change needed.

### Pitfall 2: Forgetting the `ConfigKey` constant alongside `ConfigMapName`
**What goes wrong:** Update `ConfigMapName` but forget `ConfigKey` — watcher connects to right ConfigMap but looks for wrong data key.
**How to avoid:** Always update both constants in a single edit. They are adjacent lines in each watcher service.

### Pitfall 3: E2E fixture files have the ConfigMap name embedded in their YAML content
**What goes wrong:** The fixture files (`oid-renamed-configmap.yaml`, etc.) have `name: simetra-oidmaps` and `oidmaps.json:` key inside them. If not updated, `kubectl apply` of the fixture creates the old ConfigMap name instead of the new one, and the watcher ignores it.
**How to avoid:** Update ALL six oidmaps fixture files.

### Pitfall 4: `kubectl.sh` snapshot filenames reference old names
**What goes wrong:** `snapshot_configmaps` saves to `.original-oidmaps-configmap.yaml`. After rename, `restore_configmaps` looks for `.original-oidmaps-configmap.yaml` which no longer matches the new snapshot file path.
**How to avoid:** Update both the save path and the restore path together in `kubectl.sh`.

### Pitfall 5: `snapshot_configmaps` hardcodes `"simetra-oidmaps"` ConfigMap name
**What goes wrong:** `save_configmap "simetra-oidmaps" ...` tries to fetch a ConfigMap that no longer exists after rename.
**How to avoid:** Update the ConfigMap name in the `save_configmap` call.

### Pitfall 6: `TenantVectorOptions.SectionName` affects IConfiguration binding
**What goes wrong:** Rename SectionName from `"TenantVector"` to `"Tenants"` without updating the local dev `tenants.json` wrapper key and `Program.cs` TryGetProperty call — IConfiguration binding fails at startup.
**How to avoid:** Change SectionName, `tenants.json` JSON key, and `Program.cs` TryGetProperty call atomically.

### Pitfall 7: `simetra-commandmaps` deployment.yaml gap
**What goes wrong:** Assuming commandmaps follows the same pattern as oidmaps/tenantvector in deployment.yaml projections.
**How to avoid:** Confirmed — commandmaps is NOT in deployment.yaml projected volumes. Only the watcher service C# constants and the standalone YAML and production configmap.yaml need updating. No deployment.yaml change for Rename 3.

---

## Files Excluded from Scope (Build Artifacts)

The following files appear in grep results but are build output copies — they are regenerated on build and should NOT be manually edited:

- `src/SnmpCollector/bin/Debug/net9.0/config/tenantvector.json`
- `src/SnmpCollector/bin/Debug/net9.0/config/oidmaps.json`
- `src/SnmpCollector/bin/Debug/net9.0/config/commandmaps.json`
- `tests/SnmpCollector.Tests/bin/Debug/net9.0/config/tenantvector.json`
- `tests/SnmpCollector.Tests/bin/Debug/net9.0/config/oidmaps.json`
- `tests/SnmpCollector.Tests/bin/Debug/net9.0/config/commandmaps.json`

---

## Files NOT Changed (Type/Symbol Names, Not Config Names)

These C# types and methods contain "TenantVector", "OidMap", or "CommandMap" in their identifiers but are **code names, not config file/ConfigMap names** — they are out of scope:

- All `TenantVector*` class names (TenantVectorOptions, TenantVectorRegistry, TenantVectorWatcherService, TenantVectorFanOutBehavior, etc.)
- All `OidMap*` class names (OidMapService, OidMapWatcherService, IOidMapService, etc.)
- All `CommandMap*` class names (CommandMapService, CommandMapWatcherService, ICommandMapService, etc.)
- Prometheus metric `snmp.tenantvector.routed`
- Grafana dashboard PromQL `snmp_tenantvector_routed_total`
- Test class names

---

## Sources

All findings are from direct `Grep` searches of the codebase. Confidence is HIGH — findings are verified by reading file content, not from training data.

---

## Metadata

**Confidence breakdown:**
- Change inventory: HIGH — direct file content inspection
- Architecture patterns: HIGH — read source files
- Pitfalls: HIGH — derived from actual code structure

**Research date:** 2026-03-15
**Valid until:** N/A — this is a point-in-time inventory of a specific codebase state
