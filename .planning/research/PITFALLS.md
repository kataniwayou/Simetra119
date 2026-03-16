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

---

---

# Combined Metrics Addendum: Synthetic/Computed Metric Pitfalls

**Researched:** 2026-03-15
**Confidence:** HIGH — all pitfalls derived from direct source inspection of this repository
**Scope:** Pitfalls specific to adding computed/aggregate metrics (referred to as "combined metrics"
throughout) that are synthesized inside `MetricPollJob` from raw varbind values rather than being
dispatched directly from the SNMP wire.

**What "combined metrics" means in this codebase:** A combined metric is one whose value is computed
from two or more raw SNMP varbind values collected in the same poll group (e.g., a delta, ratio, or
sum). It is dispatched as a synthetic `SnmpOidReceived` after the raw varbinds have been processed,
using a configured `CombinedMetricName` and an `Action` (e.g., Diff, Sum) rather than a real OID.

---

## Critical Pitfalls (Combined Metrics)

---

### CM Pitfall 1: CombinedMetricName That Collides With a Real OID Map Entry

**Severity:** CRITICAL

**What goes wrong:**
The config for a combined metric specifies `CombinedMetricName = "npb_rx_bytes"`. The OID map
already has an entry `1.3.6.1.4.1.47477.100.4.0 -> "npb_rx_bytes"`. Both the synthetic dispatch
and the real OID resolution resolve to the same metric name. Two separate `snmp_gauge` time series
with `metric_name="npb_rx_bytes"` are recorded: one from the real OID (raw counter value), one
from the combined computation (e.g., a per-interval delta). Prometheus scrapes both. Grafana queries
return a sum of the two series, which is neither the raw value nor the delta — it is a nonsensical
blend.

**Why it happens:**
`OidResolutionBehavior` assigns `msg.MetricName` by calling `_oidMapService.Resolve(msg.Oid)`. The
synthetic dispatch for a combined metric would need to bypass OID resolution (it has no real OID, or
carries a sentinel OID). If `CombinedMetricName` is not checked against the OID map at config load
time, the collision is invisible until Prometheus aggregates the duplicates.

**Consequences:**
Silent metric contamination. The raw time series and the computed time series cannot be disentangled
after the fact in Prometheus. Alerts that fire on `rate(snmp_gauge{metric_name="npb_rx_bytes"})` will
produce incorrect thresholds because the denominator includes both series. Clearing the contamination
requires flushing the Prometheus TSDB for that metric name.

**Warning signs:**
- `snmp_gauge{metric_name="npb_rx_bytes"}` shows two time series in Prometheus (different `oid` labels
  if the combined dispatch uses an empty or sentinel OID, or the same label if the OID is copied from
  one of the source varbinds)
- Grafana rate calculations appear twice as large as expected for the affected metric
- `snmp.event.published` counter increments more than `len(varbinds)` per poll cycle for the device

**Prevention:**
At config load time (in `DeviceWatcherService` or a validator), for each poll group entry that
specifies a `CombinedMetricName`, call `_oidMapService.ContainsMetricName(combinedMetricName)`.
If the name exists in the OID map, reject the config entry with an error log:
`"CombinedMetricName 'npb_rx_bytes' collides with a real OID map entry — combined metric names must be distinct from polled metric names"`.
This check must also run after an OID map hot-reload if the combined metric config is still active.

**Phase address:** The phase introducing combined metric config loading and validation.

---

### CM Pitfall 2: Partial Config — CombinedMetricName Without Action (or Action Without Name)

**Severity:** CRITICAL

**What goes wrong:**
A poll group entry has `CombinedMetricName = "npb_byte_rate"` but no `Action` field (or
`Action = null`). The implementation code in `MetricPollJob.DispatchResponseAsync` (or a new
`ComputeCombinedMetricsAsync` method) reads both fields and proceeds if they are both set.
If only one is set, the code either: (a) throws a `NullReferenceException` when it tries to
call `action.Compute(values)`, or (b) silently skips the combined dispatch with no log entry,
depending on how the null check is written.

The symmetric case — `Action = Diff` but no `CombinedMetricName` — is equally dangerous:
the computation runs and produces a result that is then dispatched with no metric name, causing
`OtelMetricHandler` to record it under `OidMapService.Unknown` or crash.

**Why it happens:**
These are two fields that form a logical pair. Standard config validation (`[Required]` attributes
or `IValidateOptions<T>`) enforces per-field rules but not cross-field co-presence rules unless
explicitly coded.

**Consequences:**
For the null-Action case: runtime exception inside `MetricPollJob.DispatchResponseAsync`. The
`ExceptionBehavior` swallows the exception (it is outermost in the pipeline) but the exception is
thrown in `MetricPollJob` itself, before `ISender.Send` is called — `ExceptionBehavior` does not
wrap `MetricPollJob`. The exception propagates to the Quartz job wrapper, which catches it as a
poll failure, increments `snmp_poll_errors_total`, and stamps liveness. The device's real metrics
(non-combined) are not affected because the exception fires after `foreach (var variable in
response)` has already dispatched all real varbinds — but only if combined metric computation
happens after the real-varbind loop, not interleaved.

For the no-Name case: `OtelMetricHandler` records the gauge under `"Unknown"`. This inflates
`snmp_gauge{metric_name="Unknown"}` with synthetic data, poisoning the Unknown sentinel that
operators use to discover unmapped OIDs.

**Warning signs:**
- `snmp_poll_errors_total` increments for a specific device while the device is reachable
- Pod log: unhandled exception from `MetricPollJob` referencing `Action` or `CombinedMetricName`
- `snmp_gauge{metric_name="Unknown"}` spikes with a new `oid` label that is empty or a sentinel value

