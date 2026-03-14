# Domain Pitfalls

**Domain:** SNMP monitoring agent — adding OID map validation, human-name device config, and command map infrastructure to an existing system
**Researched:** 2026-03-13
**Confidence:** HIGH (verified against source code: OidMapService, DeviceRegistry, MetricPollJob, DynamicPollScheduler, TenantVectorRegistry, ServiceCollectionExtensions, DevicesOptionsValidator, E2E test scenarios)

> **Note:** This file supersedes the 2026-03-10 version (priority vector data layer pitfalls). The original pitfalls for v1.5 (routing index desync, slot torn reads, fan-out behavior ordering, per-pod divergence) remain valid as background context but are not repeated here. This document covers pitfalls specific to v1.6 additions: OID map duplicate validation, human-name device config, and command map infrastructure.

---

## Critical Pitfalls

Mistakes that cause rewrites, silent data loss, or broken polling.

---

### Pitfall 1: Validation Rejection Emits Diff Logs Before Rejecting the Map

**What goes wrong:** Duplicate validation is added inside `OidMapService.UpdateMap()` after `BuildFrozenMap()` is called but before the atomic swap. The current code computes the diff (added, removed, changed) between the old and new map before the volatile swap. If validation runs after diff computation and rejects the new map, the diff log entries (`"OidMap added: X -> Y"`) have already been emitted. Operators see phantom "added" log entries for entries that were never applied.

**Why it happens:** The current `UpdateMap()` sequence is: build new map, compute diff, log diff, volatile swap. Inserting a validation rejection point anywhere after "compute diff" produces misleading logs.

**Consequences:** Operators investigating a failed reload see log lines claiming entries were added, but the subsequent reload-complete log is absent. Support investigations become confusing. Automated alerting on "OidMap added" log entries triggers false alarms.

**Prevention:** Validation must be the *first* action in `UpdateMap()`, before `BuildFrozenMap()`, before diff computation, before any logging. The rejection path emits a single structured error log and returns — no diff, no swap, no "added" entries.

**Detection:** Log line "OidMap added: X" appearing within the same reload cycle as an error log that says the reload was skipped. If both appear in the same request, validation was placed after diff computation.

**Phase:** OID map duplicate validation.

---

### Pitfall 2: Duplicate Metric Names in OID Map Silently Collapse in FrozenSet

**What goes wrong:** Two different OIDs map to the same metric name (e.g., `1.3.6.1.4.1.47477.10.21.1.3.4.0` and `1.3.6.1.4.1.47477.999.4.0` both map to `"obp_channel_L1"`). `OidMapService` builds `_metricNames` as a `FrozenSet<string>` from `.Values`. The set silently deduplicates. Downstream code that iterates `_metricNames` or calls `ContainsMetricName()` sees no indication that two different OIDs back the same name.

**Why it happens:** Standard duplicate detection focuses on key uniqueness (OID strings are unique in a JSON object by definition). Value uniqueness is not enforced by JSON or by the existing `FrozenDictionary` build path.

**Consequences:** `TenantVectorRegistry.DeriveIntervalSeconds()` walks device poll groups and resolves each OID via `_oidMapService.Resolve(oid)` to find the interval for a given metric name. If two OIDs share the same metric name in different poll groups, the first match wins — the routing interval may be derived from the wrong poll group. `TenantVectorOptionsValidator` uses `ContainsMetricName()` to validate tenant metric references — it passes for both OIDs, giving no hint of the ambiguity. The routing key `(ip, port, "obp_channel_L1")` is valid but backed by two different OIDs that produce data at different intervals.

**Prevention:** Add a value-uniqueness check during validation: build a reverse `Dictionary<string, string>` (metricName -> firstOid) while iterating incoming entries. If a metric name is encountered a second time with a different OID, fail with a structured error: `"Duplicate metric name 'obp_channel_L1': already assigned to OID '1.3.6.1.4.1.47477.10.21.1.3.4.0', cannot also assign to '1.3.6.1.4.1.47477.999.4.0'"`.

**Detection:** `OidMapService.EntryCount` increases but `ContainsMetricName()` returns true for a metric name that was entered via two different OIDs. Unit test: pass `{"1.2.3.0": "MetricA", "4.5.6.0": "MetricA"}` to validation — expect failure with both OIDs named in the error message.

**Phase:** OID map duplicate validation.

---

### Pitfall 3: Human Names in devices.json MetricPolls.Oids Crash MetricPollJob

**What goes wrong:** When `devices.json` `MetricPolls[].Oids[]` entries change from raw OID strings (`"1.3.6.1.4.1.47477.100.1.1.0"`) to human names (`"npb_cpu_util"`), `MetricPollJob.Execute()` passes them directly to `new ObjectIdentifier(oid)` via SharpSnmpLib. `ObjectIdentifier` throws `FormatException` on a non-dotted-decimal string. The SNMP GET to the device never fires; the job catches the exception as a generic failure, records a consecutive failure, and eventually triggers unreachability for the device.

**Why it happens:** `MetricPollJob` line 80-83: `pollGroup.Oids.Select(oid => new Variable(new ObjectIdentifier(oid)))`. There is no pre-resolution or format check. `MetricPollInfo.Oids` is `IReadOnlyList<string>` — it carries whatever string was in the config.

