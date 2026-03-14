# Research Summary — v1.7 Configuration Consistency & Tenant Commands

**Project:** Simetra119 SNMP Collector
**Domain:** SNMP monitoring agent — config model cleanup, CommunityString validation, tenant command data model
**Researched:** 2026-03-14
**Confidence:** HIGH

---

## Executive Summary

v1.7 is a pure config-layer milestone. It introduces zero new NuGet packages, zero new
infrastructure services, and zero SNMP execution logic. The work spans three areas: config
model additions (new fields on `MetricSlotOptions` + `TenantOptions`, new `CommandSlotOptions`
class), validator activation (the `TenantVectorOptionsValidator` is currently a no-op and
becomes a real validator), and mechanical renames (`tenantvector.json` → `tenants.json`,
`Name` field in devices context). All patterns already exist in the codebase — the changes
are extensions of the established `IValidateOptions<T>`, FrozenDictionary volatile-swap, and
`CommunityStringHelper` convention patterns. There is no design novelty; every v1.7 change
will be immediately recognizable to anyone who has read the v1.6 source.

The dominant risk is not design complexity — it is the mechanical risk of partial renames.
The config key rename (`tenantvector.json` → `tenants.json`) touches a C# constant, a YAML
data key, a local dev file, and inline heredocs in E2E test scripts. Any artifact left
unrenamed compiles cleanly but causes a silent load miss at runtime (the watcher finds no
data key and retains stale state with no error). The second significant risk is DNS-vs-IP
in the routing index: removing the `IDeviceRegistry` dependency from `TenantVectorRegistry`
eliminates the DNS resolution step that was previously delegated to `DeviceRegistry`. Without
a replacement (direct `Dns.GetHostAddressesAsync` in `Reload()` or a requirement that tenant
config contains pre-resolved IPs), routing keys are built from DNS strings while incoming
SNMP packets arrive with resolved IPs — a permanent routing miss that produces no error.

The recommended implementation order is: config models first (unblocks everything else),
then validators (independently testable before registry changes), then
`DeviceRegistry.BuildPollGroups` skip behavior (isolated, no cross-dependencies), then the
`TenantVectorRegistry` refactor (removes DeviceRegistry dependency, activates new model
fields), then the `DeviceOptions.Name` → `CommunityString` rename, and finally the
ConfigMap rename as a single atomic commit last. This order matches the dependency flow
"models → validators → registry → infrastructure" identified in ARCHITECTURE.md and
minimizes blast radius at each step.

---

## Key Findings

### Recommended Stack

**Zero new dependencies.** All required types exist in the current stack.

**Core technologies (unchanged):**
- `.NET 9 / C#` — runtime; no version change
- `Lextm.SharpSnmpLib 12.5.7` — `SnmpType` enum used for ValueType-to-wire mapping at future SET execution; no usage change in v1.7
- `Microsoft.Extensions.Options 9.0.0` — `IValidateOptions<T>` pattern for all config validators; v1.7 activates the no-op `TenantVectorOptionsValidator`
- `System.Collections.Frozen` (BCL) — FrozenDictionary volatile-swap in all registries; no changes to pattern
- `CommunityStringHelper` (existing internal class) — authoritative `Simetra.` prefix validator; v1.7 adds `IsValidCommunityString` helper method, no changes to existing methods

**ValueType allowed strings:** `"Integer32"`, `"IpAddress"`, `"OctetString"` — these three
cover 100% of writable OIDs across both OBP (OTS3000-BPS) and NPB (CGS NPB-2E) target devices.
`Integer32` covers plain integers, enumerated integers (transmitted as Integer32 on the wire),
and TruthValue encoding. `IpAddress` covers OBP NMU network config fields. `OctetString`
covers hostname, description, and dBm-threshold string fields. The config uses Pascal-case
strings (`"Integer32"` not `"INTEGER32"`) validated at load time against a `HashSet` — not a
C# enum — consistent with how `MetricName` and `CommunityString` are treated throughout the
codebase.

See `.planning/research/STACK.md` for the complete OBP and NPB writable OID type inventories
and the SharpSnmpLib type-mapping table.

### Expected Features

**Must build — Table Stakes (10 features):**

