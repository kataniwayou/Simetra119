# Phase 74: Grafana Dashboard Panel - Research

**Researched:** 2026-03-23
**Domain:** Grafana JSON dashboard authoring, Prometheus metric names, OTel label naming
**Confidence:** HIGH — all findings sourced directly from live codebase files

## Summary

This research reads the three authoritative source files directly: `TenantMetricService.cs` for
exact Prometheus metric names and label names, `simetra-operations.json` for the target dashboard
structure and insertion point, and `simetra-business.json` for the reference gauge metrics table
pattern (merge + organize + trend arrow + P99).

The OTel .NET SDK converts dot-separated instrument names to underscore-separated Prometheus metric
names, and converts dot-separated tag keys to underscore-separated label names. Both conversions are
confirmed by cross-referencing the code with existing working queries in the dashboards.

**Primary recommendation:** All PromQL must use snake_case metric names and snake_case label names.
The new table is inserted after the last "Commands" panel (y=47, immediately before the ".NET Runtime"
row at y=47) as a new row + table pair.

## Standard Stack

No new libraries. This phase is pure JSON editing of an existing Grafana dashboard file.

**File:** `deploy/grafana/dashboards/simetra-operations.json`

**Reference pattern file:** `deploy/grafana/dashboards/simetra-business.json`
(The "Gauge Metrics" table is the reference implementation for merge+organize+trend+P99.)

## Prometheus Metric Names (HIGH confidence)

OTel .NET converts instrument names by replacing `.` with `_`. The meter name is NOT prepended
for named instruments — only the instrument name itself is used.

Source: `src/SnmpCollector/Telemetry/TenantMetricService.cs`, confirmed against existing PromQL
in `simetra-operations.json` where `snmp_event_published_total` matches the instrument name pattern.

### Confirmed instrument-to-Prometheus-name mapping

| OTel Instrument Name | Prometheus Metric Name | Type |
|---|---|---|
| `tenant.tier1.stale` | `tenant_tier1_stale_total` | Counter |
| `tenant.tier2.resolved` | `tenant_tier2_resolved_total` | Counter |
| `tenant.tier3.evaluate` | `tenant_tier3_evaluate_total` | Counter |
| `tenant.command.dispatched` | `tenant_command_dispatched_total` | Counter |
| `tenant.command.failed` | `tenant_command_failed_total` | Counter |
| `tenant.command.suppressed` | `tenant_command_suppressed_total` | Counter |
| `tenant.state` | `tenant_state` | Gauge |
| `tenant.evaluation.duration.milliseconds` | `tenant_evaluation_duration_milliseconds` | Histogram |

**Histogram bucket metric name (for P99 query):**
`tenant_evaluation_duration_milliseconds_bucket`

**Note on `_total` suffix:** OTel .NET appends `_total` to Counter instruments when exporting
to Prometheus. This is consistent with the existing codebase pattern — `snmp_event_published_total`,
`snmp_command_dispatched_total`, `snmp_command_failed_total`, `snmp_command_suppressed_total`
all follow this pattern. The pipeline-level commands counters are confirmed at operations.json
lines 1469, 1565, 1661.

## Prometheus Label Names (HIGH confidence)

OTel .NET converts tag key names using the same dot-to-underscore rule. Tags are applied as
direct string keys in the `TagList` in `TenantMetricService.cs`.

Source: `TenantMetricService.cs` lines 61, 64, 67, 73, 76, 79, 85, 89

| Tag Key in Code | Prometheus Label | Confirmed |
|---|---|---|
| `"tenant_id"` | `tenant_id` | YES — code uses snake_case directly |
| `"priority"` | `priority` | YES — code uses snake_case directly |

Tags are passed as literal string keys — no conversion needed. `tenant_id` and `priority` are
exactly as they appear in PromQL.

**Existing OTel resource labels confirmed in dashboard queries:**
- `service_instance_id` — used in every existing panel filter (operations.json lines 184, 317, etc.)
- `k8s_pod_name` — used in every existing panel filter

