# Phase 18: Operations Dashboard - Research

**Researched:** 2026-03-08
**Domain:** Grafana dashboard JSON, Prometheus PromQL, .NET OTel runtime metrics
**Confidence:** HIGH

## Summary

This phase creates a single Grafana dashboard JSON file for manual import, showing pipeline health (11 counters), pod identity/role detection, and .NET runtime metrics across all replicas. The stale files under `deploy/grafana/` are deleted and replaced with one new dashboard JSON.

The existing stale `simetra-operations.json` provides a verified structural template: `__inputs` with `DS_PROMETHEUS`, `schemaVersion: 39`, datasource references via `${DS_PROMETHEUS}`, and templating variables. The new dashboard follows the same JSON patterns but with correct metric names matching the current codebase.

Key finding: OTel SDK converts dots to underscores for Prometheus export, and counters get a `_total` suffix. The 11 pipeline counters all carry a `device_name` tag and are on the `SnmpCollector` meter (exported by ALL pods). Leader detection works by checking presence of `snmp_gauge` or `snmp_info` metrics (on the `SnmpCollector.Leader` meter, exported ONLY by the leader pod).

**Primary recommendation:** Build the dashboard JSON by hand following the existing stale dashboard's structural patterns (`__inputs`, `${DS_PROMETHEUS}`, gridPos layout), using verified Prometheus metric names from the codebase.

## Standard Stack

This phase has no library dependencies. It produces a static JSON file.

### Core
| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| Grafana Dashboard JSON | schemaVersion 39 | Dashboard definition format | Matches existing stale dashboard; compatible with Grafana 11.x |
| Prometheus PromQL | N/A | Query language for time series | Standard query language for Prometheus datasource |

### Supporting
| Tool | Purpose | When to Use |
|------|---------|-------------|
| `__inputs` / `${DS_PROMETHEUS}` pattern | Portable datasource reference | Always -- allows import into any Grafana instance |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `__inputs` portable pattern | Hardcoded datasource UID | Would break on any Grafana instance with different datasource name |
| schemaVersion 39 | Newer schema v2 | v2 is experimental/preview -- stick with stable schema |

## Architecture Patterns

### Dashboard JSON File Location
```
deploy/
  grafana/
    dashboards/
      simetra-operations.json    # New dashboard (replaces stale file)
```

All other files under `deploy/grafana/` are deleted (stale reference artifacts).

### Pattern 1: Portable Datasource via __inputs
**What:** The `__inputs` array declares datasource placeholders. On import, Grafana prompts the user to map each input to a local datasource.
**When to use:** Always for manually-imported dashboards.
**Example:**
```json
{
  "__inputs": [
    {
      "name": "DS_PROMETHEUS",
      "label": "Prometheus",
      "type": "datasource",
      "pluginId": "prometheus"
    }
  ]
}
```
All panel datasource references use:
```json
{
  "datasource": {
    "type": "prometheus",
    "uid": "${DS_PROMETHEUS}"
  }
}
```

### Pattern 2: Template Variable for Pod Filtering
**What:** A query-type template variable that populates a dropdown with all `service_instance_id` values.
**When to use:** For the pod filter dropdown at the top of the dashboard.
**Example:**
```json
{
  "name": "pod",
  "label": "Pod",
  "type": "query",
  "datasource": {
    "type": "prometheus",
    "uid": "${DS_PROMETHEUS}"
  },
  "definition": "label_values(snmp_event_published_total, service_instance_id)",
  "query": "label_values(snmp_event_published_total, service_instance_id)",
  "multi": true,
  "includeAll": true,
  "allValue": ".*",
  "current": {
    "selected": true,
    "text": "All",
    "value": "$__all"
  },
  "refresh": 2
}
```
Note: `refresh: 2` means refresh on time range change (keeps the variable current).

### Pattern 3: Rate Query for Counters
**What:** All 11 pipeline counters are monotonic counters. Display them as per-second rates.
**When to use:** Every pipeline counter panel.
**Example:**
```
sum by (service_instance_id) (rate(snmp_event_published_total{service_instance_id=~"$pod"}[$__rate_interval]))
```
- `$__rate_interval` is a Grafana built-in that auto-adjusts based on scrape interval
- `sum by (service_instance_id)` aggregates across `device_name` tag values to show per-pod totals
- Filter uses `service_instance_id=~"$pod"` to respect the pod variable

