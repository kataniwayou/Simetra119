# Phase 12: NPB OID Population - Research

**Researched:** 2026-03-07
**Domain:** SNMP OID mapping for NPB (Network Packet Broker) device, JSONC configuration authoring
**Confidence:** HIGH

## Summary

Phase 12 is a data-authoring phase: create `oidmap-npb.json` with 68 OID entries (4 system + 64 per-port) following the exact conventions established in Phase 11's `oidmap-obp.json`. No code changes are required -- the auto-scan infrastructure, `OidMapService`, and K8s ConfigMap plumbing were all built in Phase 11.

The user's decisions define a simplified OID tree structure under enterprise prefix `1.3.6.1.4.1.47477.100` with two subtrees: `.1` for system scalars and `.2` for per-port tabular data. This is a design-time simplification -- the real NPB MIB uses `1.3.6.1.4.1.47477.100.4` with deep subtrees, but the OID map uses a clean fictional structure consistent with the enterprise OID space (same approach as OBP).

The main work is: (1) assign sequential metric suffix IDs to system and per-port OIDs, (2) construct all 68 OID strings, (3) write JSONC documentation for each entry following the OBP precedent format, and (4) add the file to the K8s ConfigMap.

**Primary recommendation:** Author `oidmap-npb.json` following the exact structure and comment format of `oidmap-obp.json`, place it in `src/SnmpCollector/config/`, and add it as a key in `deploy/k8s/configmap.yaml` and `deploy/k8s/production/configmap.yaml`.

## Standard Stack

### Core

No new libraries or tools. This phase is pure configuration authoring.

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET `AddJsonFile` | 9.0 (bundled) | JSONC parsing + hot-reload | Already configured by Phase 11 auto-scan loop |
| `OidMapService` | In-project | OID-to-metric-name resolution | Already merges all `oidmap-*.json` files via .NET config layering |

### Supporting

None.

### Alternatives Considered

None -- file format and infrastructure are locked by Phase 11.

## Architecture Patterns

### Recommended File Location

```
src/SnmpCollector/config/
  oidmap-obp.json     # Existing (Phase 11)
  oidmap-npb.json     # New (Phase 12)
deploy/k8s/
  configmap.yaml      # Add oidmap-npb.json key
deploy/k8s/production/
  configmap.yaml      # Add oidmap-npb.json key (if exists)
```

### Pattern 1: OID Tree Structure (from CONTEXT decisions)

**What:** The NPB OID map uses enterprise prefix `1.3.6.1.4.1.47477.100` with two subtrees:
- System scalars: `1.3.6.1.4.1.47477.100.1.{metricId}.0`
- Per-port tabular: `1.3.6.1.4.1.47477.100.2.{portNum}.{metricId}.0`

**IMPORTANT -- Mismatch with simulator:** The existing NPB simulator (`simulators/npb/npb_simulator.py`) uses `1.3.6.1.4.1.47477.100.4` as its OID base prefix (following the real MIB hierarchy: cgs -> npb -> npb-2e). The CONTEXT decisions specify `47477.100.1/2` subtrees which are under the `npb` node, NOT under `npb-2e`. This is intentional -- the OID map is a simplified mapping that does not need to follow the real MIB structure. However, the planner must ensure either:
  1. The OID map uses the simplified tree from CONTEXT (and tests/verifier use those OIDs), OR
  2. The OID map uses the real simulator OIDs (and CONTEXT decisions are amended)

**Recommendation:** Follow the CONTEXT decisions exactly. The OID map is the source of truth for the Simetra collector. The simulator can be updated later if needed to align, or a separate mapping layer handles the translation. The Phase 11 OBP approach used OIDs that matched the simulator, but the CONTEXT decisions for Phase 12 are explicit and locked.

### Pattern 2: System OID Suffix Assignments (Claude's Discretion)

**What:** Assign sequential metricId suffixes for the 4 system-level OIDs.

**Recommended assignment:**