These resource labels attach automatically from the OTel SDK and are present on all metrics
from the `SnmpCollector.Tenant` meter.

## Operations Dashboard Structure (HIGH confidence)

Source: `deploy/grafana/dashboards/simetra-operations.json` (2311 lines, read in full)

### Panel layout summary

| y-start | Row title | Panel IDs | Last panel gridPos y-end |
|---|---|---|---|
| 0 | Pod Identity (row) | 1 | — |
| 1 | Pod Identity table | 2 | y=1, h=5 → ends at y=6 |
| 6 | Pipeline Counters (row) | 3 | — |
| 7 | Events Published | 4 | — |
| 7 | Events Handled | 5 | — |
| 7 | Event Errors | 6 | — |
| 7 | Events Rejected | 7 | y=7, h=8 → ends at y=15 |
| 15 | Polls Executed | 8 | — |
| 15 | Poll Recovered | 14 | — |
| 15 | Poll Unreachable | 13 | y=15, h=8 → ends at y=23 |
| 23 | Traps Dropped | 12 | — |
| 23 | Trap Auth Failed | 10 | — |
| 23 | Traps Received | 9 | y=23, h=8 → ends at y=31 |
| 31 | Tenant Vector Routed | 22 | — |
| 31 | Snapshot Cycle Duration | 23 | y=31, h=8 → ends at y=39 |
| 39 | Command Dispatched | 24 | — |
| 39 | Command Failed | 25 | — |
| 39 | Command Suppressed | 26 | y=39, h=8 → ends at y=47 |
| 47 | .NET Runtime (row) | 15 | — |
| 48 | GC Collections Rate | 16 | — |
| 48 | CPU Time | 17 | — |
| 48 | Process Working Set | 18 | — |
| 56 | Exceptions | 19 | — |
| 56 | Thread Pool Threads | 20 | — |
| 56 | Thread Pool Queue Length | 21 | y=56, h=8 → ends at y=64 |

**There is no existing "Commands" section row separator.** Command panels (24, 25, 26) sit directly
under the Snapshot panels. There is no row panel wrapping them. The Pipeline Counters row (id=3,
y=6) wraps all pipeline+poll+trap+command panels.

**Insertion point:** The new "Tenant Status" row and table go between y=47 and the ".NET Runtime"
row. Specifically:
- Insert a new row panel at y=47 (shifting ".NET Runtime" row from y=47 to y=60 and all
  subsequent panels down by 13)
- Insert the tenant table panel at y=48, h=10
- ".NET Runtime" row moves to y=58
- GC/CPU/Memory panels shift to y=59, Exceptions/Threads to y=67

**Alternatively** (simpler, no shifting): append after the last panel by placing the new row at
y=64 and table at y=65. The .NET Runtime section currently ends at y=64. Appending at the end
avoids renumbering every existing panel's y-coordinate.

**Recommendation:** Append at the end (y=64 for row, y=65 for table, h=10). This avoids touching
any existing panel gridPos values. TDSH-01 says "after existing commands panels" — this places
it after all commands panels, between the command panels and runtime panels... but also after
runtime panels if appended. Inserting between Commands and .NET Runtime requires shifting all
.NET Runtime panels down. Either approach is valid JSON — the planner must decide which to specify.

**Dashboard metadata:**
- `"uid": "simetra-operations"`
- `"schemaVersion": 39`
- Datasource uid: `"dfg62p9s7xl34a"` (used on ALL existing panels — must match)
- `"refresh": "5s"`
- Panel IDs already in use: 1–26 (with gap at 11). Next available ID: 27 (row), 28 (table).

### Template variables (operations dashboard)

Only two variables exist — no `$ip` variable (unlike business dashboard):

```
$host → label_values(snmp_event_published_total, service_instance_id)
$pod  → label_values(snmp_event_published_total{service_instance_id=~"$host"}, k8s_pod_name)
```

The filter pattern for ALL tenant metric queries is therefore:
```
service_instance_id=~"$host", k8s_pod_name=~"$pod"
```