- **TS-03** `Name` → `CommunityString` rename on `DeviceOptions` and `devices.json` — makes
  the SNMP credential explicit; removes the dual-purpose `Name` field ambiguity
- **TS-04** `DeviceInfo.Name` derived from `CommunityString` via `TryExtractDeviceName` —
  consumers (Quartz job keys, Prometheus labels, log context) are unchanged
- **TS-05** CommunityString validation rules — not-null/empty, `"Simetra."` prefix (Ordinal),
  non-empty suffix; uses `CommunityStringHelper.IsValidCommunityString` as the single source
- **TS-06** Invalid CommunityString — skip entry + Error log — soft degradation matching the
  existing `BuildPollGroups` unresolvable-name pattern; other entries in the same file are
  unaffected
- **TS-01** Tenant metric entry becomes self-describing — `Device` (string label) and
  `CommunityString` added to `MetricSlotOptions`; no DeviceRegistry lookup needed
- **TS-07** Remove `IDeviceRegistry` and `IOidMapService` from `TenantVectorRegistry` — poll
  interval comes from `MetricSlotOptions.IntervalSeconds` directly; DNS resolution must be
  explicitly replaced (see Gaps)
- **TS-02** Tenant `Commands[]` data model — new `CommandSlotOptions` class; `TenantOptions.Commands`
  list; `TenantCommand` runtime record; SET execution is out of scope
- **TS-08** `tenants.json` / `simetra-tenants` rename — atomic across C# constants, YAML data
  key, local dev config file, and E2E inline `.sh` script heredocs
- **TS-09** Unresolvable `MetricName` in tenant config — skip entry + Error log via
  `IOidMapService.ContainsMetricName()`
- **TS-10** Unresolvable `CommandName` in tenant config — store entry + Debug log; CommandMap
  is independently hot-reloaded; validation belongs at execution time

**Should build — Differentiators (2 features, low cost):**

- **D-01** `TenantVectorOptionsValidator` structural validation — replaces the current no-op;
  catches missing `Ip`, empty `MetricName`, out-of-range `Port` as a pre-flight blocker
- **D-03** Structured log fields on CommunityString skip events — `EntryType`, `EntryIndex`,
  `InvalidValue`, `ValidationRule`, `ConfigMap` — enables Loki alerting on config errors

**Evaluate before committing (2 low-complexity additions):**

- **D-02** Optional `IntervalSeconds` on `MetricSlotOptions` — informational storage for
  operations dashboard; not needed for routing correctness
- **D-04** Optional `Name` on `TenantOptions` — readable tenant identity in logs; cosmetic,
  defer if schedule is tight

**Explicitly out of scope (7 anti-features):**
AF-01 (SNMP SET execution), AF-02 (cross-validate CommunityString vs DeviceRegistry),
AF-03 (reverse CommunityString lookup in DeviceRegistry), AF-04 (validate `Device` field
against DeviceRegistry), AF-05 (dual ConfigMap watch for backward compat), AF-06 (CommandName
validation against CommandMap at load time), AF-07 (community string auto-discovery).

See `.planning/research/FEATURES.md` for the full dependency graph and edge-case tables.

### Architecture Approach

v1.7 makes no architectural changes to the SNMP polling pipeline. The routing key shape
`(ip, port, metricName)`, the FrozenDictionary volatile-swap pattern, and the MediatR
behavior chain are all unchanged. The fan-out behavior retains its `IDeviceRegistry` dependency
for port lookup — removing that coupling requires a routing key model change and is explicitly
deferred to a future milestone.

**Components changed:**

1. `MetricSlotOptions` — gains `Device`, `CommunityString`, optionally `IntervalSeconds`
2. `TenantOptions` — gains `List<CommandSlotOptions> Commands`
3. `CommandSlotOptions` *(new)* — `Device`, `Ip`, `Port`, `CommunityString`, `CommandName`,
   `Value`, `ValueType`
4. `TenantCommand` *(new runtime record)* — immutable; carried by `Tenant.Commands`
5. `TenantVectorOptionsValidator` — no-op → real structural validator
6. `DevicesOptionsValidator` — gains CommunityString pattern check in `ValidateDevice()`
7. `TenantVectorRegistry` — removes `IDeviceRegistry` + `IOidMapService` constructor deps;
   reads `IntervalSeconds` from config; builds `Tenant` with commands list