| metricId | Metric Name | OID | SNMP Type |
|----------|-------------|-----|-----------|
| 1 | `npb_cpu_util` | `1.3.6.1.4.1.47477.100.1.1.0` | OctetString |
| 2 | `npb_mem_util` | `1.3.6.1.4.1.47477.100.1.2.0` | OctetString |
| 3 | `npb_sys_temp` | `1.3.6.1.4.1.47477.100.1.3.0` | OctetString |
| 4 | `npb_uptime` | `1.3.6.1.4.1.47477.100.1.4.0` | OctetString |

**Rationale:** Sequential starting from 1. CPU and memory first (most commonly polled), then temperature, then uptime. All use OctetString because the real NPB MIB returns these as string values (see NPB-SYSTEM.mib: CPU load is `ConfdString`, memory is `String (Bytes)`, temperature sensors return `String (C)`, uptime is `String`).

### Pattern 3: Per-Port OID Suffix Assignments (Claude's Discretion)

**What:** Assign sequential metricId suffixes for the 8 per-port OIDs.

**Recommended assignment:**

| metricId | Metric | SNMP Type | Real MIB Source |
|----------|--------|-----------|-----------------|
| 1 | port_status | INTEGER | `portsPortStatusLinkStatus` (Enum) |
| 2 | rx_octets | Counter64 | `summaryPortRxOctets` |
| 3 | tx_octets | Counter64 | `summaryPortTxOctets` |
| 4 | rx_packets | Counter64 | `summaryPortRxPackets` |
| 5 | tx_packets | Counter64 | `summaryPortTxPackets` |
| 6 | rx_errors | Counter64 | `errorsPortRxErrPkts` |
| 7 | tx_errors | Counter64 | `errorsPortTxErrPkts` |
| 8 | rx_drops | Counter64 | `summaryPortRxDiscards` |

**Example OIDs for Port 1:**
```
1.3.6.1.4.1.47477.100.2.1.1.0  ->  npb_port_status_P1
1.3.6.1.4.1.47477.100.2.1.2.0  ->  npb_port_rx_octets_P1
1.3.6.1.4.1.47477.100.2.1.3.0  ->  npb_port_tx_octets_P1
...
1.3.6.1.4.1.47477.100.2.1.8.0  ->  npb_port_rx_drops_P1
```

**Example OIDs for Port 8:**
```
1.3.6.1.4.1.47477.100.2.8.1.0  ->  npb_port_status_P8
...
1.3.6.1.4.1.47477.100.2.8.8.0  ->  npb_port_rx_drops_P8
```

### Pattern 4: JSONC Comment Format (following OBP precedent)

**What:** Every OID entry gets a comment line above it documenting SNMP type, values/units, and range.

**OBP format (established in Phase 11):**
```jsonc
// SNMP type: INTEGER | Values: 1=active, 2=bypass, 3=fault | Range: 1-3
"1.3.6.1.4.1.47477.10.21.1.3.1.0": "obp_link_state_L1",
```

**NPB system OID comment examples:**
```jsonc
// SNMP type: OctetString | Units: load average (dimensionless) | Range: 0.0-4.0
"1.3.6.1.4.1.47477.100.1.1.0": "npb_cpu_util",

// SNMP type: OctetString | Units: bytes (total memory used) | Range: 0-8589934592
"1.3.6.1.4.1.47477.100.1.2.0": "npb_mem_util",

// SNMP type: OctetString | Units: degrees Celsius | Range: 0.0-95.0
"1.3.6.1.4.1.47477.100.1.3.0": "npb_sys_temp",

// SNMP type: OctetString | Units: human-readable duration string | Range: N/A
"1.3.6.1.4.1.47477.100.1.4.0": "npb_uptime",
```