**Prevention:**
Add a cross-field co-presence validator at config load time: if `CombinedMetricName` is set,
`Action` must be set; if `Action` is set, `CombinedMetricName` must be set. Fail the config entry
with a structured error. Do not rely on runtime null guards in `MetricPollJob` as the primary
enforcement — the job is `[DisallowConcurrentExecution]` and exceptions there affect all polls
for that device/poll-group pair.

**Phase address:** The phase adding combined metric config model and its validator.

---

### CM Pitfall 3: Synthetic Dispatch Inflates `snmp.event.published` and `snmp.event.handled`

**Severity:** CRITICAL

**What goes wrong:**
`LoggingBehavior` increments `snmp.event.published` (via `_metrics.IncrementPublished`) for every
`SnmpOidReceived` message that enters the pipeline. `OtelMetricHandler` increments
`snmp.event.handled` on every successfully recorded gauge or info. These counters are used by
operators to verify pipeline throughput: "N OIDs polled → N events published → N events handled."

When a combined metric is dispatched as a synthetic `SnmpOidReceived` (via `_sender.Send`), it
passes through the full pipeline: `LoggingBehavior` increments `snmp.event.published`. If the
synthetic message reaches `OtelMetricHandler`, `snmp.event.handled` also increments. Both counters
now reflect (real OIDs + synthetic dispatches), not just real SNMP data.

This means the ratio `snmp.event.published / snmp_poll_executed` — used to verify that every poll
cycle produced the expected number of metric events — becomes consistently higher than the number
of configured OIDs. Alarms set to fire when "events per poll exceeds configured OID count" will
trigger spuriously.

**Why it happens:**
`LoggingBehavior` fires unconditionally on `SnmpOidReceived`. There is no `Source` flag in the
current `SnmpOidReceived` model that distinguishes a real poll varbind from a synthetic combined
dispatch. `SnmpSource` enum has `Poll` and `Trap` — a `Synthetic` or `Combined` variant does not
exist yet.

**Consequences:**
Dashboards showing "pipeline throughput = events / polls" will overcount by the number of
combined metrics per poll group per device. For a device with 3 poll groups each producing 1
combined metric, `snmp.event.published` inflates by 3 per poll cycle. If this device runs at
10s intervals, the counter inflates by 18 extra per minute. Alerting rules calibrated against
a known OID count will either need recalibration or be permanently noisy.

**Warning signs:**
- `rate(snmp_event_published_total[1m]) / rate(snmp_poll_executed_total[1m])` exceeds the
  configured OID count per poll group
- The excess exactly equals the number of configured combined metrics across all poll groups

**Prevention:** Two options, choose one before implementation:

Option A — Add `SnmpSource.Combined` to the `SnmpSource` enum. Synthetic dispatches carry
`Source = SnmpSource.Combined`. `LoggingBehavior` checks for `Combined` and uses a separate
counter (e.g., `snmp.event.combined`) instead of `snmp.event.published`. This keeps pipeline
counters semantically clean at the cost of adding a new counter.

Option B — Accept the inflation and document it. Update all alerting rules and dashboards to
account for combined dispatches. This is the lower-effort path if combined metrics are few.

Regardless of which option is chosen, the decision must be made before implementation and
recorded in the phase plan. Changing the counter semantics after the feature ships requires
updating all consumer dashboards.

**Phase address:** The phase implementing synthetic dispatch from `MetricPollJob`.

---

## Moderate Pitfalls (Combined Metrics)

---

### CM Pitfall 4: Same CombinedMetricName in Multiple Poll Groups on the Same Device

**Severity:** MODERATE

**What goes wrong:**
A device has two poll groups: group A (10s interval) and group B (60s interval). Both configure
`CombinedMetricName = "npb_total_throughput"`. Each poll group independently computes its own
version of the metric and dispatches a synthetic `SnmpOidReceived`. Two time series land in
`snmp_gauge{metric_name="npb_total_throughput", device_name="NPB-01"}`.

The two series have different underlying update frequencies (10s vs 60s). Prometheus cannot
distinguish them by label — they have identical label sets if the synthetic dispatch uses the
same `AgentIp` and `DeviceName`. Prometheus last-write-wins in this case, creating a series
that oscillates between values: group A writes every 10s with its computation, group B writes
every 60s with a different computation. PromQL `rate()` over this series is garbage.

**Why it happens:**
Nothing in the config model prevents duplicate `CombinedMetricName` values across poll groups
on the same device. Config validation would need to check uniqueness of combined metric names
per device scope, not just per poll group.

**Consequences:**
Silent metric contamination, identical in character to the OID map collision (CM Pitfall 1) but
arising from operator config error rather than a name clash with a real OID. Rate calculations
are wrong. Alerting is unreliable.

**Warning signs:**
- `snmp_gauge{metric_name="npb_total_throughput", device_name="NPB-01"}` shows erratic step
  patterns that do not correspond to either poll interval alone
- `snmp.event.published` increments for the device exceed the sum of OIDs across all poll groups
  by more than the number of distinct combined metric names

**Prevention:**
At config load time, after building all poll groups for a device, validate that `CombinedMetricName`
values are unique across all poll groups within the same device. A combined metric name that appears
in two groups of the same device is a config error. Log: `"Device 'NPB-01': CombinedMetricName
'npb_total_throughput' appears in poll groups 0 and 2 — names must be unique per device"`.

Cross-device duplicate combined names are acceptable (different devices, different label sets).

**Phase address:** The phase adding combined metric config validation.

---

### CM Pitfall 5: Negative Diff Result Recorded as a Gauge — Valid but Misleading

**Severity:** MODERATE

**What goes wrong:**
A `Diff` action computes `current - previous` for a varbind value. If the previous value was
larger than the current value (e.g., a counter that wrapped, or a gauge that legitimately
decreased), the result is negative. This negative value is dispatched as a synthetic
`SnmpOidReceived` with `ExtractedValue < 0` and recorded in `snmp_gauge`.