8. `Tenant` — gains `IReadOnlyList<TenantCommand> Commands`
9. `DeviceRegistry.BuildPollGroups` — skips groups where zero OIDs resolve (log warning)
10. `TenantVectorWatcherService` — `ConfigMapName` and `ConfigKey` constants updated
11. K8s ConfigMap YAML, local dev config file, E2E fixtures — file/key renames

**Unchanged hot path:** `TenantVectorFanOutBehavior`, `MetricSlotHolder`, `RoutingKey`,
`PriorityGroup`, `ITenantVectorRegistry`, all MediatR behaviors, `OidMapService`,
`CommandMapService`, all watcher services other than `TenantVectorWatcherService`.

See `.planning/research/ARCHITECTURE.md` for the full component change map, data flow
diagrams, and the build-order analysis.

### Critical Pitfalls

**Top 5 v1.7-specific pitfalls (from PITFALLS.md v1.7 addendum):**

1. **ConfigKey constant mismatch kills initial load silently (Pitfall A — CRITICAL)** —
   If `ConfigKey = "tenantvector.json"` is not updated atomically with the YAML data key
   rename, the watcher logs "skipping reload" and leaves the registry with zero tenants.
   No compile error, no runtime exception — just a permanent routing miss. Prevention: single
   atomic commit for C# constant + YAML data key + local dev file + E2E `.sh` heredocs
   (grep `.sh` files explicitly — `*.yaml`-only grep misses inline fixtures).

2. **DNS name in routing index vs resolved IP in SNMP packets (Pitfall C — CRITICAL)** —
   Removing `_deviceRegistry` from `TenantVectorRegistry` without replacing `ResolveIp()`
   builds routing index keys from DNS strings; incoming packets arrive with IPs. `TryRoute`
   misses on every call. `snmp_tenantvector_routed_total` stays at zero with no error log.
   Prevention: replace `ResolveIp()` with direct `Dns.GetHostAddressesAsync` in `Reload()`,
   OR require IP-only in tenant config. Decision must be made in Phase 4 planning and covered
   by an integration test.

3. **CommunityString validator diverges from runtime extraction (Pitfall F — CRITICAL)** —
   A standalone regex for the `Simetra.` prefix check will diverge from
   `CommunityStringHelper.TryExtractDeviceName` on edge cases (lowercase prefix, trailing
   space, empty suffix). Prevention: the validator must call
   `CommunityStringHelper.IsValidCommunityString` directly — never a parallel regex.

4. **Zero-OID poll group still schedules a Quartz job (Pitfall D — CRITICAL)** —
   `BuildPollGroups` currently returns `MetricPollInfo` even for empty OID lists;
   `DynamicPollScheduler` schedules them; SharpSnmpClient behavior with an empty GET is
   undefined. Prevention: implement the skip guard before any downstream phase assumes the
   behavior; unit-test with an all-unresolvable-names device.

5. **Carry-over key mismatch resets time-series history on DNS re-resolution (Pitfall H — MODERATE)** —
   If `oldSlotLookup` keys are resolved IPs and a new reload resolves the same DNS name to a
   different IP, `carried_over = 0` and all time-series history is discarded silently.
   Prevention: key carry-over on the raw config address (before DNS resolution), not the
   resolved IP.

**Also important:** Pitfall B (silent null deserialization when old ConfigMaps still use
`"Name"` in community string position — grep config files before deploying the rename),
Pitfall E (rolling deployment format divergence — apply ConfigMap before rollout restart),
Pitfall G (Value/ValueType mismatch silent at load time — consider parse-attempt validation),
Pitfall I (E2E `.sh` inline heredocs use old data key — missed by yaml-only grep).

---

## Implications for Roadmap

Based on the combined research, the natural phase structure has 6 phases ordered by
dependency and blast-radius isolation.

### Phase 1: Config Model Additions