**NPB per-port OID comment examples:**
```jsonc
// SNMP type: INTEGER | Values: 1=up, 2=down, 3=testing | Range: 1-3
"1.3.6.1.4.1.47477.100.2.1.1.0": "npb_port_status_P1",

// SNMP type: Counter64 | Units: bytes | Range: 0-2^64 (monotonic counter)
"1.3.6.1.4.1.47477.100.2.1.2.0": "npb_port_rx_octets_P1",

// SNMP type: Counter64 | Units: packets | Range: 0-2^64 (monotonic counter)
"1.3.6.1.4.1.47477.100.2.1.4.0": "npb_port_rx_packets_P1",

// SNMP type: Counter64 | Units: packets | Range: 0-2^64 (monotonic counter)
"1.3.6.1.4.1.47477.100.2.1.6.0": "npb_port_rx_errors_P1",

// SNMP type: Counter64 | Units: packets | Range: 0-2^64 (monotonic counter)
"1.3.6.1.4.1.47477.100.2.1.8.0": "npb_port_rx_drops_P1",
```

### Pattern 5: File Header Comment Block (following OBP precedent)

**What:** Device type comment block at top of file documenting enterprise prefix, OID tree structure, suffix map, and naming convention.

**Template:**
```jsonc
{
  // ===========================================================================
  // NPB (Network Packet Broker) OID Map
  // ===========================================================================
  //
  // Device type: NPB (Network Packet Broker, CGS NPB-2E)
  // Enterprise OID prefix: 1.3.6.1.4.1.47477.100
  //
  // OID tree structure:
  //   System scalars:  ...47477.100.1.{metricId}.0
  //   Per-port table:  ...47477.100.2.{portNum}.{metricId}.0
  //     portNum  = 1..8 (physical port)
  //     metricId = metric identifier (see suffix maps below)
  //
  // System suffix map:
  //    1 = cpu_util       OctetString  (load average, 0.0-4.0)
  //    2 = mem_util       OctetString  (memory used in bytes)
  //    3 = sys_temp       OctetString  (degrees Celsius)
  //    4 = uptime         OctetString  (human-readable duration)
  //
  // Per-port suffix map:
  //    1 = port_status    INTEGER  (1=up, 2=down, 3=testing)
  //    2 = rx_octets      Counter64  (received bytes, monotonic)
  //    3 = tx_octets      Counter64  (transmitted bytes, monotonic)
  //    4 = rx_packets     Counter64  (received packets, monotonic)
  //    5 = tx_packets     Counter64  (transmitted packets, monotonic)
  //    6 = rx_errors      Counter64  (receive errors, monotonic)
  //    7 = tx_errors      Counter64  (transmit errors, monotonic)
  //    8 = rx_drops       Counter64  (receive drops, monotonic)
  //
  // Naming convention: npb_{metric} (system), npb_port_{metric}_P{portNum} (per-port)
  // ===========================================================================

  "OidMap": {
    // ... entries ...
  }
}
```

### Anti-Patterns to Avoid

- **Don't omit the `"OidMap"` wrapper:** The file MUST have `"OidMap": { ... }` as the top-level section. Without it, `GetSection("OidMap").Bind()` finds nothing and the entries silently don't load. Phase 11 verifier caught this exact issue.
- **Don't use trailing commas:** JSONC supports comments but NOT trailing commas. The last entry in each section must NOT have a trailing comma.
- **Don't duplicate OID keys across files:** If `oidmap-obp.json` and `oidmap-npb.json` share any OID string, the later-loaded file (alphabetically) wins silently. NPB uses `47477.100.*` while OBP uses `47477.10.21.*` -- no collision possible.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OID map file format | New JSON schema | Copy `oidmap-obp.json` structure exactly | Consistency with existing file; proven to work with binding |
| Documentation format | Separate docs file | JSONC inline comments | Phase 11 decision: documentation lives with the OID entries |
| ConfigMap update | New ConfigMap | Add key to existing `simetra-config` ConfigMap | Phase 11 established the single-ConfigMap pattern |
| OID validation | Custom validator | Count entries (should be 68) + verify naming pattern | Simple count check catches most errors |

**Key insight:** This phase has zero code changes. It is purely a data/configuration authoring task. The risk is in getting the OID strings, metric names, and documentation right -- not in any infrastructure work.

## Common Pitfalls

### Pitfall 1: Missing `"OidMap"` Wrapper