`snmp_gauge` is modeled as a Prometheus gauge, which CAN hold negative values — this is not
technically wrong. However, operators who set up alerts like `snmp_gauge{metric_name="npb_rx_delta"} < 0`
as anomaly detection will see false positives on every counter wrap. More subtly, if the
combined metric is used in a tenant vector slot and the `Action = Diff` is intended to track
utilization (always non-negative), a negative value in the time series can cause the
downstream evaluation logic to produce incorrect results.

**Why it happens:**
Counter wrap is a known SNMP phenomenon (Counter32 wraps at 2^32). A simple `current - previous`
diff will produce a large negative value on wrap rather than detecting the wrap and computing
the correct delta.

**Consequences:**
For Counter32 wrap: the diff result is approximately `-4,294,967,296 + (new_value - old_value)`.
This very large negative value in the time series is obvious and easy to detect. For genuine
gauge decreases (e.g., active connection count drops), the negative diff is valid data and
should be recorded.

**Prevention:**
The phase implementing `Diff` action must explicitly decide the wrap-handling policy:

- Option A — Clamp to zero: if `current - previous < 0`, record 0 and emit a debug log. Simple
  but loses information about the magnitude of decrease.
- Option B — Wrap detection: if `TypeCode` is `Counter32` or `Counter64`, and the result is
  negative, compute the wrapped delta: `(MaxValue - previous) + current`. For `Counter32`, MaxValue
  = 4,294,967,295. This is the correct behavior for SNMP counters.
- Option C — Pass through: record the negative value as-is. Correct for gauge types but
  misleading for counter types.

The policy must be documented in the config schema (e.g., `DiffMode: WrapAware | Clamp | Raw`)
or applied by `TypeCode` automatically. Whatever is chosen, document in the phase plan and unit
test the counter-wrap case explicitly.

**Phase address:** The phase implementing the Diff action in `MetricPollJob`.

---

### CM Pitfall 6: Combined Metric Bypasses ValidationBehavior OID Format Check

**Severity:** MODERATE

**What goes wrong:**
`ValidationBehavior` rejects any `SnmpOidReceived` where `msg.Oid` does not match the regex
`^\d+(\.\d+){1,}$` (at least two numeric arcs separated by dots). When a synthetic
`SnmpOidReceived` is dispatched for a combined metric, it has no real OID. The implementer
must choose what to put in `msg.Oid`. Common choices and their failure modes:

- `msg.Oid = ""` (empty): fails `OidPattern.IsMatch("")`, `ValidationBehavior` rejects,
  increments `snmp.event.rejected`, returns without calling `OtelMetricHandler`. The
  combined metric is silently dropped.
- `msg.Oid = "0.0"` (sentinel): passes `OidPattern.IsMatch("0.0")`, continues through
  pipeline. `OidResolutionBehavior` calls `_oidMapService.Resolve("0.0")`, returns
  `"Unknown"`. `OtelMetricHandler` records it under `"Unknown"`. Combined metric appears
  as `snmp_gauge{metric_name="Unknown"}`.
- `msg.Oid = "combined"` (non-numeric): fails OID validation, rejected.
- `msg.Oid = null`: `OidPattern.IsMatch(null)` throws `ArgumentNullException` inside
  `ValidationBehavior`, caught by `ExceptionBehavior`, increments `snmp.event.errors`.

**Why it happens:**
`SnmpOidReceived.Oid` is `required` and typed as `string`. There is no variant of the message
type for synthetic dispatches. The OID field is mandatory but meaningless for combined metrics.

**Prevention:**
Two options:

Option A — Pre-set `MetricName` on the synthetic message before dispatch, bypassing
`OidResolutionBehavior`'s overwrite. Use a sentinel OID that passes validation (e.g.,
`"0.0"`) but ensure `MetricName` is already set to `CombinedMetricName`. Modify
`OidResolutionBehavior` to skip resolution when `msg.MetricName` is already non-null and
non-Unknown. This requires a single targeted change to `OidResolutionBehavior`.

Option B — Add a `SnmpSource.Combined` check in `ValidationBehavior`: skip OID format
validation if `msg.Source == SnmpSource.Combined`. Combined messages are trusted to have
a valid `MetricName` already set by the dispatcher.

Option A is lower risk because it requires only an additive guard in `OidResolutionBehavior`
rather than a logic change in `ValidationBehavior`.

**Phase address:** The phase implementing synthetic dispatch from `MetricPollJob`. Must be
resolved before any combined metric can reach `OtelMetricHandler`.

---

### CM Pitfall 7: Hot-Reload Removes CombinedMetricName — Stale Prometheus Series Lingers

**Severity:** MODERATE

**What goes wrong:**
A combined metric `"npb_byte_rate"` is active and has been publishing to `snmp_gauge` for
several days. An operator removes the `CombinedMetricName` entry from the devices ConfigMap.
`DeviceWatcherService` reloads, `MetricPollJob` no longer computes or dispatches the metric.
No new data points for `snmp_gauge{metric_name="npb_byte_rate"}` are produced.

In Prometheus, gauge time series do not auto-expire. The last known value for
`"npb_byte_rate"` remains in the TSDB until the series staleness timeout expires (default
5 minutes in Prometheus for scraped series, but the OTel SDK's periodic export means the
last exported data point simply ages out after one scrape interval with no new push).

In Grafana, the panel showing `"npb_byte_rate"` will display a flat line at the last known
value, then go absent. If the panel uses `last_value` fill mode, it shows the stale value
indefinitely until the operator notices. If the panel uses `No data` display, it goes blank,
which may be confused with a pipeline error.

