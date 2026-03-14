# Feature Landscape: v1.7 Configuration Consistency & Tenant Commands

**Domain:** SNMP monitoring agent — tenant config self-description, command data model, community string validation, config rename
**Researched:** 2026-03-14
**Confidence:** HIGH — derived from full codebase analysis of TenantVectorRegistry, DeviceRegistry, CommunityStringHelper, MetricPollJob, TenantVectorWatcherService, DeviceWatcherService, DeviceOptions, MetricSlotOptions, TenantVectorOptions, K8s ConfigMap manifests, and Phase 31 context document.

---

## Context: Baseline State After v1.6

Before specifying what v1.7 builds, the current state must be understood precisely. This prevents restating what already exists.

| Existing Capability | Implementation | Relevant to v1.7? |
|---------------------|----------------|--------------------|
| `devices.json` with human-readable MetricNames | `DeviceOptions.Polls[].MetricNames[]`, resolved to OIDs via `IOidMapService.ResolveToOid` at load | Yes — `Name` field renamed |
| `TenantVectorOptions` with flat `Metrics[]` of `MetricSlotOptions` | `MetricSlotOptions { Ip, Port, MetricName, TimeSeriesSize }` | Yes — becomes self-describing |
| IP resolution via DeviceRegistry | `TenantVectorRegistry.ResolveIp()` iterates `_deviceRegistry.AllDevices` | Yes — removed |
| CommunityString derived internally | `MetricPollJob` calls `CommunityStringHelper.DeriveFromDeviceName(device.Name)` when `device.CommunityString` is null | Yes — must be explicit |
| `DeviceOptions.CommunityString` optional | Nullable field, fallback to `Simetra.{Name}` convention | Yes — becomes required |
| OidMapService + CommandMapService | Volatile FrozenDictionary swap, forward + reverse lookups | Indirect dependency |
| Hot-reload watcher pattern | All ConfigMaps: initial load + watch loop + SemaphoreSlim | Yes — new watcher or rename |
| `TenantVectorOptionsValidator` is no-op | Always returns success | Yes — now needs validation |
| K8s ConfigMap `simetra-tenantvector` key `tenantvector.json` | `TenantVectorWatcherService.ConfigMapName`, `ConfigKey` | Yes — both renamed |

**The gap this milestone fills:**

1. **Tenant config entries are not self-describing.** A `MetricSlotOptions` entry only knows Ip, Port, MetricName, TimeSeriesSize. It carries no CommunityString — the SNMP credential used when that metric was polled is opaque to the tenant.
2. **Tenant config has no command entries.** Tenants can observe metrics (read OIDs) but have no place to declare which SNMP SET commands they want to send. The data model for tenant commands does not exist.
3. **CommunityString is implicitly derived**, breaking the config-as-truth principle. An operator reading `devices.json` cannot know what community string is actually used — it is constructed at runtime by `CommunityStringHelper.DeriveFromDeviceName()`. The same gap exists for new-style tenant metric entries.
4. **Tenant config is still linked to DeviceRegistry for IP resolution**, creating an implicit dependency that makes tenant config harder to reason about independently.
5. **File and section naming is inconsistent.** `tenantvector.json` and `TenantVector` section refer to a concept now known as `tenants`. The `Name` field on `DeviceOptions` is being renamed to `CommunityString`.

---

## Table Stakes

Features that MUST exist for v1.7 to be coherent. Missing any of these leaves the system in a partially-broken or inconsistent state.

---

### TS-01: Tenant Metric Entry — Self-Describing Object

**What:** `MetricSlotOptions` (in `TenantOptions.Metrics[]`) gains five new required fields so each entry is fully self-describing without any external lookup:

```json
{
  "Device": "NPB-01",
  "Ip": "npb-simulator.simetra.svc.cluster.local",
  "Port": 161,
  "CommunityString": "Simetra.NPB-01",
  "MetricName": "npb_cpu_util",
  "TimeSeriesSize": 10
}
```

**Existing fields:** `Ip`, `Port`, `MetricName`, `TimeSeriesSize` — all retained with same semantics.

**New fields:**
- `Device` (string) — human-readable device label. Used as the routing context label in fan-out. Must match a key in `devices.json` (not enforced at load time — see AF-04).
- `CommunityString` (string) — the full, explicit SNMP community string for this entry. Validated at load time (see TS-05).

**Why Expected:** The tenant vector is the authoritative declaration of "what this tenant cares about." Today it only says (ip, port, metric name) — it says nothing about how to SNMP GET that value or which device it belongs to. The current `TenantVectorRegistry` must call into `DeviceRegistry` to infer the interval and the community string. A self-describing entry removes this dependency and makes the config auditable.