**What goes wrong:** Writing entries at the JSON root level without the `"OidMap": { }` wrapper. The .NET config binding silently produces an empty dictionary.
**Why it happens:** Copy-paste error or forgetting the wrapper requirement.
**How to avoid:** Start from the OBP file as a template. Verify with `jq '.OidMap | length'` (should be 68 after stripping comments).
**Warning signs:** `OidMapService initialized with 24 entries` in logs (only OBP loaded) instead of 92 entries (24 OBP + 68 NPB).

### Pitfall 2: OID String Typos

**What goes wrong:** Transposed digits, missing dots, wrong port numbers in OID strings. These result in "Unknown" metric names at runtime.
**Why it happens:** 68 hand-authored OID strings with repetitive patterns.
**How to avoid:** Generate OIDs programmatically or use a structured approach (write system OIDs first, then copy-paste port blocks with systematic find-replace for port number). Verify with a script that checks OID uniqueness and pattern consistency.
**Warning signs:** `OID not found in OidMap` debug messages at runtime.

### Pitfall 3: Metric Name Inconsistency

**What goes wrong:** Using `npb_port_rxoctets_P1` instead of `npb_port_rx_octets_P1`, or `npb_port_status_p1` instead of `npb_port_status_P1`.
**Why it happens:** The naming convention has specific capitalization (`P` not `p`) and underscore placement.
**How to avoid:** Define the exact metric names upfront (see Pattern 3 above) and use them consistently.
**Warning signs:** Grafana dashboards show no data because metric name doesn't match expected pattern.

### Pitfall 4: Trailing Comma on Last Entry

**What goes wrong:** JSON parse error at startup because the last entry before `}` has a trailing comma.
**Why it happens:** Adding entries incrementally and forgetting to remove the comma on the last one.
**How to avoid:** JSONC supports `//` comments but NOT trailing commas. The .NET JSON parser will throw on trailing commas. Always check the last entry in each section.
**Warning signs:** `JsonException` in startup logs.

### Pitfall 5: Forgetting to Update K8s ConfigMap

**What goes wrong:** The file works in local dev but NPB OIDs resolve to "Unknown" in K8s because the ConfigMap was not updated.
**Why it happens:** The ConfigMap must have `oidmap-npb.json` as a key with the full file content.
**How to avoid:** Update both `deploy/k8s/configmap.yaml` and `deploy/k8s/production/configmap.yaml` (if separate).
**Warning signs:** Works locally, fails in K8s. Pod logs show only 24 OID map entries.

### Pitfall 6: Port Status Enum Mismatch

**What goes wrong:** Using the real NPB `LinkStatusType` enum values (which may be 0-based or use different codes) instead of the CONTEXT-specified ifOperStatus convention (1=up, 2=down, 3=testing).
**Why it happens:** The real NPB MIB may use different enum values than ifOperStatus.
**How to avoid:** Use the CONTEXT-specified values (1=up, 2=down, 3=testing) in the JSONC documentation. The OID map only maps OID to name -- it doesn't interpret values. The documentation is for humans.
**Warning signs:** Dashboard shows wrong port status interpretation.

## Code Examples

### Complete `oidmap-npb.json` Structure

Source: Derived from `oidmap-obp.json` pattern + Phase 12 CONTEXT decisions.

