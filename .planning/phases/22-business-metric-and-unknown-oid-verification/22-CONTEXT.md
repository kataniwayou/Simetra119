# Phase 22: Business Metric and Unknown OID Verification - Context

**Gathered:** 2026-03-09
**Status:** Ready for planning

<domain>
## Phase Boundary

Verify the full SNMP-to-Prometheus data path: snmp_gauge and snmp_info metrics carry correct labels and values, unmapped OIDs are classified as metric_name="Unknown", and trap-originated metrics appear with correct device_name. Build ConfigMap snapshot/restore utility for safe mutation testing (reused by Phases 23-24). No SnmpCollector code modifications.

</domain>

<decisions>
## Implementation Decisions

### Gauge & Info Verification
- **Devices: All three (E2E-SIM, OBP-01, NPB-01)** — maximum coverage across all device types
- **Depth: Labels + values** — verify metric_name, device_name, oid, snmp_type labels AND check numeric values are reasonable (non-zero, within range)
- **snmp_info: Label exists with non-empty value** — check the value label is present and not empty, don't assert exact string
- **snmp_type: Verify all snmp_type values** — E2E-SIM has 5 different SNMP types, verify each appears with correct snmp_type label

### Unknown OID Classification
- **Query approach: Query metric_name="Unknown", check OIDs present** — query snmp_gauge{metric_name="Unknown"} and verify E2E-SIM unmapped OIDs appear in results
- **Full label check** — even unknown OIDs should have correct device_name=E2E-SIM and correct snmp_type labels
- **Coverage: Both unmapped OIDs** — verify both .999.2.1.0 (gauge-type) and .999.2.2.0 (string-type) get classified as Unknown

### Trap-Originated Metrics
- **Verification: Check snmp_gauge{device_name="E2E-SIM"} from trap source** — query metrics with E2E-SIM device name and verify trap-originated OIDs appear
- **Differentiate by source** — verify that specific trap OIDs appear as metrics, proving traps flow through the full pipeline
- **Dedicated trap metric scenario** — separate scenario focused on proving trap-to-Prometheus path works

### ConfigMap Snapshot/Restore
- **Scope: Both oidmaps + devices** — backup and restore both simetra-oidmaps and simetra-devices ConfigMaps
- **Storage: Temp files in fixtures/** — save to tests/e2e/fixtures/.original-*.yaml (dot-prefixed, gitignored)
- **Lifecycle: Wrap all mutation scenarios** — snapshot before first mutation, restore after last
- **Claude's discretion: Location** — Claude decides whether to add to lib/kubectl.sh or create new lib/configmap.sh

</decisions>

<specifics>
## Specific Ideas

- E2E-SIM has deterministic static values for all OIDs — use these for exact value verification where possible
- E2E-SIM unmapped OIDs: .999.2.1.0 (gauge type, value 99999) and .999.2.2.0 (OctetString type, value "unmapped_string_value")
- Trap OIDs from E2E-SIM include the same mapped OIDs sent as varbinds — these should appear as snmp_gauge metrics
- Phase 21 already has save_configmap/restore_configmap in lib/kubectl.sh — extend or wrap these for the snapshot/restore utility

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 22-business-metric-and-unknown-oid-verification*
*Context gathered: 2026-03-09*
