# Phase 51: Simulator HTTP Control Endpoint - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Add an HTTP scenario control endpoint to the existing pysnmp E2E simulator so test scripts can switch OID return values mid-test without restarting the pod. Register 6 new test-purpose OIDs. Existing E2E scenarios must continue to pass. The simulator accepts SNMP SET on the command target OID.

</domain>

<decisions>
## Implementation Decisions

### Scenario design
- Scenarios hardcoded in Python as dicts in the source file (no external YAML/JSON)
- Existing 9 OID values can be adjusted in the default scenario as long as existing E2E tests still pass
- Claude's discretion: scenario data structure (full dict per scenario vs. modifier on baseline) and naming convention

### OID inventory
- 6 new test OIDs total:
  - 1 evaluate metric
  - 2 resolved metrics (one is the command response OID, one represents bypass status)
  - 1 command target OID (accepts SNMP SET, returns the set value on subsequent GET)
  - 2 aggregate source OIDs (for ADV-01 aggregate evaluate test)
- Claude's discretion: SNMP type for evaluate metric (gauge recommended for simplest threshold testing)
- Claude's discretion: whether aggregate source OIDs are dedicated or reuse existing

### HTTP API shape
- Hardcoded port 8080 (not configurable via env var)
- Claude's discretion: JSON vs plain text responses (JSON recommended for extensibility with jq parsing)
- Claude's discretion: whether to include GET /scenarios (list available) endpoint
- Claude's discretion: error handling for unknown scenario names (404 recommended for fail-fast in tests)

### Staleness mechanism
- SNMP error response for specific OIDs only (not all OIDs) in the stale scenario
- Other OIDs continue to respond normally during staleness
- Claude's discretion: error type (noSuchObject vs genErr — pick what the collector's poll job handles cleanly)

### Claude's Discretion
- Scenario data structure and naming convention
- SNMP types for new OIDs
- HTTP response format (JSON vs text)
- Error type for staleness (noSuchObject vs genErr)
- aiohttp integration pattern with pysnmp asyncio loop

</decisions>

<specifics>
## Specific Ideas

- The command target OID must accept SNMP SET and update its value — this proves the SET round-trip E2E, not just scenario-controlled value switching
- Staleness is per-OID, not per-scenario — the stale scenario marks specific OIDs as error-returning while others continue normally
- Default scenario must reproduce pre-HTTP behavior for zero regression

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 51-simulator-http-control-endpoint*
*Context gathered: 2026-03-17*