```jsonc
{
  // [header comment block -- see Pattern 5 above]

  "OidMap": {
    // ==== System Metrics ====

    // SNMP type: OctetString | Units: load average (dimensionless) | Range: 0.0-4.0
    "1.3.6.1.4.1.47477.100.1.1.0": "npb_cpu_util",

    // SNMP type: OctetString | Units: bytes (memory used) | Range: 0-8589934592
    "1.3.6.1.4.1.47477.100.1.2.0": "npb_mem_util",

    // SNMP type: OctetString | Units: degrees Celsius | Range: 0.0-95.0
    "1.3.6.1.4.1.47477.100.1.3.0": "npb_sys_temp",

    // SNMP type: OctetString | Units: human-readable duration | Range: N/A
    "1.3.6.1.4.1.47477.100.1.4.0": "npb_uptime",

    // ---- Port 1 ----

    // SNMP type: INTEGER | Values: 1=up, 2=down, 3=testing | Range: 1-3
    "1.3.6.1.4.1.47477.100.2.1.1.0": "npb_port_status_P1",

    // SNMP type: Counter64 | Units: bytes | Range: 0-2^64 (monotonic counter)
    "1.3.6.1.4.1.47477.100.2.1.2.0": "npb_port_rx_octets_P1",

    // SNMP type: Counter64 | Units: bytes | Range: 0-2^64 (monotonic counter)
    "1.3.6.1.4.1.47477.100.2.1.3.0": "npb_port_tx_octets_P1",

    // SNMP type: Counter64 | Units: packets | Range: 0-2^64 (monotonic counter)
    "1.3.6.1.4.1.47477.100.2.1.4.0": "npb_port_rx_packets_P1",

    // SNMP type: Counter64 | Units: packets | Range: 0-2^64 (monotonic counter)
    "1.3.6.1.4.1.47477.100.2.1.5.0": "npb_port_tx_packets_P1",

    // SNMP type: Counter64 | Units: packets | Range: 0-2^64 (monotonic counter)
    "1.3.6.1.4.1.47477.100.2.1.6.0": "npb_port_rx_errors_P1",

    // SNMP type: Counter64 | Units: packets | Range: 0-2^64 (monotonic counter)
    "1.3.6.1.4.1.47477.100.2.1.7.0": "npb_port_tx_errors_P1",

    // SNMP type: Counter64 | Units: packets | Range: 0-2^64 (monotonic counter)
    "1.3.6.1.4.1.47477.100.2.1.8.0": "npb_port_rx_drops_P1",

    // ---- Port 2 ----
    // [same 8 OIDs with portNum=2, suffix _P2]

    // ... Ports 3-7 ...

    // ---- Port 8 ----

    // SNMP type: INTEGER | Values: 1=up, 2=down, 3=testing | Range: 1-3
    "1.3.6.1.4.1.47477.100.2.8.1.0": "npb_port_status_P8",

    // [remaining 7 entries for Port 8]
    // ...last entry has NO trailing comma
    "1.3.6.1.4.1.47477.100.2.8.8.0": "npb_port_rx_drops_P8"
  }
}
```

### Expected Entry Count Verification

```bash
# Strip JSONC comments and count OidMap entries
# System: 4 entries
# Per-port: 8 ports x 8 OIDs = 64 entries
# Total: 68 entries

# Quick validation (requires jq and sed for comment stripping):
sed 's|//.*||' src/SnmpCollector/config/oidmap-npb.json | jq '.OidMap | length'
# Expected output: 68

# Combined with OBP:
sed 's|//.*||' src/SnmpCollector/config/oidmap-obp.json | jq '.OidMap | length'
# Expected output: 24

# Total OID map after merge: 92 entries
```

### ConfigMap Addition

```yaml
# In deploy/k8s/configmap.yaml, add under data:
  oidmap-npb.json: |
    {
      "OidMap": {
        "1.3.6.1.4.1.47477.100.1.1.0": "npb_cpu_util",
        ...all 68 entries (comments stripped for ConfigMap YAML)...
        "1.3.6.1.4.1.47477.100.2.8.8.0": "npb_port_rx_drops_P8"
      }
    }
```

**Note on ConfigMap JSONC:** YAML multi-line strings (`|`) support embedded JSONC comments since the JSONC is parsed by .NET, not K8s. However, including comments in ConfigMap YAML can make the ConfigMap large and harder to read in `kubectl describe`. The planner may choose to include or exclude comments in the ConfigMap version.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Empty `"OidMap": {}` for NPB | Populated 68-entry NPB OID map | Phase 12 | NPB device OIDs resolve to meaningful metric names |
| OBP-only OID coverage | OBP + NPB OID coverage | Phase 12 | Both device families have full OID maps |

## Open Questions