**Rationale:** Everything else depends on model shapes. `CommandSlotOptions`, `TenantCommand`,
and the new fields on `MetricSlotOptions` and `TenantOptions` must exist before validators,
registry code, or any test can reference them. This phase is purely additive — all new fields
default to empty/null/0, backward-compatible with existing configs.
**Delivers:** `CommandSlotOptions.cs` (new), `TenantCommand.cs` (new runtime record),
`MetricSlotOptions` + `Device` + `CommunityString` + optional `IntervalSeconds`,
`TenantOptions` + `Commands` list.
**Addresses:** TS-01 (partial), TS-02
**Avoids:** Nothing can break — all additions are backward-compatible.
**Research flag:** Standard patterns. No additional research needed.

### Phase 2: Validators

**Rationale:** Validators depend on Phase 1 models. Must be activated and unit-tested before
the registry refactor so the guard point is verified before any behavioral changes land.
Activating `TenantVectorOptionsValidator` is a behavior change — tests must confirm that
structurally-invalid configs now get rejected rather than silently passed through.
**Delivers:** `TenantVectorOptionsValidator` (no-op → structural validator for Ip, Port,
MetricName, CommunityString, CommandName); `DevicesOptionsValidator` + CommunityString
pattern check; `CommunityStringHelper.IsValidCommunityString()` added.
**Addresses:** TS-05, TS-06, D-01, D-03
**Avoids:** Pitfall F (use `IsValidCommunityString` not a regex), Pitfall G (consider
parse-attempt for Value/ValueType at this stage)
**Research flag:** Standard patterns. `DevicesOptionsValidator` is the exact template.

### Phase 3: DeviceRegistry Poll Group Skip

**Rationale:** Fully independent of all other v1.7 changes. A single method change in
`BuildPollGroups` with a focused unit test. Must be complete before Phase 4 because the
`TenantVectorRegistry` refactor testing exercises the full device/tenant reload interaction,
and the skip behavior is part of the expected device-side contract.
**Delivers:** `DeviceRegistry.BuildPollGroups` returns no `MetricPollInfo` for groups where
all metric names are unresolvable; Warning log per skipped group; unit test confirms
all-unresolvable-names device produces zero poll groups.
**Addresses:** TS-09 (poll group side), Pitfall D
**Avoids:** Pitfall D (prevents empty-OID Quartz jobs from being scheduled and firing)
**Research flag:** Standard. 5-line change with one unit test.

### Phase 4: TenantVectorRegistry Refactor

**Rationale:** Depends on Phase 1 (models must exist) and Phase 2 (validators confirmed
working). This is the most behavior-changing phase: removes `IDeviceRegistry` and
`IOidMapService` constructor dependencies, reads `IntervalSeconds` from config, adds the
commands build loop, and updates `Tenant`. The DNS resolution decision (see Gaps) must be
resolved before coding begins — it determines whether `Reload()` becomes async.
**Delivers:** `TenantVectorRegistry` with no DeviceRegistry/OidMapService deps; `Tenant`
with `IReadOnlyList<TenantCommand> Commands`; updated DI registration in
`ServiceCollectionExtensions`.
**Addresses:** TS-01, TS-07, TS-02
**Avoids:** Pitfall C (DNS replacement explicit), Pitfall H (carry-over keyed on raw config
address not resolved IP)
**Research flag:** The DNS resolution strategy (async `Reload()` with `Dns.GetHostAddressesAsync`
vs IP-only tenant config) must be decided as a named plan-time decision. An integration test
covering a DNS-name tenant entry is required to close the phase.

### Phase 5: DeviceOptions Name → CommunityString Rename

**Rationale:** Logically independent of the tenant-side changes (Phases 1-4) but must be a
single atomic commit covering all config files. Placing it after Phase 4 means the registry
refactor is already stable before this rename introduces churn in `DeviceRegistry` and
`MetricPollJob`. The rename of `DeviceOptions.Name` to `CommunityString` also requires
`DeviceInfo.Name` to be derived via `TryExtractDeviceName` rather than copied directly.
**Delivers:** `DeviceOptions.CommunityString` as the explicit credential field (not `Name`);
`DeviceInfo.Name` extracted via `TryExtractDeviceName`; all `devices.json` config files and
K8s device ConfigMap fixtures updated atomically.
**Addresses:** TS-03, TS-04
**Avoids:** Pitfall B (grep all config files and fixtures for old `"Name"` property in
community string context before merging; verify no fixture still uses the old name)
**Research flag:** Note from ARCHITECTURE.md: `DeviceOptions.CommunityString` already exists
as a property in the current code. The rename is specifically about removing the old `Name`
field that was used as both identity and community string seed. Confirm the exact scope during
plan creation.

