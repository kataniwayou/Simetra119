# Phase 24: Watcher Resilience and Comprehensive Report - Context

**Gathered:** 2026-03-09
**Status:** Ready for planning

<domain>
## Phase Boundary

Verify ConfigMap watcher error handling (invalid JSON, reconnection) via pod logs, and generate a comprehensive E2E report with pass/fail evidence for all test scenarios. No SnmpCollector code modifications.

</domain>

<decisions>
## Implementation Decisions

### Watcher Log Verification (WATCH-01, WATCH-02)
- **kubectl logs grep** — scenarios apply a known ConfigMap change, then grep pod logs for watcher reload messages
- **Reuse Phase 23 fixtures** — apply oid-renamed-configmap.yaml for WATCH-01 and device-added-configmap.yaml for WATCH-02 (already proven to work)
- **Two separate scenarios** — 24-oidmap-watcher-log.sh and 25-device-watcher-log.sh, one per watcher requirement
- **Claude inspects source** for exact log patterns — researcher reads OidMapWatcherService.cs and DeviceWatcherService.cs to find the actual log messages emitted on reload
- **Snapshot/restore isolation** — same per-scenario pattern as Phase 23

### Invalid JSON / Error Resilience (WATCH-03)
- **Both malformed types** — test syntactically invalid JSON (broken syntax) AND valid JSON with wrong schema (unexpected structure)
- **Both ConfigMaps** — test invalid JSON in both simetra-oidmaps and simetra-devices to prove both watchers handle errors gracefully
- **One combined scenario** — 26-invalid-json.sh tests both ConfigMaps sequentially (same pattern, no need for two files)
- **kubectl get pods + log grep** — verify all snmp-collector pods are still Running AND grep logs for error messages (proves graceful handling, not silent ignore)
- **New fixture files** — static YAML fixtures with invalid JSON content for each ConfigMap

### Watcher Reconnection (WATCH-04)
- **Log-based evidence only** — grep existing pod logs for reconnection/retry messages from natural K8s watch expiry (no chaos testing)
- **Separate scenario** — 27-watcher-reconnect.sh, dedicated for reconnection log evidence
- **Pass with caveat** — if no reconnection events found in current pod lifetime, record_pass with evidence noting watcher has retry logic in source code but no reconnection events observed

### Final Report (INFRA-03, RPT-01)
- **Markdown file** — generate tests/e2e/REPORT.md at the end of run-all.sh execution
- **Pass/fail + evidence string** — each scenario shows name, PASS/FAIL status, and the evidence string from record_pass/record_fail
- **Integrated into run-all.sh** — report generation happens at end of test execution, single flow
- **Grouped by category** — sections like "Pipeline Counters (01-10)", "Business Metrics (11-17)", "OID Mutations (18-20)", "Device Lifecycle (21-23)", "Watcher Resilience (24-27)"

### Scenario Numbering
- 24: OID map watcher log verification
- 25: Device watcher log verification
- 26: Invalid JSON resilience (both ConfigMaps)
- 27: Watcher reconnection (log-based)

</decisions>

<specifics>
## Specific Ideas

- Scenario 26 should test both syntactically invalid JSON (e.g., missing closing brace) and schema-invalid JSON (e.g., string where array expected) as sub-assertions within the single scenario
- Report categories map to E2E phases: Pipeline Counters → Phase 21, Business Metrics → Phase 22, OID Mutations → Phase 23, Device Lifecycle → Phase 23, Watcher Resilience → Phase 24
- WATCH-04 is inherently limited without chaos testing — the report should document this limitation honestly

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 24-watcher-resilience-and-comprehensive-report*
*Context gathered: 2026-03-09*
