---
phase: quick-036
plan: 01
subsystem: core-pipeline
tags: [tech-debt, cleanup, dead-code]
dependency-graph:
  requires: []
  provides: [clean-device-registry, accurate-thread-pool-log, accurate-grafana-comments]
  affects: []
tech-stack:
  added: []
  patterns: []
key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/IDeviceRegistry.cs
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
    - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
    - src/SnmpCollector/Services/PollSchedulerStartupService.cs
    - deploy/k8s/production/grafana.yaml
decisions: []
metrics:
  duration: ~3 min
  completed: 2026-03-09
---

# Quick 036: Cleanup Tech Debt -- Orphan, Off-by-one, Comment

Removed orphaned TryGetDevice(IPAddress) and _byIp plumbing, fixed thread pool log off-by-one, updated stale grafana.yaml dashboard references.

## Tasks Completed

| # | Task | Commit | Key Changes |
|---|------|--------|-------------|
| 1 | Remove orphaned TryGetDevice(IPAddress) and _byIp plumbing | 93074a3 | Removed IP-based lookup from interface, implementation, and all test stubs; removed _byIp FrozenDictionary |
| 2 | Fix PollSchedulerStartupService thread pool log off-by-one | b06fa92 | Changed +1 to +2 to account for HeartbeatJob |
| 3 | Update stale grafana.yaml dashboard comments | c3c7ff5 | Replaced npb-device.json/obp-device.json with simetra-business.json |

## Deviations from Plan

None -- plan executed exactly as written.

## Verification

- `dotnet build` succeeds with 0 errors, 0 warnings
- `dotnet test` passes all related tests (132 pass; 2 pre-existing OidMap failures unrelated)
- `grep TryGetDevice(IPAddress` returns zero source code matches
- `grep _byIp` returns zero source code matches
- PollSchedulerStartupService shows `pollJobCount + 2`
- grafana.yaml references simetra-operations.json and simetra-business.json only
