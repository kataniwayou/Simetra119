# Feature Landscape: v1.6 Organization & Command Map Foundation

**Domain:** SNMP monitoring agent — OID map hygiene, human-name device config, command map infrastructure
**Researched:** 2026-03-13
**Confidence:** HIGH — derived from codebase analysis of existing watchers, config models, validators, and device analysis documents

---

## Context: What Already Exists

Understanding the baseline is required to avoid restating what is already built.

| Existing Capability | Implementation |
|---------------------|---------------|
| OID map load + hot-reload | `OidMapWatcherService` + `OidMapService` (FrozenDictionary volatile swap) |
| OID → metric name resolution | `IOidMapService.Resolve()`, returns `"Unknown"` for unmapped OIDs |
| Reverse lookup (name → exists?) | `IOidMapService.ContainsMetricName()` |
| Device config load + hot-reload | `DeviceWatcherService` + `DeviceRegistry` (FrozenDictionary volatile swap) |
| Device config format | Raw OID strings in `MetricPollOptions.Oids[]` |
| Startup validation | `DevicesOptionsValidator` — catches missing Name, bad IP, port range, empty Oids[] |
| Duplicate detection | `DeviceRegistry` throws on duplicate `IP:Port` at load time |
| Diff logging on reload | `OidMapService.UpdateMap()` logs added/removed/changed entries |
| K8s ConfigMap watcher pattern | Three watchers: `simetra-oidmaps`, `simetra-devices`, `simetra-tenantvector` |

**The gap this milestone fills:**

1. **OID map has no integrity validation** — duplicates (same OID or same name appearing twice) are silently tolerated. The map is loaded as a `Dictionary<string, string>` which silently last-writes on duplicate keys.
2. **Device config exposes raw OIDs to operators** — with 744 OBP command instances and 390+ NPB metrics, writing and maintaining raw OID strings is error-prone and unreadable.
3. **No command map exists** — there is no infrastructure for mapping SET command OIDs to human names, which is prerequisite for any future command execution.

---

## Table Stakes

Features that MUST exist for this milestone to be coherent. Missing any of these produces an incomplete or broken state.

### TS-01: OID Map Duplicate OID Detection

**What:** When `OidMapService.UpdateMap()` processes a new map, detect any case where the same OID string key appears more than once. Because JSON parsing via `Dictionary<string, string>` silently drops duplicates (last writer wins), duplicate detection must happen before or during load — not by inspecting the final dictionary.

**Detection point:** In `OidMapWatcherService.HandleConfigMapChangedAsync()` before calling `_oidMapService.UpdateMap()`, parse the JSON into a list of key-value pairs (rather than directly into a Dictionary) and scan for duplicate keys. Alternatively, parse with `JsonDocument` and walk the properties.

**Behavior on detection:** Log a structured warning for each duplicate OID key found: which OID, which names it maps to, and which name was retained. Do NOT reject the entire map — log the conflict and retain last-occurrence semantics to match existing behavior. A warning is sufficient; operators will see it in logs.

**Why Expected:** The OID map is the translation layer for all metric names in the system. A duplicate OID entry means one mapping silently wins and the other is discarded — an operator who adds a line and sees no effect has no indication why. With OBP's 32-link structure (32 × 32 unique OIDs per link type) and NPB's 8 MIB files, the map will grow to hundreds of entries, making typos and copy-paste errors likely.

**Complexity:** Low
**Depends On:** Existing `OidMapWatcherService` (injection point for validation), existing `OidMapService` (UpdateMap is the sink)

---

### TS-02: OID Map Duplicate Name Detection

**What:** Detect when the same metric name value appears more than once in the OID map. This is a separate and equally important defect: two different OIDs mapping to the same name (e.g., both `...1.3.4.0` and `...2.3.4.0` → `"obp_channel_L1"`) means the same metric name would be emitted from two different SNMP OIDs, creating Prometheus cardinality ambiguity.

