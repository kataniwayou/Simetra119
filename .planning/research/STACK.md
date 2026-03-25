# Stack Research

**Domain:** Preferred leader election with site-affinity — two-lease mechanism for SNMP monitoring
**Researched:** 2026-03-25
**Confidence:** HIGH

---

## Context

This is a targeted stack addendum for adding preferred leader election to an existing SNMP collector
that already runs K8s Lease-based leader election via `KubernetesClient` 18.0.13. The existing
`K8sLeaseElection` uses `LeaseLock` + `LeaderElector` from `k8s.LeaderElection.*`. The new mechanism
adds a **second, independently managed Lease resource** used solely as a heartbeat stamp — it does
not go through `LeaderElector` at all. All findings are based on codebase inspection plus verification
against NuGet and official Kubernetes API documentation.

---

## Recommended Stack

### Core Technologies

No new NuGet packages are required. All needed APIs exist in the current dependencies.

| Technology | Current Version | Role in Preferred Election | Confidence |
|------------|----------------|---------------------------|------------|
| `KubernetesClient` | 18.0.13 | Direct `CoordinationV1` API calls for the heartbeat lease (create / read / replace / delete) | HIGH |
| `Microsoft.Extensions.Hosting` | 9.0.0 | `BackgroundService` for the `PreferredHeartbeatService` loop | HIGH |
| `Microsoft.Extensions.Options` | 9.0.0 | Config binding for the new `PreferredElectionOptions` config section | HIGH |
| K8s Downward API (pod env vars) | cluster feature | Inject `NODE_NAME` and `POD_NAMESPACE` into the container at runtime | HIGH |

### What the Heartbeat Lease Uses from KubernetesClient

The `IKubernetes` interface already injected into `K8sLeaseElection` exposes all needed operations
through `_kubeClient.CoordinationV1`:

| Operation | Method Signature | When Used |
|-----------|-----------------|-----------|
| Create heartbeat lease | `CreateNamespacedLeaseAsync(V1Lease body, string ns)` | First heartbeat if lease does not exist yet |
| Read heartbeat lease | `ReadNamespacedLeaseAsync(string name, string ns)` | Non-preferred pods check stamp freshness; non-preferred leader checks before yielding |
| Replace (update) heartbeat lease | `ReplaceNamespacedLeaseAsync(V1Lease body, string name, string ns)` | Preferred pod renews its stamp each heartbeat interval |
| Delete heartbeat lease | `DeleteNamespacedLeaseAsync(string name, string ns)` | Preferred pod cleans up on graceful shutdown |

All four methods exist in `KubernetesClient` 18.0.13 — `K8sLeaseElection.StopAsync` already calls
`DeleteNamespacedLeaseAsync` proving the delete path is in use today. `CreateNamespacedLease` and
`ReplaceNamespacedLease` are part of the same generated `CoordinationV1` interface.

**There is no need to upgrade to 19.0.2.** Version 18.0.13 has full `CoordinationV1` support.
19.0.2 (published 2026-02-24) added kubectl-layer conveniences; it does not change the underlying
Lease CRUD API the project depends on. Upgrade only if another dependency forces it.

### V1Lease Fields Used for the Heartbeat Stamp

The `V1LeaseSpec` has these fields relevant to the heartbeat mechanism:

| Field | Type | Purpose in Preferred Election |
|-------|------|-------------------------------|
| `holderIdentity` | `string` | Set to the preferred pod's identity; readers verify this matches `PreferredNode` config |
| `renewTime` | `DateTime?` (MicroTime) | The timestamp the preferred pod last wrote; non-preferred pods compare `UtcNow - renewTime` against the back-off threshold |
| `acquireTime` | `DateTime?` (MicroTime) | When the preferred pod first created the lease; used for the stability gate (`UtcNow - acquireTime >= StabilityGateSeconds`) |
| `leaseDurationSeconds` | `int?` | Set to the heartbeat TTL; used as the staleness threshold so readers know when a stamp is expired |

No new Kubernetes API objects are needed. The existing `V1Lease` / `V1LeaseSpec` / `V1ObjectMeta`
model classes cover the full heartbeat stamp.

---

## Runtime Environment Variable Access

### NODE_NAME — Downward API

The preferred pod is identified by matching the pod's node name against the `PreferredNode` config
value. `spec.nodeName` is exposed via the Kubernetes Downward API as an environment variable.

**Kubernetes manifest injection (required in Deployment spec):**

```yaml
env:
- name: NODE_NAME
  valueFrom:
    fieldRef:
      fieldPath: spec.nodeName
- name: POD_NAMESPACE
  valueFrom:
    fieldRef:
      fieldPath: metadata.namespace
```