**Consequences:** Every poll for every device with human-name OIDs fails. `snmp_poll_executed_total` increments but all results are exceptions. `snmp_poll_unreachable_total` increments. Health probes remain green (jobs still exist and stamp liveness). The failure is invisible at the infrastructure level and looks like all affected devices became simultaneously unreachable.

**Prevention:** Choose and implement one resolution strategy before any devices.json format change is deployed. Two options:

- **Strategy A — Resolve at poll time:** Add `ResolveOid(string nameOrOid) -> string?` to `IOidMapService`. `MetricPollJob` calls this before constructing `ObjectIdentifier`. If the input is already a dotted-decimal OID, pass through unchanged. If it is a name, look up the reverse mapping. If not found, skip the OID with a warning.
- **Strategy B — Resolve at schedule time:** `DeviceRegistry.ReloadAsync()` resolves human names to OIDs using `IOidMapService` before storing them in `MetricPollInfo.Oids`. `MetricPollJob` sees only raw OIDs — no change needed. Risk: resolved OIDs in `MetricPollInfo` become stale if the OID map hot-reloads after the device reload (see Pitfall 4).

The strategy decision must be recorded before the human-name migration begins.

**Detection:** `snmp_poll_executed_total` increments for a device but zero `snmp_gauge` data points appear. Pod logs contain `FormatException` from SharpSnmpLib with an OID value that looks like a metric name (contains underscores, starts with letters).

**Phase:** Human-name device config migration. Must be resolved before any devices.json format change is deployed to K8s.

---

### Pitfall 4 (Strategy B only): OID Map Hot-Reload Leaves MetricPollInfo with Stale Resolved OIDs

**What goes wrong:** If Strategy B is chosen for Pitfall 3, human name-to-OID resolution happens at `DeviceRegistry.ReloadAsync()` time. An OID map hot-reload (triggered by `OidMapWatcherService`) changes the name-to-OID mapping without triggering a device registry reload. `MetricPollInfo.Oids` in existing `DeviceInfo` records now contain old OID strings that may resolve to different or Unknown metric names under the new map.

**Why it happens:** `OidMapWatcherService` only calls `_oidMapService.UpdateMap()`. It has no dependency on `DeviceRegistry` or `DynamicPollScheduler`. `DeviceWatcherService` only fires when the devices ConfigMap changes. There is no cross-service coordination on oidmap change.

**Consequences:** After an OID map change, metrics appear under wrong names or as "Unknown" even though nothing in devices.json changed. The `snmp_gauge` time series continues but with incorrect `metric_name` labels. This is silent data corruption at the metric level.

**Prevention:** If Strategy B is chosen, `OidMapWatcherService.HandleConfigMapChangedAsync()` must also trigger `DeviceRegistry.ReloadAsync()` after the oidmap swap so human names are re-resolved. This requires injecting `IDeviceRegistry` into `OidMapWatcherService`. Alternatively, adopt Strategy A to eliminate the cross-dependency entirely. Strategy A is the lower-risk choice.

**Detection:** After an OID map rename, `snmp_gauge{metric_name="Unknown"}` suddenly appears for metrics that previously had resolved names. The affected OIDs still produce data (SNMP GET succeeds) but the labels are wrong.

**Phase:** OID map / device config coordination. Must be explicitly decided before Strategy B is implemented.

---

### Pitfall 5: OID Map Rename Silently Breaks TenantVectorRegistry Routing

**What goes wrong:** `TenantVectorRegistry` builds its routing index keyed by `(ip, port, metricName)`. When the OID map hot-reloads and renames a metric (e.g., `"obp_channel_L1"` becomes `"obp_ch_L1"`), existing `MetricSlotHolder` instances still use the old metric name in their `MetricName` field, and the routing index still contains entries under the old name. `OidResolutionBehavior` immediately uses the new map, so `SnmpOidReceived.MetricName` is now `"obp_ch_L1"`. `TenantVectorFanOutBehavior` calls `TryRoute(ip, port, "obp_ch_L1")` — no match. The tenant slot for `"obp_channel_L1"` never receives new data.

**Why it happens:** `TenantVectorWatcherService` reloads the registry when `tenantvector.json` changes, not when `oidmaps.json` changes. The routing index is initialized with metric names from the tenant vector config, which references names by value strings. There is no subscription or notification from `OidMapService` to `TenantVectorRegistry`.

**Consequences:** Tenant metric slots go dark silently after an OID map rename. No errors in logs — `TenantVectorFanOutBehavior` simply finds no routing key and moves on. The tenant sees stale metric values in dashboards. The effect persists until either `tenantvector.json` is reloaded (which triggers a fresh `Reload()` using the current OID map) or the pod restarts.

**Prevention:** On OID map hot-reload, trigger `TenantVectorRegistry.Reload()` with the current `TenantVectorOptions`. This requires `OidMapWatcherService` to have a dependency on `TenantVectorRegistry` or a publish/subscribe interface (`IOidMapChangedNotification`). An alternative is a centralized `ConfigReloadOrchestrator` that serializes all watcher events and triggers downstream rebuilds in the correct order.

**Detection:** After an OID map rename, `snmp_gauge` data points appear with the new `metric_name` label (pipeline is working), but the tenant vector dashboard shows no updates for the renamed metric. Debug: check `TenantVectorFanOutBehavior` logs for routing misses on the new metric name.

**Phase:** OID map rename coordination phase — any phase that coordinates oidmap reload with downstream registries.