No `$ip` filter (that variable does not exist in this dashboard).

## Reference Implementation: Gauge Metrics Table (HIGH confidence)

Source: `deploy/grafana/dashboards/simetra-business.json` lines 37–511

This is the definitive reference for the merge+organize+trend+P99 pattern.

### Query structure (3 refIds: A, B, C)

```
refId A: main metric query — format: "table", instant: true
refId B: delta for trend column — delta(metric{filters}[30s]), format: "table", instant: true
refId C: histogram P99 — histogram_quantile(0.99, sum by (le, ...) (rate(..._bucket{...}[$__rate_interval]))), format: "table", instant: true
```

### Transformation pipeline

```json
[
  { "id": "merge", "options": {} },
  {
    "id": "organize",
    "options": {
      "indexByName": {
        "service_instance_id": 0,
        "k8s_pod_name": 1,
        ...
        "Value #A": N,
        "Value #B": N+1,
        "Value #C": N+2
      }
    }
  }
]
```

The `merge` transformation joins all three result sets on shared label keys. Column names from
each query appear as `Value #A`, `Value #B`, `Value #C` in the merged result.

### Field overrides pattern

Each column is configured via `fieldConfig.overrides` using `"matcher": { "id": "byName" }`:

- Hide unwanted label columns: `"custom.hidden": true`
- Rename label columns to display names: `"displayName": "..."`
- Width: `"custom.width": N`
- Trend column (`Value #B`): `"custom.cellOptions": { "mode": "basic", "type": "color-background" }`
- P99 column (`Value #C`): `"unit": "ms"`, `"decimals": 1`

### Trend arrow configuration (exact values from business dashboard)

```json
{
  "id": "thresholds",
  "value": {
    "mode": "absolute",
    "steps": [
      { "color": "text", "value": null },
      { "color": "dark-red", "value": -1000000000 },
      { "color": "text", "value": 0 },
      { "color": "dark-green", "value": 0.0001 }
    ]
  }
},
{
  "id": "mappings",
  "value": [
    {
      "options": { "match": "null", "result": { "color": "text", "text": "-" } },
      "type": "special"
    },
    {
      "options": { "from": -1000000000, "result": { "color": "dark-red", "text": "▼" }, "to": -0.0001 },
      "type": "range"
    },
    {
      "options": { "from": -0.0001, "result": { "color": "text", "text": "—" }, "to": 0.0001 },
      "type": "range"
    },
    {
      "options": { "from": 0.0001, "result": { "color": "dark-green", "text": "▲" }, "to": 1000000000 },
      "type": "range"
    }
  ]
}
```

### Table options

```json
{
  "cellHeight": "sm",
  "footer": { "countRows": false, "fields": "", "reducer": ["sum"], "show": false },
  "showHeader": true,
  "sortBy": []
}
```

## Complete PromQL Queries for the New Panel (HIGH confidence)

### RefId A — Tenant State (gauge, instant)

```promql
tenant_state{service_instance_id=~"$host", k8s_pod_name=~"$pod"}
```

This is a gauge so no `rate()`. Returns one row per (service_instance_id, k8s_pod_name, tenant_id, priority).

### RefId B — Trend (delta on dispatched counter, instant)

```promql
delta(tenant_command_dispatched_total{service_instance_id=~"$host", k8s_pod_name=~"$pod"}[30s])
```

### RefId C — P99 evaluation duration (histogram, instant)

```promql
histogram_quantile(0.99, sum by (le, tenant_id, priority, service_instance_id, k8s_pod_name) (rate(tenant_evaluation_duration_milliseconds_bucket{service_instance_id=~"$host", k8s_pod_name=~"$pod"}[$__rate_interval])))
```

### RefIds D–I — Counter rates (6 counters, all instant)

