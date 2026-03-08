# Phase 19: Business Dashboard - Research

**Researched:** 2026-03-08
**Domain:** Grafana dashboard JSON (table panels, instant Prometheus queries, template variables)
**Confidence:** HIGH

## Summary

This phase creates a Grafana dashboard JSON file (`simetra-business.json`) that displays current SNMP gauge and info metric values in two dynamically-populated tables. The primary research domain is Grafana dashboard JSON structure for table panels with instant Prometheus queries, focusing on the existing patterns established in `simetra-operations.json`.

The codebase already has a fully working operations dashboard JSON at `deploy/grafana/dashboards/simetra-operations.json` that demonstrates every structural pattern needed: `__inputs`/`DS_PROMETHEUS` datasource templating, `schemaVersion: 39`, table panels with instant queries, field overrides to hide/rename columns, row panels as section headers, and template variables using `label_values()`. The business dashboard replicates these exact patterns with different queries and a `device_name` variable instead of `pod`.

The two SNMP metrics are `snmp_gauge` (numeric values with labels: metric_name, oid, device_name, ip, source, snmp_type) and `snmp_info` (always 1.0 with an additional `value` label containing the string data). For the gauge table, a simple instant query returns all labels as columns. For the info table, the same approach works but the numeric `Value` column (always 1.0) must be hidden, and the `value` label (string content) should be shown.

**Primary recommendation:** Clone the operations dashboard table panel JSON pattern, using instant queries with `format: "table"` and field overrides to hide/rename columns. No transformations beyond the standard Grafana table format are needed.

## Standard Stack

This phase has no software dependencies -- it produces a static JSON file.

### Core
| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| Grafana Dashboard JSON | schemaVersion 39 | Dashboard definition format | Matches operations dashboard, Grafana 11.x compatible |
| Prometheus datasource | via `${DS_PROMETHEUS}` | Query target | Same `__inputs` pattern as operations dashboard |

### Supporting
| Tool | Purpose | When to Use |
|------|---------|-------------|
| Grafana table panel | Display instant query results as columns | Both gauge and info metric tables |
| Grafana row panel | Section headers | "Gauge Metrics" and "Info Metrics" headers |
| Grafana template variable | Device filter dropdown | `label_values(snmp_gauge, device_name)` |

## Architecture Patterns

### Dashboard File Structure
```
deploy/grafana/dashboards/
  simetra-operations.json   # existing -- 21 panels, pod-focused
  simetra-business.json     # new -- 4 panels (2 rows + 2 tables), device-focused
```

### Pattern 1: Instant Table Query (from operations dashboard Pod Identity panel)
**What:** Use `format: "table"` with `instant: true` to get current metric values where each label becomes a column.
**When to use:** Both gauge and info tables.
**Example:**
```json
// Source: simetra-operations.json lines 213-233 (Pod Identity table)
{
  "targets": [
    {
      "datasource": {
        "type": "prometheus",
        "uid": "${DS_PROMETHEUS}"
      },
      "expr": "snmp_gauge{device_name=~\"$device\"}",
      "format": "table",
      "instant": true,
      "refId": "A"
    }
  ],
  "type": "table"
}
```

### Pattern 2: Column Hiding via Field Overrides
**What:** Hide unwanted columns (Time, __name__, ip, source, etc.) using `custom.hidden` overrides.
**When to use:** Both tables need to hide infrastructure labels and show only business-relevant columns.
**Example:**
```json
// Source: simetra-operations.json lines 97-121 (hiding Time and Value columns)
{
  "overrides": [
    {
      "matcher": { "id": "byName", "options": "Time" },
      "properties": [
        { "id": "custom.hidden", "value": true }
      ]
    },
    {
      "matcher": { "id": "byName", "options": "__name__" },
      "properties": [
        { "id": "custom.hidden", "value": true }
      ]
    }
  ]
}
```