---

### Pitfall 6: Command Map Watcher Registered Only in K8s Path, Local Dev Gets No Command Map

**What goes wrong:** Following the established pattern in `ServiceCollectionExtensions.AddSnmpConfiguration()` (lines 238–248), all ConfigMap watcher services are registered only inside `if (KubernetesClientConfiguration.IsInCluster())`. A new `CommandMapWatcherService` following this pattern without a local-dev fallback means `ICommandMapService` starts empty in local dev and stays empty. Any code that consults the command map silently falls through to a default behavior.

**Why it happens:** The pattern is intentional for K8s-specific watchers, but the local-dev initialization path for `OidMapService` explicitly loads from `oidmaps.json` in `Program.cs` after the host is built. If the command map service lacks this initialization step, local dev operates in a degraded state without any indication.

**Consequences:** In local dev, command map lookups always return the default command (likely SNMP GET v2). The command map feature appears to work but is never actually exercised during development or unit tests. Bugs in command selection remain undetected until K8s E2E.

**Prevention:** Mirror the exact initialization pattern used for `OidMapService` in `Program.cs`:
1. Register `ICommandMapService` as a singleton with an empty initial state (in both branches).
2. In the `else` branch (local dev), after `app.Build()`, load from a local `commandmap.json` file and call the service's update method.
3. In the `if (IsInCluster())` branch, register `CommandMapWatcherService` as a hosted service.

**Detection:** Run locally with a non-empty `commandmap.json` and assert that `ICommandMapService.Lookup()` returns the configured command. If it always returns the default, the local-dev init path is missing. Add this as a local smoke test.

**Phase:** Command map infrastructure.

---

## Moderate Pitfalls

Mistakes that cause delays, test failures, or operational confusion.

---

### Pitfall 7: Duplicate Name Validation Missing from DevicesOptionsValidator

**What goes wrong:** `DevicesOptionsValidator.ValidateNoDuplicates()` checks for duplicate `IP+Port` combinations but does not check for duplicate `Name` values. `DeviceRegistry.ReloadAsync()` builds `_byName` with `byNameBuilder[info.Name] = info` — if two devices share a name, the second silently overwrites the first. `MetricPollJob` and other consumers that call `TryGetDeviceByName()` will find only one of the two devices.

**Why it happens:** Device names were originally used only as human labels and community string derivation keys. The `_byName` index was added as a secondary lookup. The validator was written before `_byName` was used for SNMP GET routing, so name uniqueness was not yet a concern.

**Consequences:** If the human-name device config feature adds device name as a primary identifier (e.g., for command map lookup or display purposes), duplicate names silently lose one device from the registry. No error is logged. The lost device stops being polled without any explicit removal event.

**Prevention:** Add a duplicate name check to `DevicesOptionsValidator.ValidateNoDuplicates()` alongside the existing IP+Port check. Use `StringComparer.OrdinalIgnoreCase` to match the `FrozenDictionary` behavior in `DeviceRegistry`.

**Detection:** Add a unit test: `DevicesOptionsValidator` with two devices sharing the same `Name` should return a failure result. If the test passes without the check, the validation gap is confirmed.

**Phase:** Human-name device config migration.

---

### Pitfall 8: E2E Fixture Files Use Raw OIDs in MetricPolls — Format Change Breaks All Device Fixtures Simultaneously

**What goes wrong:** All E2E test scenarios in `tests/e2e/scenarios/` apply ConfigMap fixtures (YAML files containing `devices.json` content). If devices.json format changes from raw OIDs to human names in `MetricPolls[].Oids[]`, every fixture file containing a device config must be updated simultaneously. Partial fixture updates cause some scenarios to test the new format and others the old format. `restore_configmaps` in E2E scenarios restores to the fixture snapshot — restoring an old-format fixture after a new-format deployment leaves the cluster in an inconsistent state that affects subsequent scenarios.

**Why it happens:** Fixture files are static YAML maintained by hand. There is no fixture generation system. Scenarios 18, 19, 20, 21, 22, 23, 24, 25 all apply fixtures containing `MetricPolls[].Oids[]`. The current `devices.json` has OBP-01 with 26 OIDs across 3 poll groups and NPB-01 with 70 OIDs across 2 poll groups — significant update surface.

**Consequences:** The first E2E run after the format change fails on all fixture-based scenarios, not just the scenarios testing the new feature. The failure mode resembles a test infrastructure problem (cluster not running, ConfigMap not applied correctly) rather than a config format problem, making root cause analysis slow.

**Prevention:** Audit all fixture YAML files under `tests/e2e/scenarios/fixtures/` before implementing the format change. Update all fixtures atomically in the same commit as the format change to devices.json. If possible, add a fixture validation step at the start of the E2E harness that checks device fixture format before running scenarios.

**Detection:** Scenario 21 (device-add) fails with zero polls executed for the new device, but pod logs show `FormatException` from SharpSnmpLib rather than SNMP network errors. Scenario 25 (device-watcher-log) fails because the DeviceWatcher logs an error parsing the fixture rather than a successful reload.

**Phase:** Human-name device config migration. Include a fixture audit task in the phase plan.

---

### Pitfall 9: Quartz Job Key Format Change Causes Delete-All / Add-All Churn on First Reload

