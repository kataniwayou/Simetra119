# Phase 30: OID Map Integrity - Context

**Gathered:** 2026-03-13
**Status:** Ready for planning

<domain>
## Phase Boundary

Add duplicate validation (OID keys and metric name values) and a reverse index (name → OID) to OidMapService. No new services — modifications to existing OidMapService, IOidMapService, and OidMapWatcherService only.

</domain>

<decisions>
## Implementation Decisions

### Duplicate handling policy
- **Duplicate OIDs:** When the same OID key appears more than once, SKIP BOTH entries. Neither mapping is loaded. The OID resolves to "Unknown". Log structured warning per duplicate.
- **Duplicate names:** When the same metric name maps to two different OIDs, SKIP BOTH entries. Neither OID gets that name. Both resolve to "Unknown". Log structured warning per duplicate.
- **Consistent rule:** Any ambiguity = exclude both conflicting entries + warning log. Operator must fix the conflict.

### Validation timing & scope
- **Validate BEFORE heartbeat seed merge.** Duplicate detection runs only on operator-supplied entries. The heartbeat seed is injected after validation, guaranteed clean. This prevents operators from accidentally conflicting with the heartbeat OID/name.
- **Empty map after validation:** If all entries are duplicates and nothing survives, load the empty map (plus heartbeat seed). Log at ERROR level since this is almost certainly a broken config. Do NOT reject the reload — the map becomes effectively empty and all OIDs resolve to "Unknown".

### Cross-map awareness
- **Same OID in both oidmaps and commandmaps: ALLOWED.** Many OBP OIDs are read-write — the same OID is both a polled metric and a SET command. Each map names it independently.
- **Same human name in both maps: ALLOWED.** The two maps are independent namespaces. No cross-validation between oidmaps and commandmaps.
- **No runtime enforcement of naming conventions.** Command map naming (e.g., "set_" prefix) is operator convention, not system-enforced.

### Claude's Discretion
- Exact structured log format (event ID, property names)
- Whether to use JsonDocument or custom parsing for duplicate OID key detection (Dictionary silently deduplicates)
- Internal implementation of the reverse index rebuild alongside forward map

</decisions>

<specifics>
## Specific Ideas

- The "skip both" policy means the operator gets a clear signal: if you see a duplicate warning, the affected entries are NOT in the map. No guessing about which one won.
- Heartbeat seed is always safe because it's injected after validation, never subject to duplicate checks.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 30-oid-map-integrity*
*Context gathered: 2026-03-13*