**Detection point:** After parsing the full map (post-TS-01 duplicate OID check), scan values for duplicates. Build a `Dictionary<string, List<string>>` of name → OIDs and flag any name with more than one OID.

**Behavior on detection:** Log a structured warning for each duplicate name: which metric name is duplicated, and which OIDs both claim it. Do NOT reject the map. This is a warning, not a fatal error — but operators need visibility.

**Why Expected:** The OBP device has 32 links with identically named per-link metrics differentiated by suffix (`_L1`, `_L2`, ...). A copy-paste error where `_L1` is reused for another link's OID would silently cause two physical signals to share a Prometheus metric name, making them indistinguishable except by the `agent` label. This is a data correctness issue, not just aesthetic.

**Complexity:** Low
**Depends On:** TS-01 (both validations run at the same load point)

---

### TS-03: Device Config — Human Name Field (`Metrics`)

**What:** Add a `Metrics` property to `MetricPollOptions` (alongside the existing `Oids` property) that accepts a list of metric name strings. Device config authors write metric names (e.g., `"obp_channel_L1"`) instead of raw OIDs.

**Config format before:**
```json
{ "IntervalSeconds": 10, "Oids": ["1.3.6.1.4.1.47477.10.21.1.3.4.0"] }
```

**Config format after:**
```json
{ "IntervalSeconds": 10, "Metrics": ["obp_channel_L1"] }
```

**Why Expected:** With 26+ OBP OIDs and 71+ NPB OIDs currently in `devices.json`, the file is already difficult to maintain. Expanding to the full 1,040 OBP metric instances and 390+ NPB metrics without human names makes operator-level config changes nearly impossible. Human names are the names operators already know from Grafana dashboards.

**Complexity:** Low — adding a new property to an existing model
**Depends On:** `MetricPollOptions` (existing model), `OidMapService` (reverse lookup at load time)

---

### TS-04: Human Name Resolution at Device Load Time

**What:** In `DeviceWatcherService.HandleConfigMapChangedAsync()`, after parsing `devices.json`, resolve each entry in `MetricPollOptions.Metrics[]` to its corresponding OID using the reverse OID map (`IOidMapService`). The runtime `MetricPollInfo.Oids` list (used by Quartz jobs for SNMP GET) is populated with the resolved OIDs.

**Resolution mechanism:** The existing `IOidMapService.ContainsMetricName()` confirms a name exists. A new method `IOidMapService.ResolveToOid(metricName)` (or equivalent) performs the reverse lookup: name → OID. This requires adding a reverse index to `OidMapService` (built at `UpdateMap` time alongside the forward map).

**Dependency ordering:** OID map must be loaded before device config resolves names. The existing startup sequence already loads OidMap before devices (OidMapWatcherService and DeviceWatcherService both do initial loads at startup, but DeviceWatcher must wait for OidMap to be available). This requires `DeviceWatcherService` to depend on `IOidMapService` being initialized — which it already is, since both are registered as singletons and initial load happens in `ExecuteAsync` (not the constructor).

**Why Expected:** Without resolution at load time, the Quartz jobs have no OIDs to poll. The resolution must happen before `DynamicPollScheduler.ReconcileAsync()` is called, because ReconcileAsync uses `MetricPollInfo.Oids` to build jobs.

**Complexity:** Medium — requires reverse index in OidMapService, dependency on load ordering
**Depends On:** TS-03 (Metrics field exists), TS-06 (reverse index on OidMapService), existing `DeviceWatcherService`

---

### TS-05: Device Config Validation — Unknown Metric Name

**What:** During device config load (in `DeviceWatcherService` or a validator called from it), when resolving `Metrics[]` entries, detect any metric name that does not exist in the current OID map. Log a structured warning with the device name, poll group index, and the unresolvable metric name. Skip that entry (do not include an unknown OID placeholder). If ALL metrics in a poll group fail to resolve, log an error for that poll group.