**Why it happens:**
OpenTelemetry gauge instruments in this codebase (`snmp_gauge`) are recorded using
`ObservableGauge` or `Histogram` patterns. If the OTel SDK holds an instrument reference
keyed by metric name, removing the combined metric from config does not remove the instrument
from the SDK's registry — the instrument still exists, but its callback (if observable) is
never called again, or the last explicit `Record()` call is simply the last data point.

The Prometheus TSDB does not know that a metric was intentionally removed — it treats absence
of new data as a potential scrape failure, not a metric retirement.

**Consequences:**
Operational confusion: is the metric missing because the feature was removed, or because the
pipeline broke? No way to distinguish from Prometheus alone.

**Warning signs:**
- `snmp_gauge{metric_name="npb_byte_rate"}` time series goes flat then absent after a config
  reload, with no corresponding error in `snmp.event.errors` or `snmp_poll_errors_total`
- The metric name is no longer referenced in any active config but still appears in Prometheus

**Prevention:**
This is a fundamental property of Prometheus time series — there is no "delete metric on
config remove" primitive. Mitigation strategies:

1. Document in the operational runbook: "When removing a CombinedMetricName from config, the
   old Prometheus series will remain stale for one staleness timeout. This is expected behavior."
2. If the OTel SDK's `SnmpMetricFactory` uses an instrument cache keyed by metric name (which
   it does — see `CONCERNS.md` "MetricFactory: Unbounded instrument cache with no eviction"),
   the stale instrument reference prevents the same name from being re-registered cleanly if
   the combined metric name is later re-added. Add a note in the phase plan: the instrument
   cache does not need eviction for correctness but does prevent re-use of a removed metric
   name without a pod restart.
3. For production deployments: announce combined metric retirement via a separate Prometheus
   recording rule or dashboard annotation rather than relying on the time series going absent.

**Phase address:** The phase implementing combined metric dispatch. Document staleness behavior
in the operator guide for the feature.

---

### CM Pitfall 8: Thread Safety — MetricPollJob is Sequential, but Two Poll Groups for the Same Device Are Separate Jobs

**Severity:** MODERATE

**What goes wrong:**
`MetricPollJob` is `[DisallowConcurrentExecution]` — Quartz prevents two executions of the
*same* job key from running simultaneously. However, a device with two poll groups has *two
separate Quartz jobs* (e.g., `metric-poll-10.0.0.1_161-0` and `metric-poll-10.0.0.1_161-1`).
These two jobs can and do run concurrently.

If a combined metric's state (e.g., the "previous value" needed to compute a Diff) is stored
in a shared mutable field on a service singleton (e.g., a `ConcurrentDictionary<string,
double>` keyed on `(deviceName, metricName)`), both poll jobs may read and write to the same
entry concurrently. A Diff computation for group 0 running simultaneously with a Diff
computation for group 1 (if both use the same combined metric name on the same device — see
CM Pitfall 4) may interleave reads and writes.

More subtly: even within a single poll group, if the "previous value" state is stored on a
shared singleton and the same device's poll job fires from multiple Quartz threads (which
cannot happen due to `[DisallowConcurrentExecution]`, but this must be verified for the
combined state store too), concurrent access is possible.

**Why it happens:**
`[DisallowConcurrentExecution]` applies per job key, not per device. Combined state that needs
to persist across poll cycles (e.g., previous counter value for delta computation) must either:
(a) be stored inside `MetricPollJob` itself (scoped per job instance), but Quartz re-creates
job instances per execution, so instance state is lost; or (b) be stored in a shared service,
requiring explicit thread safety.

**Consequences:**
Race condition in Diff computation: if two poll groups access the same entry in the shared
previous-value store simultaneously, one may read a value written mid-update by the other.
The computed delta is wrong. In the worst case, the previous-value store returns a value
from a different poll group's computation, producing a nonsensical Diff result.

**Warning signs:**
- Combined Diff values oscillate erratically across poll cycles without corresponding
  changes in the underlying SNMP counter
- Debug log: `"Computing Diff for metric X: previous={A}, current={B}, delta={C}"` where
  C does not match B - A for successive log lines

**Prevention:**
Store previous values for Diff computation in a structure keyed by `(deviceName, pollGroupIndex,
combinedMetricName)`, not just `(deviceName, combinedMetricName)`. This ensures each poll group
has its own independent state cell that cannot be accessed by other jobs. Use `Volatile.Read`
and `Volatile.Write` (following the existing `MetricSlotHolder` pattern) or
`Interlocked.Exchange` for atomic updates. Do not use a shared mutable class-level field in
`MetricPollJob` — the instance is discarded after each execution.

**Phase address:** The phase implementing combined metric state management in `MetricPollJob`.

---

## Phase-Specific Warnings (Combined Metrics)

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| Combined metric config model | CombinedMetricName collides with real OID map entry (CM1) | Validate against `IOidMapService.ContainsMetricName` at config load |
| Combined metric config model | CombinedMetricName set without Action or vice versa (CM2) | Cross-field co-presence validator; fail config entry on partial spec |
| Combined metric config model | Duplicate CombinedMetricName across poll groups on same device (CM4) | Per-device uniqueness check during config load |
| Synthetic dispatch implementation | `snmp.event.published` and `snmp.event.handled` inflate (CM3) | Decide counter policy (new counter vs accept inflation) before implementation |
| Synthetic dispatch implementation | Synthetic OID fails ValidationBehavior OID regex (CM6) | Pre-set MetricName on synthetic message; add guard in OidResolutionBehavior |
| Diff action implementation | Negative result on counter wrap recorded without wrap detection (CM5) | Explicit wrap policy (WrapAware/Clamp/Raw) decided in phase plan; unit test wrap case |
| Previous-value state store | Two poll groups on same device race on shared state (CM8) | Key state by (device, pollGroupIndex, metricName); follow MetricSlotHolder pattern |
| Hot-reload and config removal | Stale Prometheus series lingers after CombinedMetricName removed (CM7) | Document in operator guide; note instrument cache implications |