### Pattern 4: Leader Detection via Metric Presence
**What:** The leader is the only pod that exports `snmp_gauge` or `snmp_info` metrics (on the `SnmpCollector.Leader` meter). Use a PromQL query that checks metric presence.
**When to use:** Pod identity/role table panel.
**Example PromQL for role detection:**
```
# Returns 1 for leader, nothing for followers
count by (service_instance_id) (snmp_gauge) * 0 + 1
```
Or use a two-query approach in a table panel:
- Query A: `count by (service_instance_id, k8s_pod_name) (snmp_event_published_total)` -- returns all pods
- Query B: `count by (service_instance_id) (snmp_gauge)` -- returns only leader pod
- Transform with value mappings: presence in Query B = "Leader", absence = "Follower"

### Pattern 5: GridPos Layout
**What:** Grafana uses a 24-column grid. Panels are positioned with `gridPos: {x, y, w, h}`.
**When to use:** All panels.
**Recommended layout:**
- Row headers: `h: 1, w: 24`
- Table panel: `h: 4-5, w: 24` (full width)
- Time series panels: `h: 8, w: 8` (3 per row) or `h: 8, w: 6` (4 per row)
- For 11 pipeline counters: 4 panels per row (w: 6) = 3 rows (4+4+3)

### Anti-Patterns to Avoid
- **Hardcoded datasource UIDs:** Always use `${DS_PROMETHEUS}` -- hardcoded UIDs break on import
- **Using `rate()` without `$__rate_interval`:** Using fixed intervals (e.g., `[5m]`) ignores the actual scrape interval and can produce inaccurate results
- **Panel IDs that conflict:** Set all panel `id` fields to `null` -- Grafana auto-assigns on import. The stale dashboard uses `null` IDs and this works correctly.
- **Missing `_total` suffix on counter names:** OTel SDK appends `_total` to counter metric names in Prometheus. Query `snmp_event_published_total`, not `snmp_event_published`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Dashboard generation tooling | Custom JSON generator script | Hand-crafted JSON file | One dashboard, one file -- a generator adds complexity for zero benefit |
| Datasource provisioning | YAML provisioning files | Manual Grafana UI datasource setup | User decision: manual setup is simpler for single-instance deployments |
| Dashboard provisioning via ConfigMap | K8s ConfigMap + Grafana sidecar | Manual JSON import | User decision: plain file approach |

**Key insight:** This is a single static JSON file. The complexity is in getting the PromQL queries and panel layout right, not in tooling.

## Common Pitfalls

### Pitfall 1: Wrong Prometheus Metric Names
**What goes wrong:** Using `.NET instrument names` (dots) instead of Prometheus-exported names (underscores + suffix).
**Why it happens:** The .NET code defines instruments with dots (e.g., `snmp.event.published`) but OTel SDK converts them.
**How to avoid:** Always use the Prometheus form:
- Dots become underscores: `snmp.event.published` -> `snmp_event_published`
- Counters get `_total` suffix: `snmp_event_published_total`
- The unit suffix (if any) is also appended by OTel conventions
**Warning signs:** Panels show "No data" despite metrics being collected.

### Pitfall 2: Resource Attribute Label Names
**What goes wrong:** Using wrong label names for OTel resource attributes in PromQL.
**Why it happens:** OTel resource attributes use dots (`service.instance.id`, `k8s.pod.name`) but Prometheus labels use underscores.
**How to avoid:** Use underscore versions: `service_instance_id`, `k8s_pod_name`, `service_name`.
**Warning signs:** Label value queries return empty results.

### Pitfall 3: service_instance_id Is the Node Name, Not the Pod Name
**What goes wrong:** Assuming `service_instance_id` is the pod name.
**Why it happens:** The code sets `serviceInstanceId` from `PHYSICAL_HOSTNAME` env var, which maps to `spec.nodeName` in the deployment YAML.
**How to avoid:** Use `service_instance_id` for filtering (it is the unique identifier per pod in this setup -- each pod is on a different node). Use `k8s_pod_name` for display of the pod name (set from `HOSTNAME` env var = pod name).
**Warning signs:** Pod names in the table show node names instead of pod names.

### Pitfall 4: Rate of Zero vs No Data
**What goes wrong:** `rate()` on a counter that has never incremented returns nothing (no data), not zero.
**Why it happens:** Prometheus only stores samples when the counter is scraped. If a counter has value 0 and never changes, `rate()` may return empty or 0 depending on the counter age.
**How to avoid:** Use `or vector(0)` if you want to show 0 instead of gaps. For pipeline counters this is usually fine -- gaps mean "no activity".
**Warning signs:** Panels intermittently show "No data" for quiet pods.

