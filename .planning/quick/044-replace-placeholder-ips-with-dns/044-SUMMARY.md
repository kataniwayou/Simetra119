---
phase: Q044
plan: 01
subsystem: tenant-vector
tags: [dns, configmap, tenant-vector, e2e]
dependency-graph:
  requires: [D29-01, D29-02, Q042, Q043]
  provides: [dns-based-tenant-vector-config]
  affects: []
tech-stack:
  added: []
  patterns: [dns-resolution-at-reload]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - deploy/k8s/snmp-collector/simetra-tenantvector.yaml
    - deploy/k8s/production/configmap.yaml
    - tests/e2e/scenarios/28-tenantvector-routing.sh
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
decisions:
  - id: Q044-D1
    choice: "ResolveIp iterates AllDevices matching on ConfigAddress"
    why: "Simple O(n) scan sufficient for small device count; avoids adding new IDeviceRegistry method"
metrics:
  duration: "~5 min"
  completed: 2026-03-11
---

# Quick Task 044: Replace Placeholder IPs with DNS Names Summary

DNS-based tenant vector config with ResolveIp() resolution via DeviceRegistry.AllDevices at Reload() time.

## What Changed

### Task 1: Add ResolveIp() to TenantVectorRegistry

Added `ResolveIp(string configIp)` helper that iterates `IDeviceRegistry.AllDevices` looking for a device whose `ConfigAddress` matches the config IP (case-insensitive). Returns the device's `ResolvedIp` if found, otherwise falls back to returning the input unchanged.

Updated `Reload()` loop so:
- `MetricSlotHolder` receives the resolved IP (for routing index)
- `DeriveIntervalSeconds` still receives the raw config address (for DeviceRegistry lookup via `TryGetByIpPort`)
- Carry-over lookup key uses resolved IP (matching existing holders)

New test `Reload_DnsName_ResolvedViaDeviceRegistry` proves:
- `TryRoute("10.0.0.99", 161, "test_metric")` returns true (resolved IP in routing)
- `TryRoute("dns.test.local", 161, "test_metric")` returns false (raw DNS excluded)

All 17 TenantVectorRegistry tests pass.

### Task 2: Replace Placeholders in ConfigMaps and Simplify E2E

- Replaced all `PLACEHOLDER_NPB_IP` with `npb-simulator.simetra.svc.cluster.local` in both dev and production ConfigMaps
- Replaced all `PLACEHOLDER_OBP_IP` with `obp-simulator.simetra.svc.cluster.local`
- Removed ClusterIP derivation (kubectl get svc), empty-check block, and sed substitution from e2e scenario 28
- Hot-reload heredoc now uses DNS names directly instead of `${NPB_IP}` / `${OBP_IP}` shell variables
- Cleanup restore simplified to direct `kubectl apply -f` (no sed needed)
- Updated STATE.md architectural facts

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | 5d50d05 | feat(Q044): add ResolveIp() to TenantVectorRegistry for DNS-to-IP resolution |
| 2 | 1883041 | feat(Q044): replace PLACEHOLDER IPs with DNS names in tenant vector ConfigMaps |

## Deviations from Plan

None - plan executed exactly as written.

## Verification Results

- `dotnet test --filter TenantVectorRegistry` -- 17/17 pass
- `dotnet build src/SnmpCollector/` -- clean build, 0 warnings
- No PLACEHOLDER_NPB_IP or PLACEHOLDER_OBP_IP anywhere in deploy/ or tests/
- DNS names present in both dev and production ConfigMaps
- Scenario 28 has no ClusterIP derivation or sed substitution