---

## Sources (Combined Metrics Addendum)

All findings derived from direct source inspection (HIGH confidence):

- `src/SnmpCollector/Jobs/MetricPollJob.cs` — `[DisallowConcurrentExecution]`, per-job-key scope,
  `DispatchResponseAsync` sequential varbind loop, finally-block counters
- `src/SnmpCollector/Pipeline/Behaviors/LoggingBehavior.cs` — unconditional `IncrementPublished`
  on every `SnmpOidReceived`; `snmp.event.published` counter semantics
- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` — `OidPattern` regex
  `^\d+(\.\d+){1,}$`; rejects empty, non-numeric, and null OID strings
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — unconditional
  `_oidMapService.Resolve(msg.Oid)` overwrites `msg.MetricName` on every `SnmpOidReceived`
- `src/SnmpCollector/Pipeline/Behaviors/ExceptionBehavior.cs` — swallows exceptions from
  downstream behaviors; does NOT wrap `MetricPollJob.Execute` itself
- `src/SnmpCollector/Pipeline/Behaviors/ValueExtractionBehavior.cs` — extracts from TypeCode
  switch; no special handling for synthetic messages with no real `ISnmpData`
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — `IncrementHandled` per
  successfully recorded gauge; records `Unknown` when MetricName falls through
- `src/SnmpCollector/Pipeline/OidMapService.cs` — `ContainsMetricName(string)` available;
  `_metricNames` FrozenSet; `Unknown` constant used as the unresolved sentinel
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — `Volatile.Read`/`Write` pattern for
  thread-safe atomic updates; reference pattern for combined state storage
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — `SnmpSource` enum (Poll/Trap only);
  `Oid` is `required string`; no synthetic variant exists
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — `MeterName` (pipeline) vs
  `LeaderMeterName` (business); combined metrics dispatched via pipeline land in leader meter
- `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` — leader-only export gate applies
  to `SnmpCollector.Leader` meter; synthetic combined metrics would be subject to same gate
- `.planning/codebase/CONCERNS.md` — "MetricFactory: Unbounded instrument cache with no eviction"
  (stale instrument reference after hot-reload removal)


---

# SnapshotJob & SNMP SET Command Dispatch Pitfalls

**Domain:** Closed-loop SNMP SET control added to existing monitoring agent
**Researched:** 2026-03-16
**Confidence:** HIGH (verified against source code: MetricSlotHolder, ThresholdOptions, CommandSlotOptions, ISnmpClient, SharpSnmpClient, TenantVectorRegistry, CommandMapService, ServiceCollectionExtensions, MetricPollJob, SharpSnmpLib 12.5.7 XML docs)

> **Context:** These pitfalls apply to a subsequent milestone that adds periodic tenant evaluation and SNMP SET command dispatch (SnapshotJob) to the existing monitoring agent at v1.10. All source references are verified against the current codebase.

---

## Critical Pitfalls

---

### Pitfall S1: Treating MetricSlotHolder.ReadSlot() Null as Value Zero

**What goes wrong:**
ReadSlot() returns null before any poll has written to a slot (MetricSlotHolder.cs line 76: `s.Length > 0 ? s[^1] : null`). If SnapshotJob evaluates a threshold immediately after startup, a null-guard-free code path coerces null to 0.0. If ThresholdOptions.Min = 0, a coerced zero satisfies the threshold and an SNMP SET fires against every device on the first cycle.

**Why it happens:**
The MetricSlotHolder constructor sets `_box = SeriesBox.Empty`. At startup with a 15-second job cycle, the first SnapshotJob execution can precede the first MetricPollJob execution for any given device, especially if DNS resolution or SNMP GET is slow.

**Consequences:**
- SNMP SET commands fire before any real metric value exists.
- Suppression cache is populated with entries that were never legitimately triggered, blocking real commands for the full suppression window.
- Devices may receive SET commands on every restart.

**Prevention:**
- In SnapshotJob, treat `ReadSlot() == null` as "not yet evaluated" -- skip threshold evaluation entirely and continue to the next slot.
- Log at Debug: "Skipping threshold evaluation for {TenantId}/{MetricName}: no data yet".
- Unit tests must include a case where ReadSlot() returns null and assert no SET is dispatched.

**Warning signs:**
- SNMP SET commands in logs within the first 15 seconds after pod start.
- Suppression cache entries populated at pod startup before any poll completes.

**Phase that should address it:** SnapshotJob threshold evaluation logic.

---

### Pitfall S2: Concurrent SNMP SET to Same Device from Multiple Tenants

**What goes wrong:**
Multiple tenants can share the same (Ip, Port). TenantVectorRegistry deliberately allows this via fan-out routing (TenantVectorRegistry.cs lines 130-153). If two tenants both have a CommandSlotOptions targeting the same device and both thresholds are violated in the same SnapshotJob cycle, two SetAsync calls go to the same device with possibly conflicting values.

**Why it happens:**
Tenant priority controls evaluation order, not exclusion. Nothing in the existing data model prevents two tenants from declaring conflicting commands to the same device.

**Consequences:**
- Race condition on the device: some embedded firmware processes SET PDUs out of order or returns genErr when a second SET arrives before the first is acknowledged.
- If the same OID is SET to different values by two tenants, last-writer-wins is non-deterministic.

**Prevention:**
- SnapshotJob must carry [DisallowConcurrentExecution] -- the same attribute pattern used by MetricPollJob (MetricPollJob.cs line 22).
- Do not introduce per-tenant Task.WhenAll parallelism inside the job.
- In TenantVectorWatcherService validation: warn when two tenants share the same (Ip, Port, CommandName) with different Value.

**Warning signs:**
- Two log entries for the same (Ip, Port, CommandName) within a single SnapshotJob execution cycle.
- Device firmware returning genErr on alternating cycles.

**Phase that should address it:** SnapshotJob skeleton and scheduling.

---

### Pitfall S3: Suppression Cache Memory Growth Without Bounded Eviction

**What goes wrong:**
A ConcurrentDictionary suppression cache keyed on (Ip, Port, CommandName) never removes entries for deleted tenants, removed devices, or renamed commands. Entries accumulate indefinitely.

**Why it happens:**
The cache is checked every SnapshotJob cycle but entries for deleted configurations are never hit again. A renamed CommandName leaves the old (Ip, Port, OldName) entry permanently.

**Consequences:**
- Long-running pods accumulate thousands of stale entries after repeated config updates.
- In operator workflows with frequent command renames or tenant removals, the cache is never cleaned.

**Prevention:**
- After each SnapshotJob cycle, sweep entries whose LastSent + 2x SuppressionWindow is in the past and remove them.
- Alternatively: use MemoryCache with an absolute expiry of 2x SuppressionWindow per entry.
- Key must remain (Ip, Port, CommandName) only -- not keyed on tenant ID.

**Warning signs:**
- Suppression cache entry count growing monotonically over days without plateau.
- Memory growth correlated with command map hot-reloads.

**Phase that should address it:** Suppression cache implementation.

---

### Pitfall S4: ThresholdOptions Both Null -- Silent Always-Fire

**What goes wrong:**
ThresholdOptions allows Min = null and Max = null simultaneously. The v1.9 requirements (THR-04) define this as always-violated semantics: a threshold with both bounds null always returns true. The command fires every time the suppression window expires, indefinitely.

**Why it happens:**
Operators may configure `Threshold: {}` intending "off -- never fire." The model says the opposite: absent Threshold property means no evaluation; a present Threshold object with null bounds means always fire.

**Consequences:**
- Operators configure a placeholder threshold and get devices hammered with SET commands every suppression period.
- Hard to diagnose because commands fire on a regular schedule that looks intentional.

**Prevention:**
- If MetricSlotHolder.Threshold == null, skip evaluation entirely.
- If Threshold is non-null but both Min and Max are null, log a Warning at load time: "Tenant {TenantId} slot {MetricName}: Threshold present but both Min and Max are null -- command will fire on every suppression expiry".
- Document the semantics in ThresholdOptions XML comment.

**Warning signs:**
- SET commands firing at clock-regular intervals equal to the suppression window.
- Operator confusion: "why is this device being set every N minutes?"

**Phase that should address it:** Threshold evaluation logic and load-time validation.

---

### Pitfall S5: CommandSlotOptions.Value Not Validated Against ValueType at Load Time

**What goes wrong:**
CommandSlotOptions.Value is always a string. ValueType must be "Integer32", "IpAddress", or "OctetString". Load-time validation checks that ValueType is a known string but does not parse Value against it. If Value = "abc" and ValueType = "Integer32", int.Parse("abc") throws FormatException at dispatch time.

**Why it happens:**
TenantVectorOptionsValidator is currently a no-op (TenantVectorOptionsValidator.cs line 12 returns Success unconditionally). Command slot value validation must be added explicitly.

**Consequences:**
- SnapshotJob throws when building the Variable for the SET PDU.
- If not caught per-slot, one bad slot aborts the entire tenant command list that cycle.
- Silent skip if caught without logging -- operator never knows the command is broken.

**Prevention:**
- At load time, dry-run conversion: Integer32 uses int.TryParse; IpAddress uses IPAddress.TryParse. Log Error and skip slot if parse fails.
- At dispatch time, wrap each command slot in try/catch. Never let one bad slot abort the rest.

**Warning signs:**
- FormatException in SnapshotJob logs at command build time.
- Command slot present in config that never appears in dispatch logs.

**Phase that should address it:** CommandSlot validation phase.

---

### Pitfall S6: ISnmpClient.SetAsync Does Not Exist -- SharpSnmpClient Must Be Extended

**What goes wrong:**
ISnmpClient currently exposes only GetAsync (ISnmpClient.cs lines 16-21). Adding SetAsync to the interface without updating SharpSnmpClient breaks the build. Unit tests that mock the interface hide whether the real implementation was ever wired.

**Why it happens:**
SharpSnmpClient delegates to Messenger.GetAsync (SharpSnmpClient.cs line 21). The correct extension is Messenger.SetAsync(VersionCode, IPEndPoint, OctetString, IList<Variable>, CancellationToken), confirmed present in SharpSnmpLib 12.5.7 XML docs. The method exists; the delegation is straightforward but must be done explicitly.

**Consequences:**
- Build failure: CS0535 SharpSnmpClient does not implement interface member ISnmpClient.SetAsync.
- Or: tests pass with a mock, but no real SET traffic is sent.

**Prevention:**
- Add SetAsync to ISnmpClient and implement in SharpSnmpClient as `=> Messenger.SetAsync(version, endpoint, community, variables, ct)`.
- Both changes must land in the same plan -- not split across plans.

**Warning signs:**
- Build error on SharpSnmpClient.
- Tests pass but SNMP SET traffic absent from integration captures.

**Phase that should address it:** ISnmpClient extension (first plan touching SET execution).

---

## Moderate Pitfalls

---

### Pitfall S7: Stale Carried-Over Value Evaluated Against New Threshold After Config Reload

**What goes wrong:**
TenantVectorRegistry.Reload carries over slot values via CopyFrom for matching (Ip, Port, MetricName) keys (lines 99-104). If an operator changes Threshold.Min or Max, the new threshold is applied to the carried-over (pre-reload) value on the very next SnapshotJob cycle -- before the metric has been re-polled.

**Prevention:**
- Accept as designed: document that evaluation reflects the last known value.
- If spurious post-reload SETs are a problem: add a ThresholdChanged flag to MetricSlotHolder, set during CopyFrom when the new threshold differs, and skip evaluation until the next fresh poll.

**Warning signs:**
- A SET fires immediately after a config reload that only changed threshold values (not metric values).

**Phase that should address it:** Threshold evaluation phase; document carry-over behavior.

---

### Pitfall S8: CommandMapService.ResolveCommandOid Returns Null -- Null OID Passed to SET PDU

**What goes wrong:**
CommandSlotOptions.CommandName is resolved to an OID at execution time via ICommandMapService.ResolveCommandOid (CommandMapService.cs line 45). If the command map has not yet loaded (startup race) or the command name was removed during hot-reload, the result is null. Passing null to new ObjectIdentifier() throws ArgumentNullException.

**Why it happens:**
CommandMapWatcherService and TenantVectorWatcherService are independent Kubernetes ConfigMap watchers (ServiceCollectionExtensions.cs lines 248-251). The first SnapshotJob cycle can execute before CommandMapWatcherService delivers its first watch event.

**Prevention:**
- In SnapshotJob: null-check ResolveCommandOid result. If null, log Warning and skip slot.
- This is a transient startup condition on the first 1-2 cycles; it resolves as watchers catch up.

**Warning signs:**
- Warning log on first cycle only, then absent = startup race (expected).
- Warning persists = command name removed or typo in config.

**Phase that should address it:** SnapshotJob command dispatch.

---

### Pitfall S9: Priority Starvation -- High-Priority Tenant SETs Consume Entire Job Budget

**What goes wrong:**
SnapshotJob evaluates tenants in priority order. A high-priority tenant with many command slots and long per-device timeouts can consume the entire 15-second interval before low-priority tenants are reached.

**Prevention:**
- Per-command timeout ceiling: 2 seconds maximum. Use CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken) with CancelAfter(2000) per SET call -- same pattern as MetricPollJob.cs lines 92-93.
- Log at Debug after each cycle: total tenants evaluated, SET count, elapsed ms.
- Never break out of tenant iteration on a suppression cache hit.

**Warning signs:**
- Low-priority tenants never appear in dispatch logs even with clearly violated thresholds.
- Total job elapsed time approaching 15 seconds.

**Phase that should address it:** SnapshotJob timeout design.

---

### Pitfall S10: Long SNMP SET Timeouts Causing Quartz Misfire

**What goes wrong:**
If SnapshotJob takes longer than its trigger interval, Quartz misfires. With DisallowConcurrentExecution, the job does not pile up but the effective dispatch interval doubles during unreachability events.

**Prevention:**
- Same mitigation as S9: hard per-command timeout ceiling.
- Register trigger with WithMisfireHandlingInstructionNextWithRemainingCount (same policy as MetricPollJob).
- Log Warning if elapsed time exceeds 0.8 x intervalSeconds.

**Warning signs:**
- Quartz misfire log.
- SnapshotJob elapsed time approaching intervalSeconds.

**Phase that should address it:** SnapshotJob timeout and Quartz trigger configuration.

---

### Pitfall S11: Threshold Flapping -- Value Oscillates Around Boundary

**What goes wrong:**
If a metric value oscillates around the threshold boundary, the threshold is violated on alternate cycles. Once the suppression window expires, the next violation triggers another SET. The device receives repeated SET commands for a condition that never stably resolves.

**Prevention:**
- In current scope: suppression cache is the only flap guard. Accept flapping as a known limitation and document it.
- Log at Debug when a SET is suppressed: "SET suppressed for {CommandName}@{Ip}:{Port} -- last sent {ElapsedMs}ms ago".
- Future milestone: add HysteresisDelta to ThresholdOptions.

**Warning signs:**
- Same (Ip, Port, CommandName) firing at suppression-window intervals in dispatch logs.
- Metric value in Grafana oscillating near the threshold boundary.

**Phase that should address it:** Threshold evaluation phase; document suppression as the flap guard.

---

### Pitfall S12: SnapshotJob Must Not Dispatch Through the MediatR Pipeline

**What goes wrong:**
MetricPollJob dispatches SnmpOidReceived via ISender.Send through the full MediatR pipeline. If SnapshotJob cargo-cults this pattern for SET commands using a new IRequest<Unit>, MediatR open behaviors fire for the new type. LoggingBehavior and ExceptionBehavior are harmless, but introducing an unnecessary dispatch creates maintenance confusion and risks unexpected behavior if ValidationBehavior evolves.

**Prevention:**
- SnapshotJob calls ISnmpClient.SetAsync directly -- evaluate threshold, resolve OID from ICommandMapService, build Variable, call SetAsync. No MediatR dispatch.
- Add code comment: "// Direct ISnmpClient.SetAsync call -- not via MediatR pipeline (OID already resolved; OtelMetricHandler not needed for SET operations)".

**Warning signs:**
- A new IRequest<Unit> type appearing unexpectedly in LoggingBehavior log output.
- ValidationBehavior rejecting a command request due to missing DeviceName or OID format.

**Phase that should address it:** SnapshotJob design phase.

---

### Pitfall S13: Suppression Cache Check-Then-Act Race if Parallelism Is Introduced Later

**What goes wrong:**
The check-then-act pattern (read not suppressed, evaluate, fire SET, write suppressed) is not atomic. Under the current sequential SnapshotJob this is safe. If a future plan introduces Task.WhenAll across tenants, two concurrent evaluations can both read "not suppressed" for the same (Ip, Port, CommandName) and both fire.

**Prevention:**
- Sequential evaluation is mandatory. DisallowConcurrentExecution prevents job-level concurrency; never add inner task parallelism.
- Document in SnapshotJob XML comment: "// Sequential evaluation is intentional -- suppression cache atomicity depends on single-threaded job execution".

**Warning signs:**
- Duplicate SET commands for the same (Ip, Port, CommandName) within a single job cycle.

**Phase that should address it:** Suppression cache implementation.

---

## Minor Pitfalls

---

### Pitfall S14: SnapshotJob Liveness Stamping Must Follow MetricPollJob Pattern

**What goes wrong:**
MetricPollJob stamps ILivenessVectorService in a finally block unconditionally (MetricPollJob.cs lines 141-142). If SnapshotJob omits this, LivenessHealthCheck reports the pod as unhealthy after any exception -- even when the pod and pipeline are fully healthy.

**Prevention:**
- Copy the MetricPollJob finally block exactly: stamp liveness with the job key, clear OperationCorrelationId.
- Register the SnapshotJob key with IJobIntervalRegistry so LivenessHealthCheck computes the correct staleness threshold (15s x GraceMultiplier = 30s default).

**Warning signs:**
- Pod reports liveness unhealthy but all polls are succeeding.
- LivenessHealthCheck log showing SnapshotJob key absent from the liveness vector.

**Phase that should address it:** SnapshotJob liveness integration.

---

### Pitfall S15: Config Apply Order Race -- Command Map Not Loaded at First Cycle

**What goes wrong:**
CommandMapWatcherService and TenantVectorWatcherService are independent watchers. The first SnapshotJob cycle may execute before CommandMapWatcherService receives its first watch event. All ResolveCommandOid calls return null on the first 1-2 cycles.

**Prevention:**
- Same mitigation as S8: null-check + Warning log + skip. Transient startup condition.
- CS-07 guidance in ServiceCollectionExtensions.cs documents recommended apply order for operators.

**Warning signs:**
- All command slot lookups returning null on the first cycle only.

**Phase that should address it:** SnapshotJob startup tolerance.

---

### Pitfall S16: ValueType Case Sensitivity in JSON Deserialization

**What goes wrong:**
CommandSlotOptions.ValueType accepts "Integer32", "IpAddress", "OctetString". System.Text.Json default options do not normalize string values. An operator who writes "integer32" or "IPADDRESS" gets a mismatch at dispatch time, causing the slot to be silently skipped or throwing an unhandled switch case.

**Prevention:**
- At load-time validation, compare ValueType with StringComparison.OrdinalIgnoreCase and normalize to canonical casing before storing.
- Log an Error for an unrecognized ValueType with the exact received value.

**Warning signs:**
- Command slot present in config that never appears in dispatch logs, with no Error log from the validator.

**Phase that should address it:** CommandSlot validation phase.

---

## Phase-Specific Warnings (SnapshotJob & SET)

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|----------------|------------|
| ISnmpClient extension | Missing SetAsync on SharpSnmpClient (S6) | Add to interface + implementation in same plan |
| SnapshotJob skeleton | No [DisallowConcurrentExecution] (S2) | Apply from the first plan; never remove |
| Threshold evaluation logic | ReadSlot() null coerced to 0.0 (S1) | Explicit null guard + test for unwritten slot |
| Threshold evaluation logic | Both Min/Max null = silent always-fire (S4) | Warning at load time; document semantics |
| CommandSlot validation | Value not validated against ValueType (S5) | Dry-run parse at load time; per-slot try/catch at dispatch |
| Suppression cache | Unbounded growth (S3) | Time-based eviction sweep after each cycle |
| Suppression cache | Check-then-act race (S13) | Sequential mandate; XML comment in SnapshotJob |
| SnapshotJob dispatch | Null OID from command map (S8) | Null-check + Warning log + skip slot |
| SnapshotJob timeout design | Long SETs causing misfire (S10) | Per-command CancellationTokenSource with 2s ceiling |
| SnapshotJob liveness | Missing finally stamp (S14) | Copy MetricPollJob finally block pattern exactly |
| MediatR usage | Cargo-culting poll dispatch for SET (S12) | Direct ISnmpClient.SetAsync; document in code comment |

---

## Sources (SnapshotJob & SET Section)

| Claim | Source | Confidence |
|-------|--------|------------|
| ReadSlot() returns null before first write | MetricSlotHolder.cs line 76 | HIGH |
| ThresholdOptions.Min and .Max are double? | ThresholdOptions.cs lines 9-10 | HIGH |
| CommandSlotOptions.Value is always string | CommandSlotOptions.cs line 28 | HIGH |
| ISnmpClient exposes only GetAsync | ISnmpClient.cs lines 16-21 | HIGH |
| SharpSnmpClient delegates to Messenger.GetAsync | SharpSnmpClient.cs line 21 | HIGH |
| Messenger.SetAsync(VersionCode, IPEndPoint, OctetString, IList<Variable>, CancellationToken) exists | SharpSnmpLib 12.5.7 SharpSnmpLib.xml | HIGH |
| MetricPollJob uses [DisallowConcurrentExecution] | MetricPollJob.cs line 22 | HIGH |
| Multiple tenants share same (Ip, Port) via routing index | TenantVectorRegistry.cs lines 130-153 | HIGH |
| TenantVectorOptionsValidator is a no-op | TenantVectorOptionsValidator.cs line 12 | HIGH |
| CommandMapWatcherService is independent of TenantVectorWatcherService | ServiceCollectionExtensions.cs lines 248-251 | HIGH |
| Both-null threshold = always-violated semantics | v1.9 requirements THR-04 | HIGH |
| MetricPollJob liveness stamp in finally block | MetricPollJob.cs lines 141-142 | HIGH |
| Open behaviors fire for all IRequest<T> types dispatched via ISender.Send | ServiceCollectionExtensions.cs lines 384-396 | HIGH |
| CommandMapService.ResolveCommandOid returns null for missing name | CommandMapService.cs lines 44-46 | HIGH |
| Carry-over logic copies last sample on reload | TenantVectorRegistry.cs lines 99-104 | HIGH |

---

*SnapshotJob & SET section added: 2026-03-16*