**Behavior:** Warning, not rejection. The device config as a whole is still applied. Individual unresolvable metrics are skipped. Operators must see the warning to know their config has a typo.

**Why Expected:** With 390+ NPB metrics and 1,040 OBP metric instances, a single typo in a metric name will silently drop that metric from polling. Without a warning, the operator has no feedback that polling is incomplete. This is the human-name equivalent of the existing "Unknown" fallback for raw OIDs.

**Complexity:** Low
**Depends On:** TS-04 (resolution step where unknown names are detected)

---

### TS-06: OidMapService Reverse Index

**What:** Add a reverse lookup to `OidMapService`: metric name → OID. This is a second `FrozenDictionary<string, string>` (name → OID) built alongside the existing forward map (OID → name) in `UpdateMap()`. Expose via a new interface method: `string? ResolveToOid(string metricName)` — returns null if the name is not in the map.

**Why Expected:** Without a reverse index, `DeviceWatcherService` cannot translate metric names in `Metrics[]` to OID strings needed for SNMP GET. Building it at map load time (O(n)) is cheap; doing it at query time (O(n) scan) would be expensive across hundreds of metrics.

**Complexity:** Low — mirrors the existing forward dictionary pattern
**Depends On:** Existing `OidMapService` and `IOidMapService` interface

---

### TS-07: Backward Compatibility — `Oids` and `Metrics` Coexistence

**What:** A device config entry may use `Oids` (raw OIDs, existing format), `Metrics` (human names, new format), or both. The runtime `MetricPollInfo.Oids` is populated by:
1. All entries from `Oids[]` (used as-is, no resolution needed)
2. All entries from `Metrics[]` that resolve to a known OID

A single poll group may mix both (e.g., a device that has some OIDs not yet in the OID map and some that are).

**Why Expected:** `devices.json` currently uses raw OIDs. Migrating to human names is a one-time operator action, but the migration cannot be atomic — during the transition, some poll groups will have been updated and some not. Coexistence is necessary to allow incremental migration.

**Complexity:** Low
**Depends On:** TS-03 (Metrics field), TS-04 (resolution), existing `Oids` field in `MetricPollOptions`

---

### TS-08: Command Map File Format

**What:** Define `commandmaps.json` as an OID-to-command-name mapping file with the same format as `oidmaps.json`: a flat JSON object where each key is an OID string and each value is a command name string.

```json
{
  "1.3.6.1.4.1.47477.10.21.1.3.3.0": "obp_set_work_mode_L1",
  "1.3.6.1.4.1.47477.10.21.1.3.4.0": "obp_set_channel_L1",
  ...
}
```

**Scope:** OBP has 8 NMU + 23 per-link × 32 links = 744 command instances. NPB has ~250+ command objects. Both are documented in `Docs/OBP-Device-Analysis.md` and `Docs/NPB-Device-Analysis.md`.

**Why Expected:** Command maps are the prerequisite for any future SET command execution. Without a lookup table, the agent cannot translate a human command name ("set link 1 to primary channel") to the OID it must SET. The format mirrors oidmaps so the same parsing infrastructure applies.

**Complexity:** Low — format definition only, no implementation required beyond specifying the schema
**Depends On:** Nothing — pure schema definition

---

### TS-09: CommandMapService

**What:** A new singleton service `ICommandMapService` / `CommandMapService` analogous to `OidMapService`. Maintains:
- Forward map: OID → command name (`FrozenDictionary<string, string>`)
- Reverse map: command name → OID (`FrozenDictionary<string, string>`)

Interface:
```csharp
public interface ICommandMapService
{
    string ResolveCommandName(string oid);      // OID → name, returns "Unknown" if absent
    string? ResolveToOid(string commandName);   // name → OID, returns null if absent
    int EntryCount { get; }
    void UpdateMap(Dictionary<string, string> entries);
}
```

Atomic swap via volatile `FrozenDictionary` write on `UpdateMap()`, identical to `OidMapService`.

**Why Expected:** The lookup table must be in-process and O(1) for both directions. The pattern is proven by `OidMapService` — no reason to diverge.