**Complexity:** Low — new fields on existing model
**Depends On:** Existing `MetricSlotOptions`, existing `TenantOptions`

**Edge cases:**
- `Device` field absent or empty: validate and log error, skip entry (see TS-06).
- `CommunityString` absent or empty: validate and log error, skip entry (see TS-05, TS-06).
- `Ip` and `CommunityString` belonging to different physical devices: no cross-validation in this milestone (see AF-04). The combination is accepted as-is.
- `TimeSeriesSize` absent: defaults to 1 (existing default, unchanged).
- `Port` absent: defaults to 161 (existing default, unchanged).

---

### TS-02: Tenant Command Entry Data Model

**What:** `TenantOptions` gains a new `Commands[]` array alongside `Metrics[]`. Each command entry is a flat object:

```json
{
  "Device": "OBP-01",
  "Ip": "obp-simulator.simetra.svc.cluster.local",
  "Port": 161,
  "CommunityString": "Simetra.OBP-01",
  "CommandName": "obp_set_bypass_L1",
  "Value": "1",
  "ValueType": "Integer32"
}
```

Fields:
- `Device` (string) — device label for routing context.
- `Ip` (string) — target device address.
- `Port` (int, default 161) — SNMP port.
- `CommunityString` (string) — full community string, validated at load time.
- `CommandName` (string) — human-readable command name, resolved to an OID at execution time via `ICommandMapService.ResolveCommandOid()`.
- `Value` (string) — the value to SET, stored as string for transport. Type interpretation is deferred to execution.
- `ValueType` (string) — SNMP type hint for encoding (e.g., `"Integer32"`, `"OctetString"`, `"IpAddress"`). Not validated against MIB in this milestone.

**Why Expected:** Without a `Commands[]` array, there is nowhere to declare tenant-scoped SNMP SET intent. The data model must exist before any execution logic can be built. This milestone delivers the model; execution is a future milestone (see AF-01).

**Complexity:** Low — new model class and new array property on `TenantOptions`
**Depends On:** Existing `TenantOptions`, `ICommandMapService` (for future resolution, not load-time validation in this milestone)

**Edge cases:**
- `Commands[]` absent from a tenant entry: treated as empty list (backward compatible).
- `Commands[]` is an empty array `[]`: valid, tenant has no commands.
- `CommandName` not in commandmap at load time: do NOT fail. CommandMap is hot-reloaded independently; a command name that doesn't resolve today may resolve after a commandmap reload. Log a debug entry; store the entry as-is.
- `Value` is empty string: permitted. Value validation belongs to command execution, not config load.
- `ValueType` is empty or unrecognized: permitted. Type validation belongs to command execution.
- `CommunityString` empty or invalid: skip this command entry with Error log (see TS-06).

---

### TS-03: devices.json — `Name` Field Renamed to `CommunityString`

**What:** The `Name` field on `DeviceOptions` (and in `devices.json`) is renamed to `CommunityString`. The field now holds the full community string value (e.g., `"Simetra.NPB-01"`) rather than just the device label.

**Config format before:**
```json
{ "Name": "NPB-01", "IpAddress": "...", "Port": 161, ... }
```

**Config format after:**
```json
{ "CommunityString": "Simetra.NPB-01", "IpAddress": "...", "Port": 161, ... }
```

**Why Expected:** The current `Name` field serves as both a human label and the seed for community string construction via `CommunityStringHelper.DeriveFromDeviceName()`. This dual-purpose naming is the source of the implicit derivation problem. Making the field explicitly `CommunityString` aligns the JSON with what the field actually represents, removes the derivation ambiguity, and makes the credential visible in config.

**Complexity:** Low — rename in model and JSON; update all code that reads `device.Name` for community string purposes
**Depends On:** Existing `DeviceOptions`, `DeviceInfo`, `CommunityStringHelper`, `MetricPollJob`