**What goes wrong:** `DynamicPollScheduler.ReconcileAsync()` builds job names as `metric-poll-{device.ConfigAddress}_{device.Port}-{pi}`. `device.ConfigAddress` is the raw address from `devices.json` — currently an IP string. If the human-name device config feature changes the job key scheme to use a device name or a different identifier, the reconciler will compute zero intersection between old keys and new keys on the first reload. All old jobs appear as "to remove" and all new jobs as "to add."

**Why it happens:** The reconciler compares string sets. A key scheme rename produces no intersection. This is by design for legitimate add/remove operations, but a bulk rename due to a key format change is unintended churn.

**Consequences:** Every device stops polling for one full interval (old jobs deleted, new jobs scheduled with `StartNow()`). `DeviceUnreachabilityTracker` failure counts reset (liveness vectors removed and re-created). In production this means a gap in Prometheus metrics for all devices simultaneously. Grafana dashboards show all devices going flat at the same moment.

**Prevention:** Keep `ConfigAddress` (the IP string) as the job key component regardless of whether devices.json now stores human names. The `DeviceInfo.ConfigAddress` field remains an IP after DNS resolution, which is stable. Do not switch job keys to human-readable device names. Document this decision in the implementation plan.

**Detection:** After deploying the human-name format change, all `snmp_poll_executed_total` counters reset to zero simultaneously. Log: `"Poll scheduler reconciled: +N added, -N removed"` where N equals the total number of existing poll jobs. If this appears without any devices actually being added or removed, the job key format changed.

**Phase:** Human-name device config migration. Explicitly document job key format as a decision before implementation.

---

### Pitfall 10: OID Map Validation Uses Different Case Sensitivity Than Runtime Lookup

**What goes wrong:** `OidMapService` initializes its internal `FrozenDictionary` with entries from `MergeWithHeartbeatSeed()`, which uses `StringComparer.OrdinalIgnoreCase` for the intermediate merged dictionary but then calls `entries.ToFrozenDictionary()` without an explicit comparer. If validation uses `StringComparer.Ordinal` for duplicate OID key detection but the runtime uses `OrdinalIgnoreCase`, a validator that reports no duplicates may still silently collapse case-variant OID strings at runtime.

**Why it happens:** OID strings are dotted-decimal (e.g., `"1.3.6.1.4.1.47477.100.1.1.0"`) — case is irrelevant for standard OIDs. However, if any OID strings contain hex notation or vendor-specific alphabetic segments, case sensitivity matters. The inconsistency is between the validation pass and the runtime behavior.

**Prevention:** Use `StringComparer.OrdinalIgnoreCase` in the validation pass to match the runtime dictionary semantics. This ensures "duplicate" means the same thing at validation time and at runtime. Document the comparer choice in a code comment.

**Phase:** OID map duplicate validation.

---

### Pitfall 11: Unit Tests for OidMapService Do Not Cover Duplicate Value Rejection

**What goes wrong:** The existing `OidMapServiceTests.cs` tests cover: known OID resolution, unknown OID fallback, empty map, reload-adds, reload-removes. None test duplicate metric name values. If duplicate validation is added but tests are not written alongside it, the feature ships without coverage. A regression in the validation path goes undetected.

**Why it happens:** Tests were written against existing behavior. The new validation is a new code path that requires new tests.

**Prevention:** Write tests in the same commit as the implementation:
- `UpdateMap_WithDuplicateMetricNames_RejectsAndRetainsPreviousMap()` — verifies old map is unchanged after rejection
- `UpdateMap_WithDuplicateMetricNames_LogsErrorWithBothOids()` — verifies the error log names both OIDs
- `Constructor_WithDuplicateMetricNames_Throws()` — if validation also runs at construction time
- `UpdateMap_WithUniqueMappings_Succeeds()` — regression guard confirming valid maps still work

**Detection:** Run mutation testing or manually remove the duplicate-value check from `UpdateMap()` — if all tests still pass, the coverage gap is confirmed.

**Phase:** OID map duplicate validation. Tests written in the same phase as the implementation, not deferred.

---

### Pitfall 12: Command Map Watcher ConfigMap Name Not Established as a Constant

**What goes wrong:** `OidMapWatcherService` uses `internal const string ConfigMapName = "simetra-oidmaps"`. `DeviceWatcherService` uses `internal const string ConfigMapName = "simetra-devices"`. The command map watcher must use a distinct name. If the name is chosen ad-hoc during implementation, the K8s RBAC rules, Helm chart templates, E2E fixture files, and the watcher service constant may use inconsistent names across developers' pull requests.

**Prevention:** Decide the ConfigMap name (e.g., `"simetra-commandmap"`) in the planning phase and record it as a named decision. Derive all other references from a single constant. Consider adding all ConfigMap names to a shared `ConfigMapConstants` class to make the full list visible.

**Phase:** Command map infrastructure. Name must be decided before any watcher, RBAC, or Helm chart work begins.

---

### Pitfall 13: Heartbeat OID Seed Counted in EntryCount — Duplicate Check Must Include the Seed

**What goes wrong:** `OidMapService` always merges the heartbeat OID into the map via `MergeWithHeartbeatSeed()`. `EntryCount` returns the count including the heartbeat entry. If duplicate validation runs against the raw `entries` dictionary (before `MergeWithHeartbeatSeed()`), a user-supplied entry that assigns the metric name `"Heartbeat"` to a non-heartbeat OID will pass validation. After the merge, the seed overwrites the user entry, and the user's intended heartbeat-named metric silently disappears.