**C# read at startup (in `PreferredElectionOptions` PostConfigure or service constructor):**

```csharp
var nodeName = Environment.GetEnvironmentVariable("NODE_NAME");
var podNamespace = Environment.GetEnvironmentVariable("POD_NAMESPACE");
```

`spec.nodeName` is only available via environment variable injection — it is not available through
downward API volume files. This is confirmed by current Kubernetes documentation (March 2025).

### POD_NAMESPACE — Runtime Namespace Resolution

The heartbeat lease must be created in the pod's own namespace, not a hardcoded value. The pod's
namespace comes from `metadata.namespace` via the Downward API (same pattern as `NODE_NAME`).

This means `LeaseOptions.Namespace` (used for the leadership lease) can serve as the fallback, but
the heartbeat lease should read `POD_NAMESPACE` from the environment at runtime and fall back to
`LeaseOptions.Namespace` if the env var is absent (local dev).

**Resolution order for namespace:**
1. `POD_NAMESPACE` environment variable (set by Downward API in-cluster)
2. `LeaseOptions.Namespace` from config (local dev fallback)

This pattern is self-contained: no service account namespace file reads, no `kubectl` calls, no
in-cluster config namespace discovery tricks. `Environment.GetEnvironmentVariable` is sufficient.

---

## New Configuration Section

One new options class is needed. Bind it from a dedicated config section (e.g., `PreferredElection`).

| Config Field | Type | Purpose |
|--------------|------|---------|
| `PreferredNode` | `string?` | Node name the preferred pod runs on; compared against `NODE_NAME`. `null` disables the mechanism entirely (safe default). |
| `HeartbeatLeaseName` | `string` | Name of the second Lease resource (e.g., `snmp-collector-preferred`) |
| `HeartbeatIntervalSeconds` | `int` | How often the preferred pod renews the stamp (e.g., 5) |
| `StaleThresholdSeconds` | `int` | Age of `renewTime` after which non-preferred pods consider the preferred pod gone (e.g., 30) |
| `StabilityGateSeconds` | `int` | How long the preferred pod must have been stamping before non-preferred pods yield (e.g., 60) |
| `BackOffSeconds` | `int` | How long a non-preferred pod waits before competing after losing leadership (e.g., the same as `StaleThresholdSeconds`) |

The namespace for the heartbeat lease is resolved at runtime from `POD_NAMESPACE` env var, not
stored in this config section. This avoids duplicating the namespace config and keeps it consistent
with the existing `LeaseOptions.Namespace`.

---

## What the New Service Looks Like

The `PreferredHeartbeatService` is a `BackgroundService` that:

1. On startup: checks `PreferredNode` config. If `null` or does not match `NODE_NAME`, exits
   immediately — non-preferred pods do not run this service's main loop.
2. If this pod is preferred: waits for a stability gate period, then enters a renewal loop calling
   `ReplaceNamespacedLeaseAsync` (creating the lease first if it does not exist).
3. On `StopAsync`: calls `DeleteNamespacedLeaseAsync` to remove the heartbeat lease, signaling
   to any current non-preferred leader that it can retain leadership cleanly.

The voluntary yield (non-preferred leader deletes the leadership lease when preferred recovers) is
implemented in `K8sLeaseElection` by injecting an `IPreferredStampReader` interface. This reader
is called on a timer inside `K8sLeaseElection.ExecuteAsync` (or a sibling watcher) to check whether
the heartbeat lease is fresh enough to trigger a yield. When yield is triggered, the non-preferred
leader calls `DeleteNamespacedLeaseAsync` on the **leadership** lease and sets `_isLeader = false`.

---