1. **OID prefix mismatch with simulator**
   - What we know: CONTEXT decisions use `47477.100.1/2` subtrees. The NPB simulator uses `47477.100.4` as its base prefix. The real MIB hierarchy is `cgs(47477) -> npb(100) -> npb-2e(4) -> systemMib(1) / portsMib(2)`.
   - What's unclear: Whether the OID map should use the simplified CONTEXT tree or align with the simulator's real OIDs.
   - Recommendation: Follow CONTEXT decisions (locked). The OID map defines the canonical OID-to-name mapping for Simetra. If integration testing with the simulator is needed later, either update the simulator or add an OID translation layer. For Phase 12, the OID map is a standalone data artifact.

2. **Expected ranges for Counter64 metrics**
   - What we know: Counter64 values are monotonically increasing and wrap at 2^64. "Expected range" for counters is less meaningful than for gauges.
   - What's unclear: Whether to document a practical expected range (e.g., "0 to ~10^18 in practice") or just say "0-2^64 (monotonic counter)".
   - Recommendation: Use `Range: 0-2^64 (monotonic counter)` consistently for all Counter64 OIDs. This is informative and matches SNMP convention.

3. **System metric expected ranges**
   - What we know: From the NPB simulator:
     - CPU load average: 0.3 to 3.0 (realistic for 4-core system)
     - Memory used: 4096-6144 MB range in simulator (total ~8192 MB)
     - Temperature: sensor-dependent, 28-65 C across different sensor types
     - Uptime: human-readable string, no numeric range
   - Recommendation: Use ranges from the simulator as "expected" ranges in documentation. These are realistic for the NPB-2E device class.

4. **ConfigMap comments or no comments?**
   - What we know: JSONC comments work in ConfigMap (parsed by .NET, not K8s). But they add bulk.
   - Recommendation: Include comments in the local `config/oidmap-npb.json` file (source of truth). For the ConfigMap, strip comments to keep it concise. The comments are for developer reference, not runtime.

## Sources

### Primary (HIGH confidence)

- **Codebase: `oidmap-obp.json`** -- Verified exact file structure, comment format, `"OidMap"` wrapper requirement, entry format
- **Codebase: `Program.cs`** -- Verified auto-scan loop: `Directory.GetFiles(configDir, "oidmap-*.json").OrderBy(f => f)` with `AddJsonFile(file, optional: true, reloadOnChange: true)`
- **Codebase: `ServiceCollectionExtensions.cs`** -- Verified `config.GetSection(OidMapOptions.SectionName).Bind(opts.Entries)` binding pattern
- **Codebase: `npb_simulator.py`** -- Verified NPB OID prefix `1.3.6.1.4.1.47477.100.4`, port range 1-8, system health OID structure, SNMP types for all metrics
- **Codebase: `NPB-Device-Analysis.md`** -- Verified real MIB OID paths, SNMP types, metric names for all target OIDs
- **Codebase: Phase 11 RESEARCH.md and CONTEXT.md** -- Verified naming convention, file structure patterns, K8s ConfigMap approach

### Secondary (MEDIUM confidence)

- **NPB MIB files** (`NPB-SYSTEM.mib`, `NPB-PORTS.mib`, `NPB-2E.mib`, `CGS.mib`) -- Verified OID hierarchy: `enterprises(47477) -> npb(100) -> npb-2e(4) -> systemMib(1) / portsMib(2)`. Verified SNMP types for CPU (OctetString), memory (String/Bytes), temperature (String/C), port status (Enum), traffic counters (Counter64).

### Tertiary (LOW confidence)

- **Expected ranges for system metrics** -- Derived from simulator random-walk bounds, not from real device data sheets. Ranges are realistic but not authoritative.

## Metadata

**Confidence breakdown:**
- File structure: HIGH - Exact copy of proven OBP pattern; no ambiguity
- OID tree design: HIGH - CONTEXT decisions are explicit and complete
- Metric naming: HIGH - CONTEXT decisions provide exact naming pattern with examples
- SNMP types and ranges: MEDIUM - Types verified against MIBs; ranges from simulator (realistic but fictional)
- Suffix ID assignments: HIGH - Sequential assignment is straightforward; Claude's discretion area

**Research date:** 2026-03-07
**Valid until:** 2026-04-07 (stable domain; configuration file authoring)