**Prevention:** Run duplicate validation against the *merged* dictionary (after `MergeWithHeartbeatSeed()` runs). Document in the validator that `"Heartbeat"` is a reserved metric name. Explicitly reject any user-supplied entry with the metric name `"Heartbeat"` as a validation error, not a silent merge.

**Phase:** OID map duplicate validation.

---

## Phase-Specific Warnings (v1.6)

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| OID map duplicate validation — validation placement | Diff log emits phantom entries on rejection (Pitfall 1) | Validate before `BuildFrozenMap()` and diff computation |
| OID map duplicate validation — value uniqueness | Duplicate metric names collapse silently in FrozenSet (Pitfall 2) | Check value uniqueness separately from key uniqueness |
| OID map duplicate validation — heartbeat seed | Reserved metric name "Heartbeat" not checked (Pitfall 13) | Validate against merged dictionary; reject "Heartbeat" as a user-supplied value |
| OID map duplicate validation — case sensitivity | Comparer mismatch between validation and runtime (Pitfall 10) | Use OrdinalIgnoreCase in both validation and runtime |
| OID map duplicate validation — test coverage | No tests for duplicate rejection (Pitfall 11) | Write tests in the same commit as the implementation |
| Human-name device OID references | MetricPollJob crashes on human names (Pitfall 3) | Decide Strategy A vs B before format change; implement resolution first |
| Human-name + OID map reload coordination | Strategy B leaves stale OIDs after oidmap change (Pitfall 4) | Either adopt Strategy A or add cross-watcher reload trigger in OidMapWatcherService |
| Human-name device config — OID map rename | TenantVectorRegistry routing keys become stale (Pitfall 5) | Trigger TenantVectorRegistry.Reload() on oidmap change |
| Human-name device config — validation | Duplicate device names silently overwrite in registry (Pitfall 7) | Add name uniqueness check to DevicesOptionsValidator |
| Human-name device config — E2E fixtures | Format change breaks all device fixture-based scenarios (Pitfall 8) | Audit and update all fixtures atomically with the format change |
| Human-name device config — Quartz job keys | Key scheme change causes delete-all/add-all churn (Pitfall 9) | Keep IP-based job keys; document as an explicit decision |
| Command map watcher registration | Empty command map in local dev (Pitfall 6) | Add local-dev init path in Program.cs identical to OidMapService pattern |
| Command map infrastructure | ConfigMap name inconsistency across services and templates (Pitfall 12) | Decide name in planning; derive from a shared constant |

---

---

# v1.7 Addendum: Configuration Consistency & Tenant Commands

**Researched:** 2026-03-14
**Confidence:** HIGH — all pitfalls derived from direct source inspection of this repository

The pitfalls below are specific to adding: self-describing tenant entries, CommunityString
validation, Name → CommunityString property rename, tenantvector.json → tenants.json rename,
poll group skip for zero-OID groups, and command entries with Value/ValueType in tenant config.

---

## Critical Pitfalls (v1.7)

---

### v1.7 Pitfall A: ConfigMap Data Key Rename Silently Kills Initial Load

**What goes wrong:**
`TenantVectorWatcherService` has two hard-coded constants at lines 31–36:

```
internal const string ConfigMapName = "simetra-tenantvector";
internal const string ConfigKey = "tenantvector.json";
```

`HandleConfigMapChangedAsync` does:
```csharp
if (!configMap.Data.TryGetValue(ConfigKey, out var jsonContent))
```

If the YAML `data:` key is renamed to `tenants.json` without updating `ConfigKey`, the
`TryGetValue` call returns false. The watcher logs `"ConfigMap does not contain key
tenantvector.json -- skipping reload"` and leaves the registry with zero tenants. Both the
initial load path (`LoadFromConfigMapAsync`) and the ongoing watch loop share this code path.

**Why it happens:** The rename touches three independent artifacts: the C# constant, the
YAML `data:` stanza, and the local dev `config/tenantvector.json` filename. They are not
linked at compile time. Any partial rename is valid at the compiler level but broken at
runtime.

**Consequences:** Pod starts with zero tenants (only heartbeat). `snmp_tenantvector_routed_total`
never increments. E2E scenario 28b (`TenantVectorWatcher initial load detected`) fails because
the expected log message never appears. The failure is indistinguishable from "tenant vector
has no entries" or a routing bug.

**Warning signs:**
- Log: `"ConfigMap simetra-tenantvector does not contain key tenantvector.json"` at pod startup
- `TenantCount = 1` (heartbeat only) after initial load completes
- `snmp_tenantvector_routed_total` delta is zero despite confirmed poll activity

**Prevention:** Rename `ConfigKey`, the YAML `data:` key, and the local `config/` file
atomically in a single commit. Add an assertion to E2E scenario 28b that specifically checks
the new key name appears in pod logs (not just the ConfigMap name).

**Phase address:** The phase introducing the tenantvector.json → tenants.json rename.

---

### v1.7 Pitfall B: Name → CommunityString Rename Silently Produces null via JSON Fallback

**What goes wrong:**
`DeviceWatcherService.HandleConfigMapChangedAsync` deserializes with:
```csharp
PropertyNameCaseInsensitive = true
```
If old ConfigMaps contain `"Name": "Simetra.MyDevice"` in the community string position, and
the C# model property is now `CommunityString`, deserialization produces
`CommunityString = null` with no error. `System.Text.Json` silently ignores unknown JSON
properties.