### Pitfall 5: Template Variable Regex Must Match All
**What goes wrong:** Setting `allValue` incorrectly causes "All" selection to match nothing.
**Why it happens:** Different Grafana versions handle `$__all` differently.
**How to avoid:** Set `"allValue": ".*"` and use `=~` regex match in queries: `{service_instance_id=~"$pod"}`.
**Warning signs:** Selecting "All" in the dropdown shows no data.

### Pitfall 6: Dashboard schemaVersion Compatibility
**What goes wrong:** Using a schemaVersion too new for the target Grafana instance.
**Why it happens:** Each Grafana version bumps schemaVersion.
**How to avoid:** Use `schemaVersion: 39` (compatible with Grafana 10.x and 11.x). The existing stale dashboard uses 39 successfully.
**Warning signs:** Import errors or panel rendering issues.

## Code Examples

### Complete Pipeline Counter Panel (Time Series)
```json
{
  "type": "timeseries",
  "title": "Events Published Rate",
  "gridPos": { "x": 0, "y": 6, "w": 6, "h": 8 },
  "id": null,
  "datasource": {
    "type": "prometheus",
    "uid": "${DS_PROMETHEUS}"
  },
  "fieldConfig": {
    "defaults": {
      "unit": "ops",
      "custom": {
        "drawStyle": "line",
        "lineWidth": 2,
        "fillOpacity": 10,
        "pointSize": 5,
        "showPoints": "never",
        "spanNulls": false
      }
    },
    "overrides": []
  },
  "options": {
    "tooltip": { "mode": "multi", "sort": "desc" },
    "legend": { "displayMode": "list", "placement": "bottom", "calcs": [] }
  },
  "targets": [
    {
      "datasource": {
        "type": "prometheus",
        "uid": "${DS_PROMETHEUS}"
      },
      "expr": "sum by (service_instance_id) (rate(snmp_event_published_total{service_instance_id=~\"$pod\"}[$__rate_interval]))",
      "legendFormat": "{{service_instance_id}}",
      "refId": "A"
    }
  ]
}
```

### Pod Identity Table Panel
```json
{
  "type": "table",
  "title": "Pod Identity & Role",
  "gridPos": { "x": 0, "y": 1, "w": 24, "h": 5 },
  "id": null,
  "datasource": {
    "type": "prometheus",
    "uid": "${DS_PROMETHEUS}"
  },
  "targets": [
    {
      "datasource": {
        "type": "prometheus",
        "uid": "${DS_PROMETHEUS}"
      },
      "expr": "count by (service_instance_id, k8s_pod_name) (snmp_event_published_total{service_instance_id=~\"$pod\"})",
      "legendFormat": "",
      "refId": "A",
      "instant": true,
      "format": "table"
    },
    {
      "datasource": {
        "type": "prometheus",
        "uid": "${DS_PROMETHEUS}"
      },
      "expr": "count by (service_instance_id) (snmp_gauge{service_instance_id=~\"$pod\"})",
      "legendFormat": "",
      "refId": "B",
      "instant": true,
      "format": "table"
    }
  ],
  "transformations": [
    {
      "id": "merge",
      "options": {}
    }
  ],
  "fieldConfig": {
    "overrides": [
      {
        "matcher": { "id": "byName", "options": "Value #B" },
        "properties": [
          {
            "id": "custom.displayName",
            "value": "Role"
          },
          {
            "id": "mappings",
            "value": [
              {
                "type": "value",
                "options": {
                  "1": { "text": "Leader", "color": "green" }
                }
              },
              {
                "type": "special",
                "options": {
                  "match": "null",
                  "result": { "text": "Follower", "color": "text" }
                }
              }
            ]
          }
        ]
      }
    ]
  }
}
```

### Dashboard Skeleton
```json
{
  "__inputs": [
    {
      "name": "DS_PROMETHEUS",
      "label": "Prometheus",
      "type": "datasource",
      "pluginId": "prometheus"
    }
  ],
  "__requires": [
    { "type": "grafana", "id": "grafana", "name": "Grafana", "version": "11.0.0" },
    { "type": "datasource", "id": "prometheus", "name": "Prometheus", "version": "1.0.0" },
    { "type": "panel", "id": "timeseries", "name": "Time series", "version": "" },
    { "type": "panel", "id": "table", "name": "Table", "version": "" }
  ],
  "title": "Simetra Operations",
  "uid": "simetra-operations",
  "tags": ["simetra", "operations"],
  "timezone": "browser",
  "editable": true,
  "graphTooltip": 1,
  "refresh": "5s",
  "time": { "from": "now-15m", "to": "now" },
  "schemaVersion": 39,
  "templating": { "list": [ /* pod variable */ ] },
  "panels": [ /* panels */ ]
}
```