### Pattern 3: Column Rename via Field Overrides
**What:** Give columns human-readable display names.
**When to use:** Both tables -- rename label columns to title case for readability.
**Example:**
```json
// Source: simetra-operations.json lines 122-145 (renaming service_instance_id)
{
  "matcher": { "id": "byName", "options": "service_instance_id" },
  "properties": [
    { "id": "displayName", "value": "Service Instance" }
  ]
}
```

### Pattern 4: Template Variable for Device Filter
**What:** Dropdown populated from metric label values, with multi-select and "All" default.
**When to use:** The `device` template variable for filtering both tables.
**Example:**
```json
// Adapted from simetra-operations.json lines 1856-1881 (pod variable)
{
  "allValue": ".*",
  "current": {
    "selected": true,
    "text": "All",
    "value": "$__all"
  },
  "datasource": {
    "type": "prometheus",
    "uid": "${DS_PROMETHEUS}"
  },
  "definition": "label_values(snmp_gauge, device_name)",
  "includeAll": true,
  "label": "Device",
  "multi": true,
  "name": "device",
  "query": {
    "qryType": 1,
    "query": "label_values(snmp_gauge, device_name)"
  },
  "refresh": 2,
  "type": "query"
}
```

### Pattern 5: Row Panel as Section Header
**What:** Collapsed-false row panel for visual grouping.
**When to use:** "Gauge Metrics" and "Info Metrics" section headers.
**Example:**
```json
// Source: simetra-operations.json lines 60-69
{
  "collapsed": false,
  "gridPos": { "h": 1, "w": 24, "x": 0, "y": 0 },
  "id": null,
  "title": "Gauge Metrics",
  "type": "row"
}
```

### Anti-Patterns to Avoid
- **Hardcoded device names in queries:** Use `device_name=~"$device"` regex match, never literal device names.
- **Using range queries for table display:** Always use `instant: true` for current-value tables.
- **Setting `id` to numeric values:** The operations dashboard uses `"id": null` throughout -- Grafana assigns IDs on import.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Device dropdown | Custom query logic | `label_values(snmp_gauge, device_name)` template variable | Grafana populates automatically from Prometheus |
| Column visibility | Complex PromQL to exclude labels | Field overrides with `custom.hidden` | Cleaner, more maintainable |
| Auto-refresh | Custom JavaScript polling | Dashboard `refresh: "5s"` setting | Built into Grafana |
| Label-to-column mapping | Transformations or label_replace | `format: "table"` with `instant: true` | Prometheus table format auto-splits labels to columns |

**Key insight:** Grafana's table panel with instant queries automatically maps each Prometheus label to a separate column. No transformations are needed for the basic case. Just hide/rename the columns you don't want.

## Common Pitfalls

### Pitfall 1: Using `format: "time_series"` instead of `format: "table"` for table panels
**What goes wrong:** Labels don't appear as separate columns; you get a single "Value" row per series with no label breakdown.
**Why it happens:** Default query format is time_series.
**How to avoid:** Always set `"format": "table"` in the target definition for table panel queries.
**Warning signs:** Table shows single row with concatenated label string instead of one column per label.

### Pitfall 2: Forgetting `instant: true` on table queries
**What goes wrong:** Table shows multiple rows per series (one per scrape interval in the time range) instead of the single latest value.
**Why it happens:** Range queries return all samples in the window.
**How to avoid:** Always pair `"format": "table"` with `"instant": true` for current-value tables.
**Warning signs:** Hundreds/thousands of rows instead of one per device+metric combination.

### Pitfall 3: Info table showing numeric 1.0 value column
**What goes wrong:** The `snmp_info` metric always records 1.0 -- showing this in a "value" column is confusing to users.
**Why it happens:** The actual string content is in the `value` label, not the numeric measurement.
**How to avoid:** Hide the `Value #A` column (numeric measurement) using a field override. The `value` label column (containing the actual string content like "router123") will still be visible as a separate column since Grafana maps labels to columns.
**Warning signs:** Table has two "value"-related columns -- one with "1" and one with the string.

