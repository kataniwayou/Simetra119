---
phase: quick-096
plan: 01
subsystem: telemetry
tags: [otel, grafana, kubernetes, namespace]
completed: 2026-03-26
duration: 3m
tasks_completed: 2
tasks_total: 2
dependency_graph:
  requires: []
  provides:
    - k8s.namespace.name OTel resource attribute on all metrics and logs
    - Namespace column in Pod Identity, Tenant Status, Gauge Metrics, Info Metrics tables
  affects: []
tech_stack:
  added: []
  patterns:
    - Downward API env var injection for namespace metadata
key_files:
  created: []
  modified:
    - deploy/k8s/snmp-collector/deployment.yaml
    - ship/deploy/deployment.yaml
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - ship/src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - deploy/grafana/dashboards/simetra-operations.json
    - deploy/grafana/dashboards/simetra-business.json
decisions: []
---

# Quick 096: Add Namespace OTel Resource Attribute Summary

**One-liner:** POD_NAMESPACE Downward API env var feeds k8s.namespace.name OTel resource attribute on metrics and logs, with Namespace columns in all four Grafana tables.

## What Was Done

### Task 1: Add POD_NAMESPACE env var and OTel resource attribute (ed304ba)

- Added `POD_NAMESPACE` env var via Kubernetes Downward API (`metadata.namespace`) in both `deploy/k8s/snmp-collector/deployment.yaml` and `ship/deploy/deployment.yaml`
- Added `podNamespace` variable in `ServiceCollectionExtensions.cs` reading from `POD_NAMESPACE` env var (fallback: "unknown")
- Added `k8s.namespace.name` resource attribute to both the metrics `ConfigureResource` block and the logging `SetResourceBuilder` block
- Both `src/` and `ship/src/` copies kept identical

### Task 2: Add Namespace column to Grafana dashboard tables (3ea1689)

- **Pod Identity table** (simetra-operations.json): Added `k8s_namespace_name` to `count by` clauses in both targets, added Namespace column override, updated `indexByName` (position 2)
- **Tenant Status table** (simetra-operations.json): Added `k8s_namespace_name` to all 9 `max by` / `sum by` clauses (targets A-I), added Namespace column override, updated `indexByName` (position 2, shifted all subsequent indices)
- **Gauge Metrics table** (simetra-business.json): Added Namespace column override after Pod, updated `indexByName` (position 2). No query changes needed -- attribute appears as label automatically.
- **Info Metrics table** (simetra-business.json): Added Namespace column override after Pod, updated `indexByName` (position 2). No query changes needed.

## Verification

- `dotnet build` passes with 0 errors
- Both deployment.yaml files contain `POD_NAMESPACE` with Downward API `fieldRef: metadata.namespace`
- Both ServiceCollectionExtensions.cs files contain `k8s.namespace.name` in metrics and logging resource configs
- Both Grafana dashboard JSON files are valid and contain `k8s_namespace_name` column overrides with "Namespace" displayName
- `src/` and `ship/src/` ServiceCollectionExtensions.cs files are identical

## Deviations from Plan

None -- plan executed exactly as written.