**Complexity:** Low — direct structural copy of `OidMapService`
**Depends On:** TS-08 (format definition), existing `OidMapService` as structural template

---

### TS-10: CommandMapWatcherService — K8s ConfigMap Watch

**What:** A new `BackgroundService` `CommandMapWatcherService` that watches the `simetra-commandmaps` ConfigMap via the K8s API and calls `ICommandMapService.UpdateMap()` on change. Follows the identical pattern as `OidMapWatcherService`:
- Initial load on `ExecuteAsync` start
- Watch loop with automatic 5s reconnect on disconnect
- `SemaphoreSlim` serialization of concurrent reload requests
- Graceful handling of: missing ConfigMap key, null deserialization, JSON parse failure
- `WatchEventType.Deleted` → log warning, retain current map

ConfigMap name: `simetra-commandmaps`
ConfigMap key: `commandmaps.json`

**Why Expected:** Every live config in this system uses a ConfigMap watcher. Without one, command map updates require a pod restart. The pattern is established — deviating from it would be surprising and inconsistent.

**Complexity:** Low — structural copy of `OidMapWatcherService`
**Depends On:** TS-09 (CommandMapService to call UpdateMap on), existing K8s client infrastructure

---

### TS-11: CommandMapWatcherService — Local Dev Fallback

**What:** When running outside Kubernetes (local dev), the `CommandMapWatcherService` must fall back to loading `commandmaps.json` from the local filesystem at the same path used by `oidmaps.json` (e.g., `src/SnmpCollector/config/commandmaps.json`). The fallback mechanism must match the pattern used by `OidMapWatcherService` and `DeviceWatcherService`.

**Why Expected:** Developers cannot run K8s locally. The existing watchers all have local fallback via the `appsettings.Development.json` file path or direct filesystem read. `CommandMapWatcherService` must be usable in both contexts without environment-specific code paths visible to the developer.

**Complexity:** Low
**Depends On:** TS-10 (watcher shell), existing local dev config pattern

---

### TS-12: Command Map Duplicate Validation

**What:** Apply the same duplicate OID and duplicate name detection from TS-01 and TS-02 to `commandmaps.json`. Warn on duplicate OID keys (two entries for the same OID). Warn on duplicate command name values (two OIDs mapped to the same command name). Log and continue — do not reject the map.

**Why Expected:** The command map will contain 744+ OBP entries and 250+ NPB entries — a large file with real copy-paste risk. The same integrity problem that exists for oidmaps applies equally here.

**Complexity:** Low
**Depends On:** TS-09 / TS-10 (CommandMapService and watcher), same pattern as TS-01/TS-02

---

## Differentiators

Features that add robustness and operational visibility. Not strictly required for basic function, but correct systems include them.

### D-01: OID Map Validation Structured Log Format

**What:** When duplicate OIDs or duplicate names are detected (TS-01, TS-02), emit a structured log entry with a consistent event type tag (e.g., `OidMapDuplicateOid`, `OidMapDuplicateName`) so operators can write log-based alerts in Grafana/Loki. Include: duplicate key/value, conflicting entries, source ConfigMap and key.

**Value Proposition:** Without a consistent log structure, operators cannot alert on config errors. A raw `LogWarning` message is sufficient for human reading but cannot be queried systematically.
**Complexity:** Low
**Depends On:** TS-01, TS-02

---

### D-02: Device Config Reload Diff Includes Metric Name Translation Changes

**What:** The existing `DeviceRegistry.ReloadAsync()` logs a diff of added/removed devices. Extend the diff to include cases where a device's OID list changed due to metric name resolution (e.g., a metric name was added to oidmaps, causing a previously-unresolvable Metrics[] entry to now resolve). This makes hot-reload behavior observable.

