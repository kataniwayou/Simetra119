---
phase: quick-097
plan: 01
subsystem: telemetry
tags: [logging, console-formatter, otel, namespace, correlationId]
dependency-graph:
  requires: [quick-096]
  provides: [deduplicated-correlationId, namespace-in-console-logs, namespace-in-otel-enrichment]
  affects: []
tech-stack:
  added: []
  patterns: [env-var-namespace-injection]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs
    - src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs
    - ship/src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs
    - ship/src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs
decisions: []
metrics:
  duration: ~2 min
  completed: 2026-03-26
---

# Quick 097: Fix Duplicate CorrelationId and Add Namespace to Logs

**One-liner:** Deduplicate correlationId in console log prefix and insert POD_NAMESPACE between podName and role.

## What Was Done

### Task 1: Fix SnmpConsoleFormatter (8ca7885)

- Added `POD_NAMESPACE` env var resolution and inserted namespace segment between podName and role
- Changed operationId condition to only append when it differs from globalId (`string.Equals` ordinal check)
- Updated XML doc comment to reflect new format: `[host|pod|namespace|role|correlationId]`
- Synced ship/ copy

### Task 2: Add namespace to SnmpLogEnrichmentProcessor (62b63c9)

- Added `_podNamespace` field resolved from `POD_NAMESPACE` env var in constructor
- Added `pod_namespace` attribute to OTel log record enrichment in `OnEnd()`
- Increased attribute list capacity from 3 to 4
- Synced ship/ copy

### Task 3: Build, deploy, and verify logs

- Docker build succeeded from repo root
- Deployed to simetra-site-a via scale 0/1 restart
- Verified logs show 5-segment prefix: `[docker-desktop|snmp-collector-xxx|simetra-site-a|leader|{guid}]`
- No duplicate GUIDs in any log line during normal poll cycles

## Deviations from Plan

None -- plan executed exactly as written.

## Verification

- Console logs show 5-segment prefix with namespace between podName and role
- No duplicate GUIDs visible in any log line
- Both src/ and ship/ telemetry files are identical (verified with diff)
- Docker build succeeded, pod running healthy