`DeviceRegistry` then passes `d.CommunityString` (null) to `DeviceInfo`, which is documented
as: "If null or empty, falls back to the `Simetra.{Name}` convention." The device starts
polling using the derived convention string instead of the explicitly configured one.

**Why it happens:** The validator (`DevicesOptionsValidator`) runs only at startup via
IOptions, not in the hot-reload path. It validates `Name` (device name), `IpAddress`, and
`Port` but has no check that catches a null `CommunityString` when one was previously set.
The `CommunityString` field is already named correctly in the current source (`DeviceOptions`
line 31), so this pitfall applies specifically if there are any older format config files or
ConfigMaps still using `"Name"` as the community string property name.

**Consequences:** Any device relying on an explicit community string different from
`Simetra.{DeviceName}` silently falls back to the derived convention. SNMP GETs start
returning authentication failures. These look like device unreachability, not config errors.

**Warning signs:**
- `snmp_poll_errors_total` increases for specific devices after deployment
- Those devices were reachable before the deployment
- Debug: `DeviceInfo.CommunityString` is null for affected devices in log output

**Prevention:** Before the rename ships, grep all config files and K8s ConfigMap YAMLs for
`"Name":` appearing in the community string context. The `device-added-configmap.yaml`
fixture already uses `"CommunityString"` correctly — verify all other fixtures match.

**Phase address:** Any phase touching the DeviceOptions property name.

---

### v1.7 Pitfall C: Self-Describing Tenant Entries — DNS Name in Routing Index, Resolved IP in Incoming Packet

**What goes wrong:**
`TenantVectorRegistry.Reload` calls `ResolveIp(metric.Ip)` which iterates
`_deviceRegistry.AllDevices` to translate a DNS name (e.g.,
`"npb-simulator.simetra.svc.cluster.local"`) to its resolved IPv4 (e.g., `"10.96.1.5"`).
The routing index key is built using the **resolved** IP.

`TenantVectorFanOutBehavior` routes via `msg.AgentIp.ToString()` — the IP from the incoming
SNMP packet. These match.

If self-describing tenant entries remove the DeviceRegistry dependency and `ResolveIp` is
no longer called during `Reload`, the routing index key becomes the raw DNS name string.
The incoming SNMP packet still arrives with the resolved IP. `TryRoute` is called with
`("10.96.1.5", 161, "npb_cpu_util")`, the index contains
`("npb-simulator.simetra.svc.cluster.local", 161, "npb_cpu_util")` — miss every time.

**Why it happens:** Removing `_deviceRegistry` from `TenantVectorRegistry` without replacing
the DNS resolution step. `ResolveIp` and `DeriveIntervalSeconds` both depend on
`_deviceRegistry` — they will be compile errors only if `_deviceRegistry` is removed from
the constructor. If the field is kept but the methods are not called, the routing index uses
unresolved names silently.

**Consequences:** `snmp_tenantvector_routed_total` stays at zero for every DNS-name tenant.
Time-series slots are never written. `carried_over=0` on every reload. Indistinguishable
from misconfigured tenant entries.

**Warning signs:**
- `TenantVectorRegistry reloaded: ... carried_over=0` on consecutive reloads with unchanged config
- `snmp_tenantvector_routed_total` delta is zero despite `snmp_poll_executed_total` incrementing

**Prevention:** When removing or weakening the DeviceRegistry dependency, ensure `Reload`
still resolves DNS names to IPs before building routing index keys. If DeviceRegistry is
removed, replace with direct `Dns.GetHostAddressesAsync` in `Reload`. Write an integration
test: configure a tenant with a DNS name, confirm routing occurs after a poll.

**Phase address:** The phase introducing self-describing tenant entries.

---

### v1.7 Pitfall D: Zero-OID Poll Group Still Schedules a Quartz Job

**What goes wrong:**
`BuildPollGroups` in `DeviceRegistry` creates a `MetricPollInfo` even when all
`MetricNames` fail resolution and `resolvedOids` is empty. The current code (lines 186–192):

```csharp
return new MetricPollInfo(
    PollIndex: index,
    Oids: resolvedOids.AsReadOnly(),   // could be empty list
    IntervalSeconds: poll.IntervalSeconds,
    TimeoutMultiplier: poll.TimeoutMultiplier);
```

`DynamicPollScheduler.ReconcileAsync` then includes this group in `desiredJobs` and
schedules it. The Quartz job fires on interval, attempts to SNMP GET an empty OID list.
The Phase 31 CONTEXT.md decision says "If zero names resolve in a group, no job for that
group" — but this is not yet implemented in the code.

**Why it happens:** The design intent (skip zero-OID groups) was documented in CONTEXT.md
but not yet enforced in `BuildPollGroups`. Any phase that depends on the skip behavior
before it is coded will schedule empty-OID poll jobs.

**Consequences:** The Quartz job fires at interval with an empty OID list. `SharpSnmpClient`
behavior with empty GET OIDs is undefined — it may throw, send a malformed PDU, or return
an error response. Either way, `snmp_poll_errors_total` inflates and the device may log
spurious authentication requests.

**Warning signs:**
- Log: `"Device 'X' poll N: resolved 0/N metric names"` with no subsequent "skipping" message
- `snmp_poll_errors_total` increases for a device after reload where some metrics were
  unresolvable