### Phase 6: ConfigMap Rename (tenantvector.json → tenants.json)

**Rationale:** Purely mechanical. Zero logic changes. Placed last to keep all prior phase
diffs clean. Must be deployed as a single atomic unit: ConfigMap update first, then
`kubectl rollout restart` (never the reverse).
**Delivers:** `TenantVectorWatcherService.ConfigMapName = "simetra-tenants"`,
`ConfigKey = "tenants.json"`; local dev `config/tenants.json`; K8s YAML file renamed and
data key updated; `TenantVectorOptions.SectionName` decision applied; E2E scenario `.sh`
files updated including inline heredoc fixtures.
**Addresses:** TS-08
**Avoids:** Pitfall A (constant + YAML + config + `.sh` inline heredocs all in one commit),
Pitfall I (grep `.sh` files specifically — `*.yaml` grep will miss inline fixtures),
Pitfall E (document deployment order in runbook: apply ConfigMap before rollout restart)
**Research flag:** Confirm the `TenantVectorOptions.SectionName` decision (keep `"TenantVector"`
for the IConfiguration binding key, or also rename to `"Tenants"`) as a named decision in
the phase plan before touching any files (see Gaps).

### Phase Ordering Rationale

- **Models before validators:** validators reference model properties — won't compile otherwise.
- **Validators before registry refactor:** the guard point must be verified before behavioral
  changes land; regressions surface earlier.
- **Poll group skip independent:** no cross-dependencies; keeps the diff reviewable in isolation.
- **Registry refactor before renames:** avoids mixing logic changes with mechanical renames in
  the same diff.
- **Device rename before ConfigMap rename:** both are mechanical but operate on different files;
  separate commits isolate failure modes and simplify rollback.
- **ConfigMap rename last:** highest operational risk (rolling deployment window with format
  divergence); standalone commit makes rollback to the previous tag trivial.

### Research Flags

**Needs a plan-time decision before coding (not additional external research):**

- **Phase 4 (TenantVectorRegistry refactor):** DNS resolution strategy is the only unresolved
  design question in the entire milestone. Two options: (a) make `Reload()` async with direct
  `Dns.GetHostAddressesAsync` (call site in watcher changes), or (b) require IP-only in
  `MetricSlotOptions.Ip` (existing DNS-name entries in tenant config must be updated). Both
  are implementable in Phase 4; the decision determines scope. Record as a named decision in
  the phase plan.

- **Phase 6 (ConfigMap rename):** `TenantVectorOptions.SectionName` scope — keep `"TenantVector"`
  (internal binding key unchanged, only file/ConfigMap artifacts rename) or also rename to
  `"Tenants"` (simpler JSON for operators but changes the IConfiguration binding chain).
  Simpler path: keep `"TenantVector"`. Record as a named decision.

**Standard patterns (no additional research needed):**

- **Phase 1 (Config models):** Additive POCO extension. Template: `MetricSlotOptions` and
  `TenantOptions` already exist; just extend them.
- **Phase 2 (Validators):** `DevicesOptionsValidator` is the exact template to follow.
- **Phase 3 (Poll group skip):** 5-line change in `BuildPollGroups`; pattern is in the method
  already (the warn-and-skip logic for individual OIDs).
- **Phase 5 (Device rename):** Mechanical with a focused diff; `CommunityStringHelper` already
  provides the extraction method.
- **Phase 6 (ConfigMap rename):** Mechanical. Pitfalls A and I are the only risks; both have
  clear, specific mitigations.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All decisions derived from direct codebase reads: SharpSnmpLib version, CommunityStringHelper source, existing model shapes, existing validators. Complete OBP and NPB writable OID inventories verified from Docs/ analysis files. Zero training-data inference. |
