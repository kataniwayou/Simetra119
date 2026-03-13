# Phase 31: Human-Name Device Config - Context

**Gathered:** 2026-03-13
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace raw OID strings in devices.json with human-readable metric names resolved via the OID map. Full rename of config structure (MetricPolls → Polls, Oids → Names) and C# models. Rewrite devices.json, K8s ConfigMap, and all E2E scenarios. No cross-watcher triggering — each ConfigMap reloads independently.

</domain>

<decisions>
## Implementation Decisions

### Migration strategy
- **Full replacement, not coexistence.** The `Oids[]` field is removed entirely. `Names[]` is the only field. All entries are metric names resolved via `IOidMapService.ResolveToOid` at config load time.
- **Rename config structure:** `MetricPolls` → `Polls`, `Oids` → `Names` in JSON. Future-proofs for `Commands[]` as a sibling field.
- **Rename C# models:** `MetricPollOptions` → `PollOptions`, property `Oids` → `Names`. Full rename through all code references (DeviceWatcherService, MetricPollJob, tests, etc.).
- **Rewrite devices.json now:** Translate all current OID entries to their human-readable metric names using the OID map as part of this phase. Ship a working config.
- **Rewrite K8s ConfigMap:** Both local `config/devices.json` and the K8s `simetra-devices` ConfigMap manifest are rewritten to use metric names.
- **Update all E2E scenarios:** Every E2E scenario that touches device config is updated to use metric names.
- **Config-level only:** Simulators and SNMP protocol still use OIDs on the wire. Name → OID resolution happens at device config load time, before any SNMP traffic.

### Unresolvable name handling
- **Per-name resolution, no group-level logic.** Each name resolves independently. Resolved name → included in poll job. Unresolved name → excluded, warning log.
- **Poll group behavior:** Within a poll group, resolved names are collected into one poll job (same `IntervalSeconds`). Unresolved names are excluded from that job. If zero names resolve in a group, no job for that group.
- **Device always registered:** Even if ALL poll groups have zero resolved names, the device is still registered in DeviceRegistry (needed for traps).
- **Simple warning log:** "Metric name 'X' not found in OID map for device Y" — no fuzzy matching, no suggestions.
- **Per-name detail in reload diff:** Log each resolved and unresolved name, not just summary counts. E.g., "OBP-01 group 10s: resolved 20/22, unresolved: obp_chanl_L1, obp_typo"

### OID map hot-reload cascade
- **No cross-watcher triggering.** Each ConfigMap watcher is independent. OID map reload does NOT trigger device config re-resolution.
- **Point-in-time resolution:** When device config reloads, it resolves names against whatever the current OID map state is at that moment.
- **Operator responsibility:** Operator is responsible for aligning OID map and device config. If they update the OID map, they need to trigger a device config reload for changes to take effect.

### Raw OID detection
- **No special detection.** If a raw OID string (digits and dots) appears in `Names[]`, it's treated as a metric name, goes through `ResolveToOid`, fails, and gets the standard "not found" warning. No special OID-pattern detection or hint. Keep it simple.

### Claude's Discretion
- Internal implementation of name resolution in DeviceWatcherService
- Exact structured log format (event ID, property names)
- How to organize the mechanical rename across the codebase
- Test structure for name resolution logic

</decisions>

<specifics>
## Specific Ideas

- The rename from MetricPolls/Oids to Polls/Names is designed to be future-proof: `Polls` (read operations) and future `Commands` (write operations) sit as siblings under a device.
- Resolution is config-level only — the SNMP wire protocol still uses OIDs. The translation happens once at config load, not per-poll.

</specifics>

<deferred>
## Deferred Ideas

- Tenant vector config validation against OID map — separate concern, not in scope for device config phase.
- Fuzzy matching / "did you mean?" suggestions for unresolvable names — keep it simple for now.

</deferred>

---

*Phase: 31-human-name-device-config*
*Context gathered: 2026-03-13*