- A Quartz job exists with zero OIDs in its `MetricPollInfo`

**Prevention:** Implement the skip guard in `BuildPollGroups` before any phase depends on
it. Add a unit test: device with all-unresolvable metric names → `BuildPollGroups` returns
zero groups (or returns groups with non-empty OID lists only).

**Phase address:** Whichever phase implements the zero-OID skip — must be completed before
any downstream phase relies on the behavior.

---

## Moderate Pitfalls (v1.7)

---

### v1.7 Pitfall E: Rolling Deployment Format Divergence — Old Pods Skip Reload

**What goes wrong:**
During a K8s rolling update, old and new pods run simultaneously. The ConfigMap is a single
cluster resource. If the ConfigMap data key is renamed (e.g., `tenantvector.json` →
`tenants.json`) and the ConfigMap is updated while old pods are still running:

- Old pods: watch fires, `TryGetValue("tenantvector.json")` misses, logs "skipping reload,"
  retains previous in-memory state (tolerable but config is now stale)
- New pods: `TryGetValue("tenants.json")` hits, reloads correctly

If the ConfigMap is updated before the old pods are terminated, the old pods retain their
last valid config until they are replaced — not ideal but survivable. If the ConfigMap is
updated after the rollout completes but new pods started before the ConfigMap was present,
new pods start with empty registry and rely on the watch loop to recover.

**Consequences:** During the overlap window, pods in the same deployment have different
active configs. A tenant metric might route correctly on one pod and miss on another.
This is visible in Prometheus as inconsistent `snmp_tenantvector_routed_total` per pod.

**Warning signs:**
- Mix of `"tenantvector.json"` and `"tenants.json"` appears in logs from different pods
  at the same time
- `TenantCount` varies between pods serving the same deployment

**Prevention:** Apply ConfigMap changes before triggering rollout. The watch loop's
initial-load failure path already has "will retry via watch loop" resilience. Standard
procedure: `kubectl apply -f configmaps/ && kubectl rollout restart deployment/snmp-collector`.
Document this order in the deployment runbook.

**Phase address:** Any phase renaming a ConfigMap name or data key.

---

### v1.7 Pitfall F: CommunityString Validation Logic Diverges From Runtime Extraction

**What goes wrong:**
`CommunityStringHelper.TryExtractDeviceName` uses:
```csharp
community.StartsWith("Simetra.", StringComparison.Ordinal)
&& community.Length > CommunityPrefix.Length
```

This is case-sensitive (Ordinal) and requires at least one character after the prefix.
If a load-time validator checks community strings against a different rule — e.g., a
case-insensitive regex `Simetra\..+` or a simpler `StartsWith("Simetra.")` without the
length guard — the validator passes strings that runtime extraction rejects (or vice versa).

Specific edge cases:
- `"Simetra."` (empty suffix): length guard rejects at runtime, but `Simetra\..+` also
  rejects — these agree. However `Simetra\..*` passes and runtime rejects. One character
  matters.
- `"simetra.MyDevice"` (lowercase): runtime rejects (Ordinal), but a case-insensitive
  validator passes. SNMP AUTH fails silently.
- `"Simetra.My Device"` (space in suffix): runtime extracts `"My Device"`, device lookup
  is OrdinalIgnoreCase against device names — space in name likely does not match any
  registered device name.

**Prevention:** Write the load-time validator to call `CommunityStringHelper.TryExtractDeviceName`
directly — not a separate regex. This guarantees validation and runtime share identical logic.
Test: pass `"simetra.device"` (lowercase) to the validator — expect failure.

**Phase address:** The phase adding CommunityString format validation at load time.

---

### v1.7 Pitfall G: Value/ValueType Mismatch for Command Entries Is a Silent Runtime Crash

**What goes wrong:**
Command entries with `Value` (a string) and `ValueType` (an enum like `Integer32`, `OctetString`)
in tenant config need validation that `Value` is parseable as `ValueType`. If the validator
only checks that `Value` is non-empty and `ValueType` is a recognized enum value, a config
like `{ "ValueType": "Integer32", "Value": "not-a-number" }` passes validation. At command
execution time, the conversion attempt throws — likely an `InvalidOperationException` or
`FormatException` in the SNMP PDU builder.

**Why it happens:** Validators typically check structural completeness, not semantic
correctness. Parsing `Value` as `ValueType` is a semantic check that must be explicitly added.

**Consequences:** Command fails at execution time. The error is logged but the invalid
config is not flagged at load time. Operators see intermittent command failures with cryptic
PDU construction errors rather than a clear "invalid value for type" config error.

**Prevention:** At validation time, for each command entry, attempt to parse `Value` as the
declared `ValueType` using the same conversion logic used at execution time. Reject the
config with a structured error: `"Command entry: Value 'not-a-number' cannot be parsed as
Integer32"`. This produces a clear operator error at config load, not a runtime exception.

**Phase address:** The phase adding command entry validation in tenant config.

---

### v1.7 Pitfall H: Carry-Over Key Mismatch Resets All Time-Series Slots on Every Reload

**What goes wrong:**
`TenantVectorRegistry.Reload` builds `oldSlotLookup` keyed on `holder.Ip`. If a previous
reload resolved DNS names to IPs, old keys are resolved IPs. A new reload that resolves the
same DNS names differently (e.g., due to a pod restart where DNS TTL caused a different IP
resolution) produces new keys that miss the old lookup. `carried_over = 0`, all time-series
samples are discarded.

