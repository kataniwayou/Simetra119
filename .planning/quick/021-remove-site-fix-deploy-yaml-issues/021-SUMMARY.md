---
phase: quick-021
plan: 01
subsystem: configuration, deploy
tags: [rename, options, configmap, yaml, cleanup]
dependency-graph:
  requires: []
  provides:
    - PodIdentityOptions replaces SiteOptions across codebase
    - Production ConfigMap matches C# schema exactly
    - Dev service exposes SNMP trap UDP port
    - Duplicate deploy files removed
  affects: []
tech-stack:
  added: []
  patterns: []
file-tracking:
  key-files:
    created:
      - src/SnmpCollector/Configuration/PodIdentityOptions.cs
      - src/SnmpCollector/Configuration/Validators/PodIdentityOptionsValidator.cs
    modified:
      - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
      - src/SnmpCollector/Telemetry/K8sLeaseElection.cs
      - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
      - tests/SnmpCollector.Tests/Pipeline/Behaviors/ExceptionBehaviorTests.cs
      - tests/SnmpCollector.Tests/Pipeline/Behaviors/LoggingBehaviorTests.cs
      - deploy/k8s/snmp-collector/configmap.yaml
      - deploy/k8s/snmp-collector/service.yaml
      - deploy/k8s/production/configmap.yaml
    deleted:
      - src/SnmpCollector/Configuration/SiteOptions.cs
      - src/SnmpCollector/Configuration/Validators/SiteOptionsValidator.cs
      - deploy/k8s/production/namespace.yaml
      - deploy/k8s/production/rbac.yaml
      - deploy/k8s/production/serviceaccount.yaml
      - deploy/grafana/provisioning/datasources/prometheus.yaml
decisions: []
metrics:
  duration: "4m"
  completed: "2026-03-08"
---

# Quick 021: Remove Site, Fix Deploy YAML Issues Summary

Renamed SiteOptions to PodIdentityOptions (dropping unused Name property), fixed production ConfigMap schema mismatches, added SNMP trap UDP port to dev service, and deleted 4 duplicate deploy files.

## Tasks Completed

| # | Task | Commit | Status |
|---|------|--------|--------|
| 1 | Rename SiteOptions to PodIdentityOptions across all C# code | e8914ac | Done |
| 2 | Fix dev ConfigMap and service YAML | b2f0c67 | Done |
| 3 | Fix production ConfigMap | 0e6f1ac | Done |
| 4 | Delete duplicate deploy files | 2e880d9 | Done |

## What Was Done

### Task 1: SiteOptions to PodIdentityOptions Rename
- Created `PodIdentityOptions` class with only `PodIdentity` property (removed `Name`)
- Created `PodIdentityOptionsValidator` (replaces `SiteOptionsValidator`)
- Deleted `SiteOptions.cs` and `SiteOptionsValidator.cs`
- Updated `ServiceCollectionExtensions.cs`: DI binding, PostConfigure, validator registration
- Updated `K8sLeaseElection.cs`: field, constructor param, and usage renamed
- Updated 3 test files: replaced `new SiteOptions { Name = "test-site" }` with `new PodIdentityOptions { PodIdentity = "test-pod" }`
- Zero `SiteOptions` references remain in `src/` and `tests/`

### Task 2: Dev ConfigMap and Service
- Replaced `"Site": { "Name": "site-lab-k8s" }` with `"PodIdentity": {}`
- Removed redundant `"OidMap": {}` and `"Devices": []` stubs (loaded by separate watchers)
- Added UDP port 10162 (snmp-trap) to dev service alongside existing TCP 8080 (health)

### Task 3: Production ConfigMap
- Fixed `metadata.name` from `simetra-config` to `snmp-collector-config` (matches deployment projected volumes)
- Replaced `Site` section with empty `PodIdentity` section
- Removed `CommunityString` from `SnmpListener` (belongs on `DeviceOptions`)
- Fixed `ServiceName` from `simetra-supervisor` to `snmp-collector`
- Added `Lease` section with Name, Namespace, DurationSeconds, RenewIntervalSeconds
- Fixed `devices.json` schema: flat `Oids` string array + `IntervalSeconds` (removed fictitious MetricName, MetricType, StaticLabels, object Oid entries)
- Updated all YAML comments for accuracy

### Task 4: Duplicate File Deletion
- `deploy/k8s/production/namespace.yaml` (duplicate of `deploy/k8s/namespace.yaml`)
- `deploy/k8s/production/rbac.yaml` (duplicate of base RBAC)
- `deploy/k8s/production/serviceaccount.yaml` (duplicate of `deploy/k8s/serviceaccount.yaml`)
- `deploy/grafana/provisioning/datasources/prometheus.yaml` (superseded by `simetra-prometheus.yaml`)

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build` | 0 errors, 2 pre-existing warnings (K8s WatchAsync deprecation) |
| `dotnet test` | 138 passed, 0 failed |
| `grep SiteOptions src/ tests/` | 0 matches |
| `grep "Site" deploy/*.yaml` | 0 matches |
| Production configmap name | `snmp-collector-config` |
| Dev service UDP 10162 | Present |

## Deviations from Plan

None -- plan executed exactly as written.