## Verified Metric Name Mapping

All 11 pipeline counters (meter: `SnmpCollector`, exported by ALL pods):

| .NET Instrument Name | Prometheus Metric Name | Tag |
|---------------------|----------------------|-----|
| `snmp.event.published` | `snmp_event_published_total` | `device_name` |
| `snmp.event.handled` | `snmp_event_handled_total` | `device_name` |
| `snmp.event.errors` | `snmp_event_errors_total` | `device_name` |
| `snmp.event.rejected` | `snmp_event_rejected_total` | `device_name` |
| `snmp.poll.executed` | `snmp_poll_executed_total` | `device_name` |
| `snmp.trap.received` | `snmp_trap_received_total` | `device_name` |
| `snmp.trap.auth_failed` | `snmp_trap_auth_failed_total` | `device_name` |
| `snmp.trap.unknown_device` | `snmp_trap_unknown_device_total` | `device_name` |
| `snmp.trap.dropped` | `snmp_trap_dropped_total` | `device_name` |
| `snmp.poll.unreachable` | `snmp_poll_unreachable_total` | `device_name` |
| `snmp.poll.recovered` | `snmp_poll_recovered_total` | `device_name` |

Leader-only metrics (meter: `SnmpCollector.Leader`, exported ONLY by leader):

| Prometheus Metric Name | Purpose |
|----------------------|---------|
| `snmp_gauge` | Business gauge metrics (used for role detection) |
| `snmp_info` | Business info metrics |

## Recommended .NET Runtime Metrics

