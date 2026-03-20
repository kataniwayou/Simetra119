---
phase: quick
plan: 080
subsystem: e2e-testing
tags: [kubectl, configmap, k8s-watch, double-reload]

key-files:
  modified:
    - tests/e2e/lib/kubectl.sh

duration: 5min
completed: 2026-03-20
---

# Quick Task 080: Fix ConfigMap Restore Double-Reload

**Strip `kubectl.kubernetes.io/last-applied-configuration` annotation from configmap snapshots and use `kubectl replace` for restore to prevent K8s from firing two Modified events.**

## Root Cause

`kubectl apply -f snapshot.yaml` caused K8s to fire two Modified events:
1. First event: data update (correct) → tenants loaded
2. Second event: `last-applied-configuration` annotation update → watcher re-reads configmap with stale annotation data → ValidateAndBuildTenants produces 0 tenants

The annotation contained the PREVIOUS scenario's fixture data (e.g., `e2e-tenant-A`), not the restored data. This wiped the tenant registry to 0 holders after every restore.

## Fix

1. `save_configmap`: strip all `kubectl.kubernetes.io` annotations and empty `annotations: {}` key
2. `restore_configmap`: use `kubectl replace` (single Modified event, no annotation update) with `kubectl apply` fallback

## Task Commits

1. **Task 1: Fix kubectl.sh save/restore** - `baea8ae` (fix)

## Verification

- Stale `.original-*-configmap.yaml` snapshot files removed
- Base configmaps re-applied to cluster

---
*Completed: 2026-03-20*