**Value Proposition:** Operators who add a new OID map entry to fix an unresolvable metric name need confirmation that the device config picked it up. Without this log, the fix is silent.
**Complexity:** Medium — requires cross-watcher awareness (OID map change triggers device re-resolution)
**Depends On:** TS-04 (resolution), TS-06 (reverse index), existing DeviceRegistry reload path

---

### D-03: OID Map Change Triggers Device Config Re-Resolution

**What:** When the OID map changes (via `OidMapWatcherService`), any device that has unresolved `Metrics[]` entries (logged as warnings by TS-05) should be re-resolved against the new OID map. This requires `OidMapWatcherService` to notify `DeviceWatcherService` or `DeviceRegistry` of the change, or for the device registry to hold the original `Metrics[]` config and re-resolve on OID map reload.

**Value Proposition:** Without this, adding a new OID-to-name mapping in oidmaps doesn't automatically fix device configs that reference that name. The operator would need to touch devices.json (even with no changes) to trigger a device reload that re-resolves names. This is surprising behavior.

**Complexity:** Medium — cross-watcher dependency requires careful design to avoid circular references
**Depends On:** TS-04, TS-06, existing `OidMapWatcherService` + `DeviceWatcherService`

---

### D-04: CommandMapService Diff Logging on Reload

**What:** When `CommandMapService.UpdateMap()` is called, log a structured diff of added/removed/changed command entries — identical to the pattern in `OidMapService.UpdateMap()`. Log counts and individual changes.

**Value Proposition:** Makes the command map observable during hot-reload. Operators know exactly what changed when they push a new ConfigMap.
**Complexity:** Low — direct copy of existing OidMapService diff logging
**Depends On:** TS-09 (CommandMapService)

---

### D-05: `Metrics[]` Validation at Config Load — Regex or Name Convention Check

**What:** Before attempting OID resolution, validate that each metric name string in `Metrics[]` matches the project's naming convention (snake_case, alphanumeric + underscores). Reject entries that contain OID-like strings (contain only digits and dots) — this catches the mistake of accidentally putting a raw OID in the `Metrics[]` field instead of the `Oids[]` field.

**Value Proposition:** The most likely operator error when migrating from `Oids` to `Metrics` is putting an OID string in the wrong field. A quick format check gives immediate feedback.
**Complexity:** Low
**Depends On:** TS-03 (Metrics field exists), TS-05 (validation runs at the same point)

---

### D-06: Entry Count Metrics for Both Maps

**What:** Expose `ICommandMapService.EntryCount` (mirrors `IOidMapService.EntryCount`). Log the count at startup and on every reload. This confirms the CommandMap is loaded and sized as expected.

**Value Proposition:** A zero-entry command map (ConfigMap not found, parse error) would silently accept all commands as "Unknown". Logging the count makes this visible without requiring a health-check query.
**Complexity:** Low
**Depends On:** TS-09, TS-10

---

## Anti-Features

Things to deliberately NOT build in this milestone.

### AF-01: SET Command Execution

**What:** Do NOT implement SNMP SET operations, command dispatching, command queuing, or any mechanism that writes values to devices.
**Why Avoid:** This milestone builds the lookup TABLE only — the infrastructure that knows OID → command name. Executing commands is a separate milestone with different concerns (authorization, retry logic, audit logging, value type validation). Building execution now would be premature.
**What to Do Instead:** `ICommandMapService` provides lookup only. The only consumers in this milestone are unit tests verifying the map contains the right entries.

---

### AF-02: Command Authorization or Access Control

**What:** Do NOT add any authorization model, role-based access, or permission checking for commands.
**Why Avoid:** No commands are executed in this milestone. Authorization only makes sense when there is something to authorize.
**What to Do Instead:** Design `ICommandMapService` with a clean interface boundary that a future authorization layer can sit in front of.

---

### AF-03: Command Schemas or Typed Command Parameters