## Alternatives Considered

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| Second plain `V1Lease` as heartbeat stamp | `LeaderElector` with `preferredHolder` field | `preferredHolder` is Alpha-gated behind `CoordinatedLeaderElection` feature gate; not available in standard Kubernetes clusters without explicit enablement. The two-lease pattern works on any K8s version. |
| `ReplaceNamespacedLeaseAsync` for stamp renewal | Re-use `LeaseLock` / `LeaderElector` for the heartbeat lease | `LeaderElector` is designed for contested election; heartbeat is a unilateral write by the preferred pod only. Using `LeaderElector` would add unnecessary retry/backoff logic to an uncontested write. |
| `POD_NAMESPACE` env var (Downward API) for namespace | In-cluster config namespace file (`/var/run/secrets/kubernetes.io/serviceaccount/namespace`) | The service account namespace file works but adds a file read dependency. The Downward API env var approach is consistent with how `NODE_NAME` is already injected and does not require filesystem access. |
| `NODE_NAME` env var compared against `PreferredNode` config | Label/annotation selector on the pod | Env var is simpler — no Kubernetes API call needed to determine if this pod is preferred. The pod knows its own node at startup. |
| `StabilityGateSeconds` before stamping | Stamp immediately on startup | Without a gate, a preferred pod that flaps (crash-loop) could destabilize leadership every restart. The gate ensures the preferred pod is running stably before non-preferred pods yield. |

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Any new NuGet package | Nothing is needed that isn't already in `KubernetesClient` 18.0.13 | Direct `CoordinationV1` API calls |
| `KubernetesClient` upgrade to 19.0.2 | 19.0.2 adds kubectl-layer helpers irrelevant to this feature; upgrading risks transitive dependency churn without benefit | Stay on 18.0.13 unless forced by another dependency |
| `preferredHolder` / `strategy` fields on `V1LeaseSpec` | These are Alpha-gated features (`CoordinatedLeaderElection` gate); they require explicit feature gate enablement and are not stable as of Kubernetes 1.35 | Two-lease pattern using standard `V1Lease` resources |
| Shared `LeaderElectionConfig` between leadership and heartbeat leases | `LeaderElectionConfig` applies contest semantics (retry, renew deadline); heartbeat is a solo write | Direct `CoordinationV1` API calls for heartbeat |
| Config-driven namespace for heartbeat lease | Would duplicate `LeaseOptions.Namespace` and create drift risk in production vs dev | Read `POD_NAMESPACE` from Downward API env var at runtime |

---

## Version Compatibility

| Package | Version in Csproj | Notes |
|---------|------------------|-------|
| `KubernetesClient` | 18.0.13 | All four `CoordinationV1` Lease CRUD methods available. No upgrade needed. |
| `Microsoft.Extensions.Hosting` | 9.0.0 | `BackgroundService` for `PreferredHeartbeatService`. No change. |
| `Microsoft.Extensions.Options.DataAnnotations` | 9.0.0 | Validates the new `PreferredElectionOptions` config section. No change. |

No new package references are added to `SnmpCollector.csproj`.

---

## Installation

No changes to `SnmpCollector.csproj`.

The only deployment change required is adding `NODE_NAME` and `POD_NAMESPACE` Downward API
environment variables to the Kubernetes Deployment manifest:

```yaml
env:
- name: NODE_NAME
  valueFrom:
    fieldRef:
      fieldPath: spec.nodeName
- name: POD_NAMESPACE
  valueFrom:
    fieldRef:
      fieldPath: metadata.namespace
```

And adding the `PreferredElection` config section to `appsettings.Production.json` (or the
ConfigMap that provides it):

```json
"PreferredElection": {
  "PreferredNode": "site-a-node",
  "HeartbeatLeaseName": "snmp-collector-preferred",
  "HeartbeatIntervalSeconds": 5,
  "StaleThresholdSeconds": 30,
  "StabilityGateSeconds": 60,
  "BackOffSeconds": 30
}
```

When `PreferredNode` is `null` or absent, the mechanism is fully disabled and the pod behaves
exactly as it does today.

---

## Sources

- Codebase `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — existing `CoordinationV1.DeleteNamespacedLeaseAsync` call pattern (HIGH)
- Codebase `src/SnmpCollector/SnmpCollector.csproj` — `KubernetesClient` 18.0.13 confirmed (HIGH)
- Codebase `src/SnmpCollector/Configuration/LeaseOptions.cs` — existing lease config pattern (HIGH)
- NuGet Gallery https://www.nuget.org/packages/KubernetesClient/ — latest version 19.0.2, published 2026-02-24; 18.0.13 is the current project version (HIGH)
- Kubernetes API reference https://kubernetes.io/docs/reference/kubernetes-api/cluster-resources/lease-v1/ — `V1LeaseSpec` fields: `holderIdentity`, `renewTime`, `acquireTime`, `leaseDurationSeconds`, `leaseTransitions`, `preferredHolder` (Alpha), `strategy` (Alpha) (HIGH)
- Kubernetes Downward API docs https://kubernetes.io/docs/concepts/workloads/pods/downward-api/ — `spec.nodeName` and `metadata.namespace` available as env vars via `fieldRef`; `spec.nodeName` is env-var only, not available as volume file (HIGH)
- kubernetes-client/csharp GitHub releases https://github.com/kubernetes-client/csharp/releases — v19.0.2 change summary confirms no `CoordinationV1` API changes vs 18.x (MEDIUM)

---
*Stack research for: preferred leader election with two-lease site-affinity mechanism*
*Researched: 2026-03-25*