### Pitfall 4: Template variable query source metric disappearing
**What goes wrong:** Device filter dropdown becomes empty if the metric used for `label_values()` has no data.
**Why it happens:** `label_values()` only returns values from currently-active series.
**How to avoid:** Use `snmp_gauge` as the source metric for `label_values(snmp_gauge, device_name)` since gauge metrics are always present when devices are being polled. If no devices are polled, having no dropdown values is correct behavior.
**Warning signs:** Dropdown empty when devices are known to be active.

### Pitfall 5: Not filtering by device variable in both tables
**What goes wrong:** Selecting a device in the filter only filters one table.
**Why it happens:** Forgetting to add `{device_name=~"$device"}` to both the gauge and info queries.
**How to avoid:** Both queries must include the `device_name=~"$device"` label matcher.
**Warning signs:** Selecting a device filters one table but not the other.

## Code Examples

### Complete Gauge Metrics Table Panel
```json
// Source: adapted from simetra-operations.json Pod Identity table pattern
{
  "datasource": {
    "type": "prometheus",
    "uid": "${DS_PROMETHEUS}"
  },
  "fieldConfig": {
    "defaults": {
      "custom": {
        "align": "auto",
        "cellOptions": { "type": "auto" },
        "inspect": false
      },
      "mappings": [],
      "thresholds": {
        "mode": "absolute",
        "steps": [{ "color": "green", "value": null }]
      }
    },
    "overrides": [
      {
        "matcher": { "id": "byName", "options": "Time" },
        "properties": [{ "id": "custom.hidden", "value": true }]
      },
      {
        "matcher": { "id": "byName", "options": "__name__" },
        "properties": [{ "id": "custom.hidden", "value": true }]
      },
      {
        "matcher": { "id": "byName", "options": "ip" },
        "properties": [{ "id": "custom.hidden", "value": true }]
      },
      {
        "matcher": { "id": "byName", "options": "source" },
        "properties": [{ "id": "custom.hidden", "value": true }]
      },
      {
        "matcher": { "id": "byName", "options": "job" },
        "properties": [{ "id": "custom.hidden", "value": true }]
      },
      {
        "matcher": { "id": "byName", "options": "instance" },
        "properties": [{ "id": "custom.hidden", "value": true }]
      }
    ]
  },
  "gridPos": { "h": 12, "w": 24, "x": 0, "y": 1 },
  "id": null,
  "options": {
    "cellHeight": "sm",
    "footer": { "countRows": false, "fields": "", "reducer": ["sum"], "show": false },
    "showHeader": true,
    "sortBy": []
  },
  "targets": [
    {
      "datasource": { "type": "prometheus", "uid": "${DS_PROMETHEUS}" },
      "expr": "snmp_gauge{device_name=~\"$device\"}",
      "format": "table",
      "instant": true,
      "refId": "A"
    }
  ],
  "title": "Gauge Metrics",
  "type": "table"
}
```

### PromQL for Gauge Table
```promql
# Returns one row per unique (device_name, metric_name, oid, snmp_type) combination
# Each label automatically becomes a column in table format
snmp_gauge{device_name=~"$device"}
```

### PromQL for Info Table
```promql
# Returns one row per unique (device_name, metric_name, oid, snmp_type, value) combination
# The "value" label contains the string SNMP value (e.g., "router123")
# The numeric measurement (always 1.0) should be hidden via field override
snmp_info{device_name=~"$device"}
```

### Columns to Hide (both tables)
```
# Always hidden (infrastructure labels not relevant to business view):
Time          -- instant query timestamp, not meaningful
__name__      -- metric name ("snmp_gauge" or "snmp_info"), redundant
ip            -- device IP, not needed in business view
source        -- "poll" or "trap", not needed in business view
job           -- Prometheus job label, infrastructure detail
instance      -- Prometheus instance label, infrastructure detail

# Info table only:
Value #A      -- numeric measurement (always 1.0), meaningless for info metrics
```