**What:** Do NOT define command parameter schemas (e.g., "this command takes an INTEGER 0-7" or "this command takes an IpAddress"). Do NOT create typed command request objects.
**Why Avoid:** The command map is OID → name, exactly like oidmaps. Type information for command parameters is MIB-level knowledge that would require a separate schema definition file. It is out of scope for this milestone.
**What to Do Instead:** Store command name strings only. Type metadata belongs in a future "command schema" milestone when SET execution is being designed.

---

### AF-04: Mandatory Migration of `devices.json` to Human Names

**What:** Do NOT require that all existing `devices.json` entries migrate to `Metrics[]` format in this milestone. Do NOT deprecate or remove the `Oids[]` field.
**Why Avoid:** The existing `devices.json` uses raw OIDs. Forcing migration as part of this milestone mixes infrastructure work (adding the capability) with operational work (updating all device configs). TS-07 (coexistence) is the correct scope boundary.
**What to Do Instead:** Support both formats. Document the migration path. Let operators migrate at their own pace.

---

### AF-05: Separate Command ConfigMap per Device Type

**What:** Do NOT create separate ConfigMaps for OBP commands (`simetra-commandmaps-obp`) and NPB commands (`simetra-commandmaps-npb`). Do NOT namespace command names by device type in the map keys.
**Why Avoid:** The `oidmaps` ConfigMap contains entries for all device types in a single flat dictionary, and it works. Splitting by device type adds operational complexity (two watchers, two file paths, two Kubernetes objects) with no benefit. Command names are already namespaced by convention (`obp_*`, `npb_*`).
**What to Do Instead:** A single `simetra-commandmaps` ConfigMap with all command entries for all device types, exactly mirroring the oidmaps pattern.

---

### AF-06: Command Map HTTP API or Prometheus Export

**What:** Do NOT expose the command map via REST API or export command names as Prometheus metrics.
**Why Avoid:** The command map is operational configuration, not telemetry. Exposing it externally creates a maintenance surface for no benefit. Prometheus labels are for metric data, not config dictionaries.
**What to Do Instead:** In-process access only via `ICommandMapService`. Log the entry count and diff on reload.

---

### AF-07: Validate That OID Map Names Match Device Config Names at Load Time

**What:** Do NOT cross-validate that every metric name referenced in `devices.json` `Metrics[]` exists in oidmaps at startup as a hard validation failure (blocking startup).
**Why Avoid:** Soft warning (TS-05) is correct. Hard failure would mean a single unresolvable metric name in any device's poll group blocks the entire agent from starting, which is too severe. The agent should degrade gracefully (skip the unresolvable entries, log warnings, continue polling everything else).
**What to Do Instead:** TS-05 (log warning, skip entry) is the correct behavior.

---

## Feature Dependencies

```
TS-01 (Duplicate OID Detection)
    |
    +--> D-01 (Structured Log Format)

TS-02 (Duplicate Name Detection)
    |
    +--> D-01 (Structured Log Format)

TS-06 (OidMapService Reverse Index)
    |
    +--> TS-04 (Human Name Resolution at Device Load)
              |
              +--> TS-03 (Metrics Field)
              |
              +--> TS-05 (Unknown Metric Warning)
              |
              +--> TS-07 (Backward Compat — Oids + Metrics)
              |
              +--> D-02 (Reload Diff — name translation changes)
              |
              +--> D-03 (OID Map Change triggers re-resolution)
              |
              +--> D-05 (Name convention validation)

TS-08 (Command Map File Format)
    |
    +--> TS-09 (CommandMapService)
              |
              +--> TS-10 (CommandMapWatcherService K8s)
              |         |
              |         +--> TS-11 (Local Dev Fallback)
              |         |
              |         +--> TS-12 (Command Map Duplicate Validation)
              |
              +--> D-04 (CommandMap Diff Logging)
              |
              +--> D-06 (Entry Count Metrics)
```

### Critical Path

Minimum viable path for the milestone:

```
TS-06 → TS-03 + TS-04 + TS-05 + TS-07   (human-name device config)
TS-01 + TS-02                              (OID map validation)
TS-08 → TS-09 → TS-10 + TS-11 + TS-12   (command map infrastructure)
```