Based on the `AddRuntimeInstrumentation()` call in `ServiceCollectionExtensions.cs`, these .NET runtime metrics are exported by all pods. Recommended panels (Claude's discretion area):

### GC Metrics (2 panels)
| Prometheus Name | Panel Title | Unit | Why Include |
|----------------|-------------|------|-------------|
| `dotnet_gc_collections_total` | GC Collections Rate | ops | Shows GC pressure per generation; high Gen2 = memory leak signal |
| `dotnet_gc_pause_time_seconds_total` | GC Pause Time Rate | seconds | Directly impacts latency; high pause = degraded pipeline |

### Memory Metrics (2 panels)
| Prometheus Name | Panel Title | Unit | Why Include |
|----------------|-------------|------|-------------|
| `dotnet_process_memory_working_set_bytes` | Process Working Set | bytes | Memory consumption relative to pod limits; OOM risk |
| `dotnet_gc_last_collection_heap_size_bytes` | GC Heap Size | bytes | Managed heap growth; trend shows leak or stabilization |

### Thread Pool Metrics (2 panels)
| Prometheus Name | Panel Title | Unit | Why Include |
|----------------|-------------|------|-------------|
| `dotnet_thread_pool_thread_count` | Thread Pool Threads | short | Thread pool saturation; Quartz jobs depend on available threads |
| `dotnet_thread_pool_queue_length` | Thread Pool Queue Length | short | Non-zero = thread pool saturation, work items waiting |

**Total: 6 .NET runtime panels.** These cover the three categories (GC, memory, thread pool) and provide actionable operational signals. JIT, assembly count, and timer metrics are omitted as they are rarely actionable for operations.

### Runtime Metric PromQL Patterns
- Counters (rate): `rate(dotnet_gc_collections_total{service_instance_id=~"$pod"}[$__rate_interval])`
- Gauges (instant): `dotnet_process_memory_working_set_bytes{service_instance_id=~"$pod"}`
- Rate with label: `sum by (service_instance_id, gc_generation) (rate(dotnet_gc_collections_total{service_instance_id=~"$pod"}[$__rate_interval]))` (GC has `gc_generation` label: gen0, gen1, gen2)

## Panel Layout Recommendation

Using 24-column grid. Claude's discretion items resolved:

| Section | Panels Per Row | Panel Width | Panel Height | Rows |
|---------|---------------|-------------|--------------|------|
| Pod Identity Table | 1 | 24 | 5 | 1 |
| Pipeline Counters | 4 | 6 | 8 | 3 (4+4+3) |
| .NET Runtime | 3 | 8 | 8 | 2 (3+3) |

**Total panels:** 1 (table) + 11 (pipeline) + 6 (runtime) = 18 panels + 3 row headers = 21 elements.

**Y-position calculation:**
- y=0: Row header "Pod Identity"
- y=1: Pod identity table (h=5)
- y=6: Row header "Pipeline Counters"
- y=7: Pipeline row 1 (4 panels, h=8)
- y=15: Pipeline row 2 (4 panels, h=8)
- y=23: Pipeline row 3 (3 panels, h=8)
- y=31: Row header ".NET Runtime"
- y=32: Runtime row 1 (3 panels, h=8)
- y=40: Runtime row 2 (3 panels, h=8)

## Stale Files to Delete

All files under `deploy/grafana/` that must be removed:

```
deploy/grafana/dashboards/npb-device.json       # Stale reference project dashboard
deploy/grafana/dashboards/obp-device.json       # Stale reference project dashboard
deploy/grafana/dashboards/simetra-operations.json  # Stale, replaced by new version
deploy/grafana/provisioning/datasources/simetra-prometheus.yaml  # No longer needed
```

After cleanup, `deploy/grafana/provisioning/` directory tree is removed entirely.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Grafana provisioning YAML + sidecar | Manual JSON import | User decision | Simpler single-instance deployment |
| Dashboard JSON schema v2 | Schema v1 (schemaVersion 39) | Grafana 11.x | v2 is experimental; stick with stable |
| `instance` label (Prometheus default) | `service_instance_id` (OTel resource attribute) | OTel adoption | Filter by OTel identity, not scrape target |

## Open Questions

1. **GC generation label name**
   - What we know: .NET runtime metrics export GC collection counts with a generation label
   - What's unclear: The exact Prometheus label name may be `gc_generation` or `dotnet_gc_generation` depending on the OTel instrumentation version
   - Recommendation: Use `dotnet_gc_collections_total` without breaking down by generation initially. If generation breakdown is desired, verify the label name against a running Prometheus instance.

2. **Resource attribute attachment to metrics**
   - What we know: OTel resource attributes (`service_instance_id`, `k8s_pod_name`) should be attached to all exported metrics
   - What's unclear: Whether the OTLP-to-Prometheus pipeline attaches them as metric labels directly or only on `target_info`
   - Recommendation: The deployment uses OTLP exporter to a collector that writes to Prometheus. Resource attributes should be promoted to metric labels by the collector. If they are not, the `target_info` metric can be joined. Build queries assuming direct label attachment (simpler), and note in the dashboard description that resource attribute promotion may need to be configured in the OTel Collector.

## Sources

### Primary (HIGH confidence)
- Codebase: `src/SnmpCollector/Telemetry/PipelineMetricService.cs` -- all 11 counter instruments verified
- Codebase: `src/SnmpCollector/Telemetry/TelemetryConstants.cs` -- meter names verified
- Codebase: `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` -- OTel config, `AddRuntimeInstrumentation()` verified
- Codebase: `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` -- leader gating logic verified
- Codebase: `deploy/grafana/dashboards/simetra-operations.json` -- existing JSON structure patterns verified
- Codebase: `deploy/k8s/snmp-collector/deployment.yaml` -- env var mapping verified
- [Grafana Dashboard JSON Model](https://grafana.com/docs/grafana/latest/dashboards/build-dashboards/view-dashboard-json-model/) -- JSON structure reference
- [OTel .NET Runtime Metrics Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/runtime/dotnet-metrics/) -- metric names verified

### Secondary (MEDIUM confidence)
- [Grafana DS_PROMETHEUS __inputs pattern](https://github.com/grafana/grafana/issues/12587) -- community-confirmed pattern, also verified in existing stale dashboard

### Tertiary (LOW confidence)
- GC generation label name (`gc_generation`) -- needs validation against running instance

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - no libraries involved, just JSON file structure verified from existing codebase
- Architecture: HIGH - patterns verified from existing stale dashboard and official Grafana docs
- Metric names: HIGH - verified directly from source code (`PipelineMetricService.cs`, `TelemetryConstants.cs`)
- .NET runtime metrics: MEDIUM - metric names from OTel semantic conventions docs, but exact Prometheus export names depend on OTel SDK version
- Pitfalls: HIGH - derived from codebase analysis (name conversion, resource attributes)

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (stable domain -- Grafana JSON format rarely changes)