```promql
# D — Dispatched
sum by (tenant_id, priority, service_instance_id, k8s_pod_name) (rate(tenant_command_dispatched_total{service_instance_id=~"$host", k8s_pod_name=~"$pod"}[$__rate_interval]))

# E — Failed
sum by (tenant_id, priority, service_instance_id, k8s_pod_name) (rate(tenant_command_failed_total{service_instance_id=~"$host", k8s_pod_name=~"$pod"}[$__rate_interval]))

# F — Suppressed
sum by (tenant_id, priority, service_instance_id, k8s_pod_name) (rate(tenant_command_suppressed_total{service_instance_id=~"$host", k8s_pod_name=~"$pod"}[$__rate_interval]))

# G — Stale
sum by (tenant_id, priority, service_instance_id, k8s_pod_name) (rate(tenant_tier1_stale_total{service_instance_id=~"$host", k8s_pod_name=~"$pod"}[$__rate_interval]))

# H — Resolved
sum by (tenant_id, priority, service_instance_id, k8s_pod_name) (rate(tenant_tier2_resolved_total{service_instance_id=~"$host", k8s_pod_name=~"$pod"}[$__rate_interval]))

# I — Evaluate
sum by (tenant_id, priority, service_instance_id, k8s_pod_name) (rate(tenant_tier3_evaluate_total{service_instance_id=~"$host", k8s_pod_name=~"$pod"}[$__rate_interval]))
```

**Important:** The `merge` transform requires ALL queries to share the same label keys for join
rows correctly. All queries above use the same `by (tenant_id, priority, service_instance_id, k8s_pod_name)` grouping, which matches the labels on `tenant_state` (RefId A).

## Column Mapping (13 columns total)

| Column Display Name | Source | `indexByName` value |
|---|---|---|
| Host | `service_instance_id` (label) | 0 |
| Pod | `k8s_pod_name` (label) | 1 |
| Tenant | `tenant_id` (label) | 2 |
| Priority | `priority` (label) | 3 |
| State | `Value #A` | 4 |
| Dispatched | `Value #D` | 5 |
| Failed | `Value #E` | 6 |
| Suppressed | `Value #F` | 7 |
| Stale | `Value #G` | 8 |
| Resolved | `Value #H` | 9 |
| Evaluate | `Value #I` | 10 |
| P99 (ms) | `Value #C` | 11 |
| Trend | `Value #B` | 12 |

All other labels (`job`, `instance`, `service_name`, `telemetry_sdk_*`) must be hidden via overrides.

## State Column Value Mappings (HIGH confidence)

Source: Phase context decisions (locked). Numeric gauge values map to enum names.

```json
{
  "id": "mappings",
  "value": [
    { "options": { "0": { "color": "text", "index": 0, "text": "NotReady" } }, "type": "value" },
    { "options": { "1": { "color": "green", "index": 1, "text": "Healthy" } }, "type": "value" },
    { "options": { "2": { "color": "yellow", "index": 2, "text": "Resolved" } }, "type": "value" },
    { "options": { "3": { "color": "red", "index": 3, "text": "Unresolved" } }, "type": "value" }
  ]
}
```

State column also needs `color-background` cell options (same as trend column):
```json
{ "id": "custom.cellOptions", "value": { "mode": "basic", "type": "color-background" } }
```

The `text` color used in the context decisions corresponds to Grafana's built-in `"text"` named
color (grey/neutral). The existing "Role" column in operations.json (id=2, line 148) uses the exact
same `color-background` + value mapping pattern — confirmed at operations.json lines 122–154.

## Common Pitfalls

### Pitfall 1: Wrong label names in PromQL
**What goes wrong:** Using `tenantId` or `Priority` (camelCase) instead of `tenant_id` and `priority`.
**Why it happens:** OTel docs mention tag normalization but code uses snake_case directly.
**How to avoid:** Tags are set as `"tenant_id"` and `"priority"` string literals in `TenantMetricService.cs` — use these exact strings.

### Pitfall 2: Missing `_total` suffix on counters
**What goes wrong:** Querying `tenant_command_dispatched` instead of `tenant_command_dispatched_total`.
**Why it happens:** OTel .NET appends `_total` to Counter instruments on Prometheus export.
**How to avoid:** All 6 counter metrics have `_total` suffix. Gauge and histogram do not.