TS-01/TS-02 are independent of the device config changes — they can be built first as isolated improvements to the existing OID map watcher. TS-06 is the prerequisite for all device-config human-name work. Command map infrastructure (TS-08 through TS-12) is entirely independent of the OID map and device config changes.

---

## MVP Recommendation

**Must build (12 features — all table stakes):**

1. **TS-06** OidMapService reverse index (foundation for device config resolution)
2. **TS-01** Duplicate OID detection in OID map load
3. **TS-02** Duplicate name detection in OID map load
4. **TS-03** `Metrics[]` field on `MetricPollOptions`
5. **TS-04** Human name resolution at device load time
6. **TS-05** Unknown metric name warning during resolution
7. **TS-07** Backward compat — `Oids` and `Metrics` coexistence
8. **TS-08** Command map file format definition
9. **TS-09** `CommandMapService` (lookup table, forward + reverse)
10. **TS-10** `CommandMapWatcherService` (K8s ConfigMap watch)
11. **TS-11** Local dev fallback for CommandMapWatcher
12. **TS-12** Command map duplicate validation

**Should build (4 differentiators — high operational value, low cost):**

1. **D-01** Structured log format for OID map warnings (enables Loki alerts)
2. **D-04** CommandMapService diff logging on reload (matches existing OidMapService pattern)
3. **D-05** `Metrics[]` name convention validation (catches OID-in-wrong-field mistakes)
4. **D-06** Entry count logging for CommandMapService

**Evaluate before committing (2 differentiators — medium complexity):**

- **D-02** Reload diff including name translation changes — useful but requires careful diff logic across two data sources
- **D-03** OID map change triggers device re-resolution — significant cross-watcher coupling; evaluate whether the simpler alternative (document that re-resolution requires touching devices.json) is acceptable for the milestone

**Explicitly do NOT build (7 anti-features):**

- AF-01: SET command execution
- AF-02: Command authorization
- AF-03: Command schemas / typed parameters
- AF-04: Mandatory migration to human names
- AF-05: Separate per-device-type CommandMaps
- AF-06: Command map HTTP API or Prometheus export
- AF-07: Hard startup failure on unresolvable metric names

---

## Sources

- Codebase: `src/SnmpCollector/Pipeline/OidMapService.cs` — FrozenDictionary volatile swap, UpdateMap diff logging, ContainsMetricName (HIGH confidence)
- Codebase: `src/SnmpCollector/Pipeline/IOidMapService.cs` — current interface surface, missing ResolveToOid (HIGH confidence)
- Codebase: `src/SnmpCollector/Services/OidMapWatcherService.cs` — ConfigMap watch pattern, SemaphoreSlim, reconnect, graceful error handling (HIGH confidence)
- Codebase: `src/SnmpCollector/Services/DeviceWatcherService.cs` — device reload pattern, dependency on DeviceRegistry + DynamicPollScheduler (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/MetricPollOptions.cs` — current `Oids[]` field, no Metrics field (HIGH confidence)
- Codebase: `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` — existing validation pattern, duplicate IP+Port detection (HIGH confidence)
- Codebase: `src/SnmpCollector/config/oidmaps.json` — 27 OBP entries + 71 NPB entries, confirms real map size (HIGH confidence)
- Codebase: `src/SnmpCollector/config/devices.json` — confirms raw OID format in current MetricPolls (HIGH confidence)
- Device analysis: `Docs/OBP-Device-Analysis.md` — 8 NMU + 23 per-link × 32 links = 744 command instances, 16 NMU + 32 per-link × 32 links = 1,040 metric instances (HIGH confidence)
- Milestone context: NPB ~250+ command objects across 8 MIB files, ~390+ metrics (MEDIUM confidence — counts from milestone description, not independently verified against NPB-Device-Analysis.md full text)

---

*Feature research for: v1.6 Organization & Command Map Foundation*
*Researched: 2026-03-13*