**Impact surface (full rename trace required):**
- `DeviceOptions.Name` → `DeviceOptions.CommunityString` (C# property rename)
- `DeviceInfo.Name` — still exists as a separate identity field (see TS-04)
- `DevicesOptionsValidator` — validation message strings updated
- `DeviceRegistry` — `_byName` dictionary: keyed by what? (see TS-04)
- `MetricPollJob` — currently reads `device.CommunityString ?? CommunityStringHelper.DeriveFromDeviceName(device.Name)`. After rename, fallback logic changes (see TS-05).
- `SnmpTrapListenerService` — extracts device name from community string via `CommunityStringHelper.TryExtractDeviceName()`. This path must remain valid.
- All `devices.json` config files (local dev + K8s ConfigMap).
- Unit tests and integration test fixtures.

**Edge cases:**
- Empty `CommunityString` field in `devices.json`: validation must catch this (previously caught as empty `Name`).
- `CommunityString` that does NOT start with `Simetra.`: valid for the devices.json field (see TS-05 for trap listener impact).
- Duplicate `CommunityString` values across devices: no uniqueness constraint required (community strings per device can theoretically be shared, though unusual).

---

### TS-04: DeviceInfo — Separate `Name` Identity Field

**What:** `DeviceInfo` retains a distinct `Name` property for use as the Prometheus `device_name` label, Quartz job identity component, log context, and registry lookup key. This `Name` is derived from the `CommunityString` field: after extracting the suffix past `Simetra.` using `CommunityStringHelper.TryExtractDeviceName()`.

**Specifically:**
- `devices.json` has `CommunityString: "Simetra.NPB-01"`.
- `DeviceRegistry` extracts `Name = "NPB-01"` from the community string at load time.
- `DeviceInfo.Name` = `"NPB-01"`, `DeviceInfo.CommunityString` = `"Simetra.NPB-01"`.
- `_byName` dictionary in `DeviceRegistry` continues to key on the extracted `Name` = `"NPB-01"`.

**Why Expected:** The rest of the system (Quartz job keys, Prometheus labels, log messages, trap listener device lookup) all identify devices by their short name (e.g., `"NPB-01"`). These consumers must not change. The extraction step is already implemented in `CommunityStringHelper.TryExtractDeviceName()` — it is currently used only in the trap listener. This milestone reuses it in `DeviceRegistry`.

**Complexity:** Low — extraction using existing helper; no logic changes to consumers of `DeviceInfo.Name`
**Depends On:** TS-03 (CommunityString field exists on DeviceOptions), existing `CommunityStringHelper`

**Edge cases:**
- `CommunityString` does not follow `Simetra.{Name}` pattern (e.g., `"public"`, `"private"`, `"custom-community"`): `TryExtractDeviceName` returns false. The device cannot be registered because `Name` is required for identity. This is an invalid-CommunityString condition handled by TS-05.
- `CommunityString` = `"Simetra."` (prefix only, no suffix): `TryExtractDeviceName` returns false (current implementation checks `community.Length > CommunityPrefix.Length`). Same handling as above.
- Two devices with community strings that produce the same extracted name (e.g., `"Simetra.OBP-01"` and `"simetra.OBP-01"` with different casing): `DeviceRegistry._byName` uses `StringComparer.OrdinalIgnoreCase`, so this would be a duplicate. Log error, skip one entry (consistent with existing duplicate detection behavior).

---

### TS-05: CommunityString Validation Rules

**What:** Define explicit validation rules for `CommunityString` fields in both `devices.json` and `tenants.json`. Invalid community strings result in the entry being skipped (not a global reload failure).

**Validation rules (applies to both DeviceOptions and MetricSlotOptions/CommandEntryOptions):**

1. **Not null or whitespace.** A missing or blank `CommunityString` is always invalid.
2. **Must start with `"Simetra."` prefix (case-sensitive, Ordinal).** This matches the existing `CommunityStringHelper.TryExtractDeviceName()` logic and the trap listener's validation.
3. **Must have a non-empty suffix after `"Simetra."`.** `"Simetra."` alone is invalid.
4. **The suffix (extracted device name) must contain only printable, non-whitespace characters.** No tabs, newlines, or control characters. This matches what would be valid in a Prometheus label.

**What is NOT validated:**
- Whether the suffix matches any known device name in `DeviceRegistry`. Cross-registry validation is an anti-feature (see AF-04).
- Whether the community string would be accepted by the target SNMP device. That is a runtime concern.
- Character encoding beyond ASCII printability. SNMP community strings are ASCII.

**Why Expected:** The `CommunityString` is the SNMP credential. A garbage value (empty, whitespace-only, missing prefix) would produce SNMP AUTH failures at runtime with no clear root cause visible in config. Explicit validation at load time surfaces the problem before any polling starts.

**Complexity:** Low — static string validation using existing `CommunityStringHelper` plus null/empty check
**Depends On:** TS-03 (field exists), existing `CommunityStringHelper`

**Edge cases — valid CommunityStrings:**
- `"Simetra.NPB-01"` — valid.
- `"Simetra.OBP-01"` — valid.
- `"Simetra.device-with-hyphens"` — valid.
- `"Simetra.DEVICE_WITH_UNDERSCORES"` — valid (non-alpha suffix is fine as long as printable/non-whitespace).

**Edge cases — invalid CommunityStrings:**
- `""` (empty) — invalid: null/empty check.
- `"   "` (whitespace) — invalid: null/whitespace check.
- `"Simetra."` — invalid: suffix is empty.
- `"public"` — invalid: missing `Simetra.` prefix.
- `"simetra.NPB-01"` (lowercase prefix) — invalid: the check is case-sensitive Ordinal to match `CommunityStringHelper` behavior. Log clear message: "CommunityString 'simetra.NPB-01' must start with 'Simetra.' (case-sensitive)".
- `"Simetra. NPB-01"` (space after dot) — valid prefix, suffix is `" NPB-01"`. Suffix contains a leading space — invalid by the printable non-whitespace rule.
- `null` (JSON field absent or explicitly null) — invalid.

---

### TS-06: Invalid CommunityString — Ignore Entry with Structured Log

**What:** When a `CommunityString` fails validation (TS-05), the behavior is:
- **For `devices.json` entry:** Skip that device entirely. Log at Error level with structured fields: device index, the invalid value, which rule failed. The device is NOT registered in `DeviceRegistry` — it will not receive SNMP GET jobs or trap routing.
- **For `tenants.json` metric entry:** Skip that metric slot entirely. Log at Error level with the tenant index, metric index, the invalid value. The slot is not added to `TenantVectorRegistry`.
- **For `tenants.json` command entry:** Skip that command entry entirely. Log at Error level with tenant index, command index, the invalid value.
- **Global behavior:** Other entries in the same file are unaffected. The reload continues. Only the specific invalid entry is dropped.

**Why Expected:** The existing pattern in the system is "soft degradation with structured logging." `DeviceRegistry.BuildPollGroups()` already does this for unresolvable metric names — warn, skip, continue. This milestone applies the same pattern to invalid CommunityStrings. A hard failure (reject entire reload) is disproportionate: a single misconfigured entry would block all other devices/tenants from updating.

**Complexity:** Low
**Depends On:** TS-05 (validation rules), existing structured log patterns

**Edge cases:**
- All entries in a tenant's `Metrics[]` have invalid CommunityStrings: the tenant is created with zero metric slots (same as a tenant with an empty Metrics list). The tenant entry still exists in `TenantVectorRegistry` but has no routing slots.
- All entries in `devices.json` have invalid CommunityStrings: `DeviceRegistry` is empty. All poll jobs are removed by `DynamicPollScheduler.ReconcileAsync()`. The system runs with no active polling — this is expected and correct; the operator sees Errors in logs.
- Same entry is invalid on every reload: the Error log fires on every reload, not just the first. This is intentional — the operator needs ongoing visibility that the entry is broken.

---

### TS-07: Remove DeviceRegistry Dependency from TenantVectorRegistry

**What:** `TenantVectorRegistry.Reload()` currently calls `_deviceRegistry.AllDevices` (for IP resolution) and `_deviceRegistry.TryGetByIpPort()` + `_oidMapService.Resolve()` (for interval derivation). Both usages are removed.

**IP resolution:** Removed. `MetricSlotOptions.Ip` is used directly as-is in the `MetricSlotHolder`. The tenant config now carries the explicit IP; no translation from DeviceRegistry is needed.

**Interval derivation:** `DeriveIntervalSeconds()` currently finds the interval by looking up the device in DeviceRegistry and scanning its poll groups for a matching OID. This cross-registry derivation is removed. Interval for a tenant slot either:
- Is taken from `MetricSlotOptions.IntervalSeconds` if the field is added (see D-02), OR
- Falls back to 0 (current fallback when device not found) — which is acceptable for this milestone since the interval is informational in the holder, not used for scheduling.

**Constructor impact:** `TenantVectorRegistry` no longer requires `IDeviceRegistry` or `IOidMapService` as constructor arguments.

**Why Expected:** The self-describing entry (TS-01) was motivated by removing this cross-registry dependency. After TS-01, the TenantVectorRegistry has all the information it needs in the config itself. The DeviceRegistry dependency is vestigial and creates tight coupling.

**Complexity:** Low (deletion of code), Medium (verifying no regressions in routing key construction or carry-over logic)
**Depends On:** TS-01 (self-describing entry provides the data that was previously looked up)

**Edge cases:**
- Interval derivation currently returns 0 when device not found. The new behavior also returns 0 (or whatever fallback is chosen). No behavioral regression for the slot holder since 0 is already the existing fallback.
- `ResolveIp()` currently passes through the original `configIp` when the device is not found in registry (line 206: `return configIp;`). The new behavior uses `configIp` directly, which is identical to the fallback path — no change in the common case.

---

### TS-08: tenants.json and simetra-tenants ConfigMap Rename

**What:** Three simultaneous renames that must be applied together:

| Old | New |
|-----|-----|
| File: `config/tenantvector.json` | `config/tenants.json` |
| ConfigMap: `simetra-tenantvector` | `simetra-tenants` |
| ConfigMap key: `tenantvector.json` | `tenants.json` |
| C# constant `TenantVectorWatcherService.ConfigMapName` | `"simetra-tenants"` |
| C# constant `TenantVectorWatcherService.ConfigKey` | `"tenants.json"` |
| `TenantVectorOptions.SectionName` = `"TenantVector"` | `"Tenants"` |

**The JSON structure inside the file is unchanged.** Only the file name, ConfigMap name, ConfigMap key, and config section name change.

**Why Expected:** `tenantvector.json` is an internal implementation name that leaked into operator-facing config. The concept is simply "tenants." The rename aligns the file name with the domain concept. Consistency: `devices.json` is called `devices.json`, not `deviceregistry.json`.

**Complexity:** Low (mechanical rename), Medium (must update all references atomically — a partial rename leaves the system broken)
**Depends On:** Nothing — purely mechanical

**Rename impact — full surface:**
- `src/SnmpCollector/config/tenantvector.json` → `config/tenants.json`
- `appsettings.Development.json` — file path reference
- `TenantVectorWatcherService.ConfigMapName` constant
- `TenantVectorWatcherService.ConfigKey` constant
- `TenantVectorOptions.SectionName`
- K8s ConfigMap: `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` → `simetra-tenants.yaml`, metadata name, data key
- All deployment manifests that reference the ConfigMap name (volumes, volumeMounts in `deployment.yaml`)
- E2E test scripts that reference the ConfigMap name (`tests/e2e/scenarios/`)
- Unit/integration test fixtures that use `ConfigMapName` or `ConfigKey` constants

---

### TS-09: Unresolvable Metric Name in Tenant Config — Skip Entry, Log Error

**What:** For each `MetricSlotOptions` entry in `TenantOptions.Metrics[]`, validate that `MetricName` exists in the current OID map (`IOidMapService.ContainsMetricName()`). If the metric name is NOT in the OID map, the entry is skipped with an Error-level structured log.

**Behavior:**
- Entry is not added to `TenantVectorRegistry`.
- Other entries in the same tenant's `Metrics[]` are unaffected.
- The OID map is hot-reloaded independently; this validation runs only at tenant vector reload time.

**Why Expected (v1.7 scope clarification):** Phase 31 context document (in `<deferred>`) explicitly deferred "Tenant vector config validation against OID map — separate concern, not in scope for device config phase." v1.7 is the milestone that makes this validation real, because tenant metric entries are now self-describing and thus validatable.

**Poll group behavior when ALL metrics fail to resolve:** If ALL entries in `TenantOptions.Metrics[]` fail (invalid CommunityStrings, unresolvable metric names), the tenant is created with zero slots. The tenant still exists in `TenantVectorRegistry` but has no routing slots and will never receive fan-out data. This is a valid (degraded) state.

**Complexity:** Low — single `ContainsMetricName` call per entry during `Reload()`
**Depends On:** Existing `IOidMapService.ContainsMetricName()`, TS-01 (self-describing entry has a MetricName), existing `TenantVectorRegistry.Reload()`

**Edge cases:**
- Metric name exists in OID map but the device does not actually poll it: the slot exists in `TenantVectorRegistry` but will never receive a value. This is not a validation error — the tenant config may be forward-looking or the device config may lag.
- Metric name was valid at load time but OID map is updated and the name is removed: the tenant slot persists (no retroactive invalidation on OID map change). The slot will stop receiving values until the tenant config is reloaded.
- MetricName is empty string: fails validation (empty string is not a valid metric name). Log Error, skip.

---

### TS-10: Unresolvable Command Name in Tenant Config — Store Entry, Log Debug

**What:** For `CommandEntryOptions` entries in `TenantOptions.Commands[]`, the `CommandName` is NOT validated against the CommandMap at load time. The entry is stored as-is regardless of whether the `CommandName` is in the current command map.

**Why different from TS-09:** `CommandName` validation against the CommandMap is deliberately deferred to execution time. The CommandMap is hot-reloaded independently. Unlike metric slots (which need an OID map entry to be useful immediately), command entries are only executed on demand — a command that references a name not yet in the CommandMap is not broken, it's just not yet executable. Logging a persistent Error for every such entry would produce noise on every tenant reload when the CommandMap hasn't loaded yet.

**Behavior:** Log at Debug level: "CommandName 'X' not found in CommandMap — entry stored, will resolve at execution time."

**Complexity:** Low (no action, just a debug log)
**Depends On:** TS-02 (command entry model), existing `ICommandMapService`

**Edge cases:**
- CommandName is empty string: this IS validated — empty CommandName is an error (the command entry has no identity). Log Error, skip entry (unlike non-empty names that simply don't resolve).
- CommandName is valid but CommandMap is empty (not yet loaded): Debug log, entry stored. This is the normal startup sequence edge case.

---

## Differentiators

Features that add operational visibility and correctness without being required for basic function.

---

### D-01: TenantVectorOptionsValidator — Structural Validation

**What:** The current `TenantVectorOptionsValidator` is a no-op (always returns Success). Replace it with real structural validation that catches the following at reload time (before calling `TenantVectorRegistry.Reload()`):

- Tenant `Priority` is any integer: valid (no constraint, existing behavior).
- `Metrics[]` entries: each must have non-null/non-empty `Ip`, `MetricName`. `Port` must be 1–65535. `TimeSeriesSize` must be >= 1.
- `Commands[]` entries: each must have non-null/non-empty `Ip`, `CommandName`. `Port` must be 1–65535. Non-empty `Value`.

**Important distinction:** This validator does NOT check CommunityString validity (that happens in TS-05 at the per-entry level during Reload, not as a pre-flight blocker). This validator catches structural issues like missing required fields.

**Behavior:** Validation failures at this level reject the entire reload and log the failures (consistent with existing `DevicesOptionsValidator` behavior). This is appropriate for structural problems (malformed JSON object, missing Ip) but NOT for semantic issues like invalid CommunityString or unresolvable MetricName.

**Why:** Structural failures indicate a badly malformed config where partial application would produce unpredictable behavior. Semantic failures (bad CommunityString, unknown MetricName) are per-entry and should not block other entries.

**Complexity:** Low — follows existing `DevicesOptionsValidator` pattern
**Depends On:** TS-01, TS-02 (the models these fields appear on)

---

### D-02: MetricSlotOptions — Optional `IntervalSeconds` for Informational Storage

**What:** Add an optional `IntervalSeconds` field to `MetricSlotOptions`. When present, it is stored in `MetricSlotHolder` for observability (e.g., the operations dashboard can show polling frequency per tenant slot). It is NOT used by the Quartz scheduler — only the device's poll group interval governs actual polling cadence.

**Value Proposition:** Removes the awkward `DeriveIntervalSeconds()` cross-registry lookup (which goes away in TS-07 anyway) and replaces it with an operator-declared interval. The operations dashboard (Phase 18) and any future observability layer can read the interval directly from the slot without traversing the device registry.

**Behavior:** If absent (field not present in JSON), defaults to 0 (same as the current DeviceRegistry-not-found fallback). No validation required — 0 is valid as "interval unknown."

**Complexity:** Low
**Depends On:** TS-01 (self-describing entry), TS-07 (DeviceRegistry dependency removed)

---

### D-03: Structured Log Fields on CommunityString Skip Events

**What:** When an entry is skipped due to invalid CommunityString (TS-06), the log entry must include structured properties suitable for Loki alerting:

```
"Skipping device[{Index}]: CommunityString '{Value}' is invalid — {Reason}. Device will not be registered."
```

Structured properties:
- `EntryType`: `"Device"`, `"TenantMetric"`, or `"TenantCommand"`
- `EntryIndex`: zero-based index within the parent array
- `InvalidValue`: the actual CommunityString value that failed
- `ValidationRule`: which rule failed (e.g., `"MissingSimetraPrefix"`, `"EmptySuffix"`, `"NullOrEmpty"`)
- `ConfigMap`: which ConfigMap the entry came from

**Value Proposition:** Enables Loki alert: "alert if any CommunityString skip event occurs" — catches operator config errors proactively.

**Complexity:** Low
**Depends On:** TS-06 (the skip behavior), TS-05 (the rule being violated)

---

### D-04: tenants.json — `Name` Field on Tenant Object for Readability

**What:** Add an optional `Name` field to `TenantOptions` (the outer tenant object, not the inner metric/command entries). Used for log context only — log messages include the tenant name rather than `"tenant-3"`.

```json
{
  "Name": "primary-tenant",
  "Priority": 1,
  "Metrics": [...]
}
```

**Behavior:** If absent, fall back to the existing `tenant-{index}` synthetic ID. No uniqueness validation required.

**Value Proposition:** Operators managing many tenants can assign meaningful names that appear in logs, making config errors easier to trace.

**Complexity:** Low
**Depends On:** Existing `TenantOptions`, `TenantVectorRegistry.Reload()` (which currently generates `tenant-{i}`)

---

## Anti-Features

Things to deliberately NOT build in v1.7.

---

### AF-01: SNMP SET Command Execution

**What:** Do NOT implement any mechanism that sends SNMP SET packets to devices using the `Commands[]` entries.
**Why Avoid:** v1.7 defines the data model. Execution requires authorization design, retry semantics, audit logging, value encoding by type, and error reporting — all separate milestone concerns. Building execution now mixes data model definition with protocol implementation, making the data model harder to iterate on.
**What to Do Instead:** `Commands[]` is stored in `TenantOptions` and survives reload. The entries are accessible via `TenantVectorRegistry` for future consumers.

---

### AF-02: Cross-Validation — CommunityString vs DeviceRegistry

**What:** Do NOT validate that `CommunityString` in `tenants.json` entries matches any entry in `devices.json`.
**Why Avoid:** These are two independently hot-reloaded ConfigMaps. Cross-validation would require ordering guarantees between reloads, circular dependencies between watchers, or a two-phase load sequence — all of which add complexity without meaningful benefit. The operator is responsible for consistency. A CommunityString mismatch will manifest as SNMP AUTH failure at runtime.
**What to Do Instead:** TS-05 validates the CommunityString format. Runtime SNMP errors surface mismatches during polling.

---

### AF-03: Reverse CommunityString Lookup

**What:** Do NOT add a `TryGetByCommunityString()` method to `DeviceRegistry` or a CommunityString-indexed lookup.
**Why Avoid:** The trap listener already extracts device name from the community string via `CommunityStringHelper.TryExtractDeviceName()` and then looks up by name. This works without a reverse index. Adding one would be unused infrastructure.
**What to Do Instead:** The existing `TryGetDeviceByName()` lookup after extracting the name suffix is sufficient.

---

### AF-04: Validate That Tenant Entry's `Device` Field Matches DeviceRegistry

**What:** Do NOT require or validate that `MetricSlotOptions.Device` or `CommandEntryOptions.Device` corresponds to a device registered in `DeviceRegistry`.
**Why Avoid:** The `Device` field is a label for routing context and observability — it is not a foreign key. Cross-validating it against `DeviceRegistry` recreates exactly the coupling that TS-07 removes.
**What to Do Instead:** The `Device` field is stored as-is. Mismatches show up in metric labels or log context, not in config errors.

---

### AF-05: Preserve Backward Compat for `tenantvector.json` / `simetra-tenantvector`

**What:** Do NOT provide a transition period where both the old ConfigMap name `simetra-tenantvector` and the new `simetra-tenants` are watched.
**Why Avoid:** Supporting both names doubles the watcher complexity and creates ambiguity about which is authoritative. The rename is a clean break — all references are updated atomically as part of TS-08.
**What to Do Instead:** TS-08 specifies a complete atomic rename of all references. Document the rename in the deployment note so operators know to apply the ConfigMap change and deployment config change together.

---

### AF-06: CommandName Validation Against CommandMap at Tenant Load Time

**What:** Do NOT fail or skip a command entry because its `CommandName` is not in the current CommandMap.
**Why Avoid:** The CommandMap is hot-reloaded independently. At tenant config load time, the CommandMap may not be populated yet (startup race), or it may be temporarily stale. Rejecting command entries that don't resolve now would mean the tenant config must always be reloaded after a CommandMap reload — creating an ordering dependency.
**What to Do Instead:** TS-10 specifies Debug-level logging. Validation at execution time (not load time) is the correct behavior.

---

### AF-07: SNMP Community String Negotiation or Discovery

**What:** Do NOT implement any mechanism to discover or auto-configure community strings by probing the device.
**Why Avoid:** SNMP community strings are static credentials configured by device operators. Auto-discovery would require sending SNMP requests with guessed community strings — a security violation in many environments.
**What to Do Instead:** Require explicit `CommunityString` in config (TS-03, TS-05).

---

## Feature Dependencies

```
TS-03 (Name → CommunityString rename on DeviceOptions)
    |
    +--> TS-04 (DeviceInfo.Name derived from CommunityString)
              |
              +--> TS-05 (CommunityString validation rules)
                        |
                        +--> TS-06 (Skip + Error log on invalid CommunityString)
                        |
                        +--> D-03 (Structured log fields on skip events)

TS-01 (Self-describing MetricSlotOptions)
    |
    +--> TS-05 (CommunityString validation — applies to MetricSlotOptions too)
    |
    +--> TS-07 (Remove DeviceRegistry dependency from TenantVectorRegistry)
    |
    +--> TS-09 (Unresolvable MetricName — skip + Error)
    |
    +--> D-01 (TenantVectorOptionsValidator structural validation)
    |
    +--> D-02 (Optional IntervalSeconds on MetricSlotOptions)

TS-02 (Tenant Commands[] data model)
    |
    +--> TS-05 (CommunityString validation on command entries)
    |
    +--> TS-10 (Unresolvable CommandName — store + Debug)
    |
    +--> D-01 (TenantVectorOptionsValidator — structural validation of Commands[])

TS-08 (tenants.json / simetra-tenants rename)
    | (independent, but must be atomic with TS-01/TS-02 deployment)
```

### Critical Path

```
TS-03 + TS-04 → TS-05 → TS-06         (CommunityString validation infrastructure)
TS-01 → TS-07 + TS-09                  (self-describing metrics, remove DeviceRegistry dep)
TS-02 → TS-10                          (commands data model)
TS-08                                  (rename — independent, apply atomically with deployment)
```

TS-08 (rename) is mechanically independent of TS-01/TS-02/TS-03 but must be deployed atomically. The ConfigMap rename and code rename must reach production together — a partial state where the code watches `simetra-tenants` but only `simetra-tenantvector` exists in K8s (or vice versa) means the watcher finds no data.

---

## MVP Recommendation

**Must build (10 features — all table stakes):**

1. **TS-03** `Name` → `CommunityString` rename on `DeviceOptions` and `devices.json`
2. **TS-04** `DeviceInfo.Name` derived from `CommunityString` via `TryExtractDeviceName`
3. **TS-05** CommunityString validation rules
4. **TS-06** Invalid CommunityString — skip entry, Error log
5. **TS-01** Tenant metric entry — self-describing object (`Device`, `CommunityString` added)
6. **TS-07** Remove DeviceRegistry dependency from TenantVectorRegistry
7. **TS-02** Tenant Commands[] data model
8. **TS-08** `tenants.json` / `simetra-tenants` rename (atomic with deployment)
9. **TS-09** Unresolvable MetricName in tenant config — skip entry, Error log
10. **TS-10** Unresolvable CommandName in tenant config — store entry, Debug log

**Should build (2 differentiators — low cost, high operational value):**

1. **D-01** `TenantVectorOptionsValidator` structural validation (replaces the no-op)
2. **D-03** Structured log fields on CommunityString skip events (enables Loki alerts)

**Evaluate before committing (2 differentiators — low complexity but scope creep risk):**

- **D-02** Optional `IntervalSeconds` on `MetricSlotOptions` — useful for operations dashboard but not needed for correctness in this milestone
- **D-04** Optional `Name` on `TenantOptions` — cosmetic improvement, deferred if schedule is tight

**Explicitly do NOT build (7 anti-features):**

- AF-01: SNMP SET command execution
- AF-02: Cross-validation CommunityString vs DeviceRegistry
- AF-03: Reverse CommunityString lookup in DeviceRegistry
- AF-04: Validate `Device` field matches DeviceRegistry
- AF-05: Dual ConfigMap watch for backward compat during rename
- AF-06: CommandName validation against CommandMap at tenant load time
- AF-07: Community string auto-discovery

---

## Sources

- Codebase: `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `ResolveIp()` and `DeriveIntervalSeconds()` cross-registry calls, volatile FrozenDictionary swap, carry-over logic (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — `BuildPollGroups()` unresolvable name handling pattern, `_byName` / `_byIpPort` indexes, `DeviceInfo` constructor (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/CommunityStringHelper.cs` — `TryExtractDeviceName()` exact implementation: `Simetra.` prefix, `Length > CommunityPrefix.Length` check (HIGH confidence)
- Codebase: `src/SnmpCollector/Jobs/MetricPollJob.cs` — community string selection logic line 86–88 (HIGH confidence)
- Codebase: `src/SnmpCollector/Services/SnmpTrapListenerService.cs` — trap community string validation, `TryExtractDeviceName` usage, drop-with-debug behavior (HIGH confidence)
- Codebase: `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — ConfigMap name `simetra-tenantvector`, key `tenantvector.json`, no-op validator call (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/DeviceOptions.cs` — `Name` and nullable `CommunityString?` fields (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — current fields: Ip, Port, MetricName, TimeSeriesSize (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/TenantOptions.cs` — current shape: Priority + List<MetricSlotOptions> (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` — confirmed no-op (HIGH confidence)
- Codebase: `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` — current ConfigMap name and key (HIGH confidence)
- Codebase: `deploy/k8s/snmp-collector/simetra-devices.yaml` — confirmed `Name` field in current device entries (HIGH confidence)
- Codebase: `src/SnmpCollector/config/tenantvector.json` — local dev format: `TenantVector.Tenants[]` section (HIGH confidence)
- Phase context: `.planning/phases/31-human-name-device-config/31-CONTEXT.md` — confirmed deferred item: "Tenant vector config validation against OID map" (HIGH confidence)

---

*Feature research for: v1.7 Configuration Consistency & Tenant Commands*
*Researched: 2026-03-14*