### Pitfall 3: Merge join fails due to label mismatch
**What goes wrong:** Table shows duplicate rows or NaN values because queries return different label sets.
**Why it happens:** One query uses `by (tenant_id)` and another uses `by (tenant_id, priority)`.
**How to avoid:** All rate/histogram queries must group by `(le, tenant_id, priority, service_instance_id, k8s_pod_name)` with identical dimensions. RefId A (gauge) naturally includes all these labels.

### Pitfall 4: Using wrong datasource UID
**What goes wrong:** Panel shows "No data" after import.
**Why it happens:** Hardcoded datasource UID `"dfg62p9s7xl34a"` must match the deployed Prometheus datasource.
**How to avoid:** Copy the UID from any existing panel. All panels in operations.json use `"dfg62p9s7xl34a"`.

### Pitfall 5: `organize` indexByName out of sync with actual column names
**What goes wrong:** Columns appear in wrong order or are hidden unexpectedly.
**Why it happens:** `indexByName` keys must exactly match the merged column names including `Value #A`..`Value #I` notation.
**How to avoid:** After a 9-query panel, columns are `Value #A` through `Value #I`. Verify the refId letter maps correctly to the expected column name.

### Pitfall 6: `$ip` variable used in tenant queries
**What goes wrong:** Queries reference `ip=~"$ip"` but operations dashboard has no `$ip` variable.
**Why it happens:** Business dashboard has `$ip`; operations dashboard does not.
**How to avoid:** Operations dashboard only has `$host` and `$pod`. Do not add `$ip` filter.

## Architecture Patterns

### JSON insertion location

The new panels are appended after the Command Suppressed panel (last y=47 area) and before the
`.NET Runtime` row. Two new entries are added to the `"panels"` array:

1. A row panel (type: `"row"`)
2. A table panel (type: `"table"`)

The existing `.NET Runtime` row must have its `y` value updated if inserting between commands and
runtime. Appending at end (y=64+) avoids any edits to existing panels.

**Decision for planner:** The requirement TDSH-01 says "after existing commands panels." The last
commands panel ends at y=47. The `.NET Runtime` row is also at y=47. To insert between them:
- Move `.NET Runtime` row from y=47 to y=60 (+13)
- Move all 6 .NET panels from y=48/56 to y=61/69
- New row at y=47, new table at y=48, h=10

Or append after runtime (y=64 for row, y=65 for table). Technically satisfies "after commands"
since all commands panels end before y=47.

### Panel ID assignment

Use IDs 27 (row) and 28 (table). IDs 1–26 are all taken. ID 11 is missing from the file but
was likely deleted — skip it and use 27/28 to avoid conflicts.

## State of the Art

The operations dashboard uses `schemaVersion: 39` which is current for Grafana 10.x+. The
`color-background` cell type with `mode: "basic"` is the correct format for this schema version.
Older schema versions used a different format for cell coloring.

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Telemetry/TenantMetricService.cs` — exact instrument names, tag key names
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — meter name `"SnmpCollector.Tenant"`
- `deploy/grafana/dashboards/simetra-operations.json` — full panel structure, template variables, datasource UID, panel IDs, gridPos layout
- `deploy/grafana/dashboards/simetra-business.json` — complete merge+organize+trend+P99 reference implementation

## Metadata

**Confidence breakdown:**
- Metric names: HIGH — read directly from source code
- Label names: HIGH — read directly from source code (no conversion needed, literal snake_case)
- PromQL patterns: HIGH — verified against existing working queries in both dashboards
- JSON structure: HIGH — read directly from both dashboard files
- Insertion gridPos: HIGH — layout read from operations.json; row/table y-values are arithmetic
- Panel IDs: HIGH — enumerated from operations.json (IDs 1–26, next available 27/28)

**Research date:** 2026-03-23
**Valid until:** Until TenantMetricService.cs or either dashboard JSON changes (stable otherwise)
