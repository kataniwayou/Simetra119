---
phase: quick-019
plan: 01
subsystem: repo-structure
tags: [cleanup, k8s, reorganization]
dependency-graph:
  requires: []
  provides: [reference-directory, clean-k8s-manifests, snmp-collector-naming]
  affects: [deploy/k8s/production]
tech-stack:
  added: []
  patterns: [reference-code-separation]
file-tracking:
  key-files:
    created:
      - reference/simetra/ (moved from src/Simetra/)
      - reference/Simetra.sln (moved from root)
    modified:
      - deploy/k8s/production/deployment.yaml
      - deploy/k8s/production/service.yaml
      - deploy/k8s/production/service-nodeports.yaml
    deleted:
      - deploy/k8s/deployment.yaml
      - deploy/k8s/configmap.yaml
      - deploy/k8s/service.yaml
      - src/Simetra/ (moved to reference/)
      - Simetra.sln (moved to reference/)
decisions: []
metrics:
  duration: ~3 minutes
  completed: 2026-03-08
---

# Quick Task 019: Reorganize Simetra Files and Cleanup K8s Summary

Moved Simetra reference project to reference/ directory, deleted 3 duplicate top-level K8s manifests, and renamed simetra to snmp-collector in production K8s YAMLs while preserving namespace and configmap naming.

## What Was Done

### Task 1: Move Simetra source to reference/ directory
- Moved `src/Simetra/` to `reference/simetra/`
- Moved `Simetra.sln` to `reference/Simetra.sln`
- Updated .sln project path from `src\Simetra\Simetra.csproj` to `simetra\Simetra.csproj`
- Removed "src" solution folder entry and NestedProjects section
- Removed bin/ and obj/ build artifacts from reference
- Commit: `e6f4182`

### Task 2: Delete duplicate top-level K8s manifests
- Removed `deploy/k8s/deployment.yaml` (duplicate of snmp-collector/deployment.yaml)
- Removed `deploy/k8s/configmap.yaml` (stale simetra-named configmaps)
- Removed `deploy/k8s/service.yaml` (duplicate service definition)
- Kept shared infrastructure: namespace.yaml, rbac.yaml, serviceaccount.yaml
- Commit: `c50e908`

### Task 3: Rename simetra to snmp-collector in production K8s YAMLs
- deployment.yaml: metadata.name, app labels, container name, image all changed to snmp-collector
- service.yaml: name, app labels, selector changed to snmp-collector
- service-nodeports.yaml: SNMP trap service renamed to snmp-collector-snmp-traps with updated labels
- Namespace remains `simetra` throughout all manifests
- ConfigMap names unchanged (simetra-config, simetra-oidmaps, simetra-devices)
- ServiceAccount unchanged (simetra-sa)
- Other NodePort services (prometheus, elasticsearch, grafana) untouched
- Commit: `f3844f6`

## Deviations from Plan

**1. [Rule 3 - Blocking] src/Simetra/ and Simetra.sln were untracked**
- Plan specified `git mv` but files were not tracked in git
- Used regular `mv` + `git add` instead
- Also removed bin/ and obj/ build artifacts that would have been committed

**2. [Rule 3 - Blocking] deploy/k8s/service.yaml was untracked**
- Plan specified `git rm` but file was not tracked
- Used `rm -f` instead

## Verification

- `reference/simetra/` exists with all source files
- `reference/Simetra.sln` has correct project path (`simetra\Simetra.csproj`)
- `src/Simetra/` no longer exists
- `deploy/k8s/` has exactly 3 YAML files (namespace, rbac, serviceaccount)
- No `app: simetra` labels in production deployment
- `namespace: simetra` preserved in all production manifests
- `dotnet build src/SnmpCollector/SnmpCollector.sln` succeeds (active project untouched)