### Columns to Show

**Gauge table (in order):** service_instance_id, device_name, metric_name, oid, snmp_type, Value #A
**Info table (in order):** service_instance_id, device_name, metric_name, oid, value

Note: The `value` label on snmp_info contains the actual string SNMP data. The `Value #A` column contains the numeric 1.0 measurement and should be hidden.

### snmp_gauge Labels (from SnmpMetricFactory.cs)
```
metric_name   -- OID map friendly name (e.g., "sysUpTime")
oid           -- numeric OID (e.g., "1.3.6.1.2.1.1.3.0")
device_name   -- device name from config (e.g., "router1")
ip            -- device IP address
source        -- "poll" or "trap"
snmp_type     -- one of: integer32, gauge32, timeticks, counter32, counter64
```
Plus Prometheus/OTel auto-added: `service_instance_id`, `job`, `instance`, `__name__`

### snmp_info Labels (from SnmpMetricFactory.cs)
```
metric_name   -- OID map friendly name
oid           -- numeric OID
device_name   -- device name from config
ip            -- device IP address
source        -- "poll" or "trap"
snmp_type     -- one of: octetstring, ipaddress, objectidentifier
value         -- the actual string SNMP value (truncated to 128 chars)
```
Plus Prometheus/OTel auto-added: `service_instance_id`, `job`, `instance`, `__name__`

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Graph panel with table transform | Native table panel with instant queries | Grafana 8+ | Simpler JSON, better performance |
| Panel IDs as sequential integers | `"id": null` (Grafana assigns on import) | Established in this project | No ID collision on import |
| `__requires` listing specific panel types | Include only used panel types | schemaVersion 39 | Must list "table" in `__requires` |

**Deprecated/outdated:**
- Old "table-old" panel type: replaced by current "table" panel type in Grafana 8+

## Open Questions

1. **Exact Prometheus auto-added labels**
   - What we know: Prometheus typically adds `job`, `instance`; OTel collector adds `service_instance_id`. These are visible in the operations dashboard queries.
   - What's unclear: Whether additional labels like `service_name`, `service_namespace` are added by the OTel pipeline.
   - Recommendation: Hide all known infrastructure labels. If unexpected columns appear during UAT, add more hide overrides. The pattern of hiding by field name is easy to extend.

2. **Column ordering in Grafana table panel**
   - What we know: Grafana displays columns in the order they appear in the query result. The context decision specifies: service_instance_id, device_name, metric_name, oid, snmp_type, value.
   - What's unclear: Whether Grafana respects a specific column order or alphabetizes.
   - Recommendation: Use the "Organize fields by name" transformation if column order matters, or rely on field overrides to reorder. Test during UAT.

## Sources

### Primary (HIGH confidence)
- `deploy/grafana/dashboards/simetra-operations.json` -- existing project dashboard, verified patterns for table panels, template variables, field overrides, row panels, `__inputs`, schema version, all structural elements
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` -- exact label names and types for snmp_gauge and snmp_info instruments
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` -- meter names (LeaderMeterName for business metrics)
- `src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs` -- RecordGauge and RecordInfo signatures confirming all label parameters

### Secondary (MEDIUM confidence)
- Grafana official docs on dashboard JSON model -- confirms schemaVersion 39 structure
- Grafana community forums on table panel instant queries -- confirms format/instant pattern

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no external dependencies, pure JSON file matching established project pattern
- Architecture: HIGH -- operations dashboard provides verified, working template for every structural element
- Pitfalls: HIGH -- pitfalls derived from known Grafana table panel behavior verified against operations dashboard
- PromQL queries: HIGH -- metric names and labels verified directly from C# source code

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (stable -- Grafana JSON schema changes rarely)