More directly: if the IP used to build routing keys changes (DNS TTL expiry, cluster
reschedule), the carry-over mechanism silently loses all time-series history on every reload.

**Why it happens:** The key used in `oldSlotLookup` (from `holder.Ip`) and the key used for
new holders (from `ResolveIp(metric.Ip)`) must be produced by the same resolution at the
same point in time. If DNS resolution produces different IPs across reloads, keys diverge.

**Warning signs:**
- `TenantVectorRegistry reloaded: ... carried_over=0` consistently on hot-reloads where
  config has not changed
- Debug log: `"TimeSeries holder: ... samples=0"` immediately after reload despite history
  existing before

**Prevention:** Normalize carry-over keys to use the raw config address (before DNS
resolution), not the resolved IP. The routing index still uses resolved IPs for runtime
matching, but carry-over uses the stable config key. Or: design carry-over to use metric
names and the config-level address as the key, not the resolved IP.

**Phase address:** The phase where tenant metric IPs change their resolution strategy.

---

### v1.7 Pitfall I: E2E Scenario 28 Fixtures Reference tenantvector.json by Name — Break After Rename

**What goes wrong:**
E2E scenario 28d (`TenantVector hot-reload detects added tenant`) applies an inline
`kubectl apply` with a hardcoded ConfigMap data key `tenantvector.json` (line 108 of
`28-tenantvector-routing.sh`). After renaming the data key to `tenants.json`, this inline
fixture still uses `tenantvector.json`. The watcher sees the update but reads an empty
data block (wrong key), skips reload, and logs "skipping reload." The scenario then checks
for `"tenants=4"` in logs, which never appears. Scenario 28d fails.

The same scenario also checks `DESCRIBE_OUTPUT` for `"simetra-tenantvector"` (the ConfigMap
name, not the key) — this check is unaffected by the data key rename. So 28a passes, 28b
may pass (initial load from correct key), but 28d fails.

**Warning signs:**
- Scenario 28d fails: `"No pod logged 'reloaded' with 'tenants=4' within 30s window"`
- Pod logs contain `"ConfigMap does not contain key tenantvector.json"` during scenario 28d

**Prevention:** When renaming the data key, search `tests/e2e/scenarios/` for the old key
name as a string, not just in fixture YAML files. Inline heredoc content in `.sh` files is
not caught by grep on `*.yaml` files. Update scenario 28d's inline fixture at the same time
as the YAML manifest.

**Phase address:** The phase renaming tenantvector.json → tenants.json.

---

## Phase-Specific Warnings (v1.7)

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| tenantvector.json → tenants.json rename | ConfigKey constant mismatch kills initial load (Pitfall A) | Atomic rename: constant + YAML + local config + E2E inline fixtures |
| tenantvector.json → tenants.json rename | E2E scenario 28d inline heredoc uses old key (Pitfall I) | Grep .sh files for old key name, not just .yaml files |
| Name → CommunityString property rename | Silent JSON null deserialization (Pitfall B) | Grep all config files for old property name before merge |
| Self-describing tenant entries | DNS name in routing index vs resolved IP in packets (Pitfall C) | Keep DNS resolution in Reload; integration test required |
| Zero-OID poll group skip | Empty-OID job scheduled and fires (Pitfall D) | Implement skip guard before any dependent phase; unit test |
| Rolling deployment | Format divergence between pod generations (Pitfall E) | Apply ConfigMap before `kubectl rollout restart` |
| CommunityString format validation | Regex diverges from runtime TryExtractDeviceName (Pitfall F) | Use TryExtractDeviceName as the validator, not a standalone regex |
| Value/ValueType for command entries | Type mismatch is silent at load time (Pitfall G) | Parse Value as ValueType at validation time |
| Time-series carry-over after IP change | DNS re-resolution produces different keys, zeroes history (Pitfall H) | Use config-level address as carry-over key, not resolved IP |

---

## Sources (v1.7 Addendum)

All findings derived from direct source inspection (HIGH confidence):

- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — ConfigKey constant (line 36), initial load path
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — Reload, ResolveIp, carry-over logic, oldSlotLookup
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — BuildPollGroups (lines 156–194), zero-OID group creation
- `src/SnmpCollector/Services/DynamicPollScheduler.cs` — job scheduling from MetricPollInfo with empty Oids
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — DeviceRegistry lookup for port resolution
- `src/SnmpCollector/Pipeline/CommunityStringHelper.cs` — Ordinal comparison, length guard (line 19–21)
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` — CommunityString field, null = convention fallback
- `src/SnmpCollector/Configuration/DeviceOptions.cs` — CommunityString property (already renamed in current code)
- `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` — startup-only scope, no hot-reload path
- `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` — current no-op validator
- `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` — data key `tenantvector.json` (line 7)
- `deploy/k8s/snmp-collector/simetra-devices.yaml` — DNS names as IpAddress values
- `tests/e2e/scenarios/28-tenantvector-routing.sh` — inline fixture at line 108 using `tenantvector.json`
- `tests/e2e/fixtures/device-added-configmap.yaml` — already uses `CommunityString` correctly
- `.planning/phases/31-human-name-device-config/31-CONTEXT.md` — zero-OID skip design decision
