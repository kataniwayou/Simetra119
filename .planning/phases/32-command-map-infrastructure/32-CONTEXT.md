# Phase 32: Command Map Infrastructure - Context

**Gathered:** 2026-03-13
**Status:** Ready for planning

<domain>
## Phase Boundary

A command map lookup table operational with hot-reload. Operators load commandmaps.json via ConfigMap or local file. Code can resolve a command name to its SET OID or vice versa. Mirrors the OID map pattern (Phase 30). No SNMP SET execution — lookup only.

</domain>

<decisions>
## Implementation Decisions

### Config file format
- **Array-of-objects format**, identical structure to oidmaps.json: `[{ "Oid": "1.3.6...", "CommandName": "obp_set_bypass_L1" }]`
- **Field names:** `Oid` (same as oidmaps) and `CommandName` (parallel to MetricName)
- **ConfigMap:** Both separate `simetra-commandmaps.yaml` in `deploy/k8s/snmp-collector/` AND added as a section in `deploy/k8s/production/configmap.yaml` — mirrors how oidmaps and devices work
- **Seed data — 12 real commands:**
  - OBP bypass per link (4): `obp_set_bypass_L1` through `L4`, using control subtree `.4.1.0`
    - `1.3.6.1.4.1.47477.10.21.1.4.1.0` (L1)
    - `1.3.6.1.4.1.47477.10.21.2.4.1.0` (L2)
    - `1.3.6.1.4.1.47477.10.21.3.4.1.0` (L3)
    - `1.3.6.1.4.1.47477.10.21.4.4.1.0` (L4)
  - NPB counter reset per port (8): `npb_reset_counters_P1` through `P8`, using control subtree `.3.{port}.1.0`
    - `1.3.6.1.4.1.47477.100.3.1.1.0` (P1) through `1.3.6.1.4.1.47477.100.3.8.1.0` (P8)

### Command naming
- **snake_case convention** — matches metric name convention (obp_channel_L1, npb_cpu_util)
- **C# naming mirrors OidMap pattern exactly:**
  - `CommandMapService` + `ICommandMapService` (parallel to OidMapService/IOidMapService)
  - `CommandMapWatcherService` (parallel to OidMapWatcherService)
- **File naming mirrors oidmaps:**
  - `config/commandmaps.json` (local dev)
  - `simetra-commandmaps` ConfigMap name (already locked in STATE.md)

### Validation & error behavior
- **Identical 3-pass duplicate validation** to OidMapWatcherService: duplicate OIDs, duplicate command names, clean build. Same structured log format.
- **Own copy of validation logic** — CommandMapWatcherService gets its own `ValidateAndParseCommandMap` method, parallel pattern to `ValidateAndParseOidMap`. Keeps services independent (no shared generic parser).
- **Empty map is fine** — start with zero commands, log info "CommandMap loaded: 0 entries". No error, no warning. Commands are optional infrastructure until SNMP SET is implemented.

### Scope of lookups
- **Both directions:** forward `ResolveCommandName(oid)` → name, reverse `ResolveCommandOid(name)` → OID. Mirrors OidMapService pattern.
- **List method:** `GetAllCommandNames()` returns `IReadOnlyCollection<string>`. Useful for future command discovery/validation.
- **Count + Contains:** `int Count` property and `bool Contains(string commandName)` check on ICommandMapService.

### Claude's Discretion
- Internal implementation details (FrozenDictionary layout, volatile swap pattern — follow OidMapService precedent)
- Exact structured log event IDs and property names
- Test structure and organization
- Whether to extract shared constants (e.g., ConfigMap name constant)

</decisions>

<specifics>
## Specific Ideas

- The entire Phase 32 is a structural mirror of Phase 30 (OidMapService) + OidMapWatcherService. Same architecture, different data domain.
- OBP bypass commands use `.4.1.0` control subtree (parallel to `.3.1.0` status subtree for link_state reads)
- NPB counter reset commands use `100.3.{port}.1.0` control subtree (parallel to `100.2.{port}.{field}.0` status subtree)
- ConfigMap name "simetra-commandmaps" was pre-locked in STATE.md as an architectural decision

</specifics>

<deferred>
## Deferred Ideas

- SNMP SET execution using the command map — future milestone, not this phase
- Command parameter schemas / typed parameters — MIB-level knowledge, out of scope
- Command authorization / access control — no commands executed, nothing to authorize

</deferred>

---

*Phase: 32-command-map-infrastructure*
*Context gathered: 2026-03-13*