| Features | HIGH | All 10 table-stakes features traced to specific source file lines. Feature scope is concrete and bounded. The only scope ambiguity is TS-03 (Name → CommunityString rename) — ARCHITECTURE.md notes the property already exists with the correct name; the rename is about removing the old `Name` field, not adding `CommunityString`. |
| Architecture | HIGH | Component inventory verified by reading every named file. Routing key shape, carry-over logic, fan-out behavior, DI registration — all confirmed. One design decision deferred (DNS resolution strategy). |
| Pitfalls | HIGH | All 9 v1.7 pitfalls derived from direct source inspection of constants, carry-over logic, routing index build logic, and E2E `.sh` files. No speculation. |

**Overall confidence: HIGH**

### Gaps to Address

1. **DNS resolution strategy in `TenantVectorRegistry.Reload()`** — ARCHITECTURE.md Open
   Question 1 is unresolved. Two options: async DNS in `Reload()` or require IPs in tenant
   config. Impact: if async, the watcher call site changes; if IP-only, all DNS-name entries
   in existing tenant config must be updated. Either is implementable. Must be recorded as
   a named decision before Phase 4 begins.

2. **`TenantVectorOptions.SectionName` with rename** — ARCHITECTURE.md Open Question 3 is
   unresolved. Simpler path: keep `SectionName = "TenantVector"` (only ConfigMap/file names
   change). Changing it to `"Tenants"` requires updating the IConfiguration binding chain.
   Recommend keeping it, but this must be a named decision in Phase 6 plan.

3. **Value/ValueType parse validation scope** — PITFALLS.md Pitfall G recommends parsing
   `Value` as `ValueType` at load time. FEATURES.md TS-02 says "Value validation belongs to
   command execution." These conflict. Recommend resolving at plan time: parse-attempt at
   load gives operators early error discovery; deferring to execution simplifies the validator.
   Since SET execution is out of scope in v1.7, load-time parseability checking is the safer
   choice — operators see config errors at config load, not at a future execution invocation.

4. **TS-03 rename scope clarification** — ARCHITECTURE.md flags (LOW confidence) that
   `DeviceOptions.CommunityString` already exists in the current code; the rename target is
   specifically the `Name` field that was previously used as both device identity and community
   string seed. Verify the exact rename surface before Phase 5 planning (which fields change,
   which consumers change, whether DeviceInfo changes).

---

## Sources

### Primary — HIGH Confidence (direct codebase reads)

- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `Reload()`, `ResolveIp()`,
  `DeriveIntervalSeconds()`, carry-over logic, volatile swap
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — `BuildPollGroups()`, zero-OID group
  behavior, `_byName` index
- `src/SnmpCollector/Pipeline/CommunityStringHelper.cs` — exact `Simetra.` prefix rule,
  Ordinal comparison, length guard
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — DeviceRegistry
  port lookup; routing key formation
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — current fields confirmed
- `src/SnmpCollector/Configuration/TenantOptions.cs` — current shape confirmed
- `src/SnmpCollector/Configuration/TenantVectorOptions.cs` — `SectionName = "TenantVector"` confirmed
- `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` — no-op confirmed
- `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` — existing pattern;
  no CommunityString check confirmed
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — `ConfigMapName` and `ConfigKey` constants
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — CommunityString selection logic
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` — CommunityString null fallback
- `src/SnmpCollector/Configuration/DeviceOptions.cs` — `CommunityString` property already
  named correctly in current code; `Name` still present as separate identity field
- `src/SnmpCollector/SnmpCollector.csproj` — SharpSnmpLib 12.5.7 version confirmed
- `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` — data key `tenantvector.json` confirmed
- `tests/e2e/scenarios/28-tenantvector-routing.sh` — inline fixture using old key name (Pitfall I source)
- `tests/e2e/fixtures/device-added-configmap.yaml` — already uses `CommunityString` correctly
- `Docs/OBP-Device-Analysis.md` — complete writable OID type inventory for OBP (Section 5)
- `Docs/NPB-Device-Analysis.md` — complete writable OID type inventory for NPB (Section 5)
- `.planning/phases/31-human-name-device-config/31-CONTEXT.md` — zero-OID skip design decision confirmed deferred

---

*Research completed: 2026-03-14*
*Ready for roadmap: yes*
