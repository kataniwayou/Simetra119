# Phase 85: PreferredHeartbeatService Reader Path - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Non-preferred pods poll the heartbeat lease and maintain an in-memory `IsPreferredStampFresh` volatile bool. This is the reader side of the two-lease mechanism. The writer (Phase 86) is not in scope — this phase only reads.

Note: Phase scope changed from original roadmap. The heartbeat mechanism uses a single Quartz job (`PreferredHeartbeatJob`) that handles both reading and writing (gated by `IsPreferredPod`). This phase implements the reader path of that job only. The writer path lands in Phase 86.

</domain>

<decisions>
## Implementation Decisions

### Job architecture
- Single `PreferredHeartbeatJob` Quartz job handles both reading and writing (not two separate jobs)
- Read always runs on all pods. Write is gated by `IsPreferredPod` (Phase 86 scope).
- Job reads its interval from `PreferredHeartbeatJob.IntervalSeconds` in appsettings (same pattern as HeartbeatJob, SnapshotJob)
- `PreferredLeaderService` stays as a singleton holding state — NOT a BackgroundService. Quartz job updates it.
- Job stamps liveness via `ILivenessVectorService` (same pattern as all other Quartz jobs)

### Polling lifecycle
- All pods poll (including preferred pod) — preferred pod sees its own stamp as fresh (harmless)
- Polling starts immediately on pod startup — no readiness gate for the reader
- On graceful shutdown: stop immediately via CancellationToken (Quartz standby in GracefulShutdownService handles this)

### Freshness computation
- Freshness threshold: `DurationSeconds + 5s` (hardcoded clock-skew tolerance, not configurable)
- `DurationSeconds` comes from `LeaseOptions.DurationSeconds` (shared with leadership lease)
- Stamp time = `renewTime` from heartbeat lease spec. Fall back to `acquireTime` if `renewTime` is null (lease just created).
- 404 (lease not found) = stale (IsPreferredStampFresh = false, no exception)
- K8s API temporarily unreachable = keep last known value (avoids flapping on transient failures)

### State transitions
- `IsPreferredStampFresh` is a `volatile bool` (same pattern as `_isLeader` in K8sLeaseElection)
- Initial value: `false` (stale) — non-preferred pods compete freely at startup, preferred pod wins through normal election
- No debounce — single stale read flips to false immediately. The DurationSeconds + 5s threshold already provides tolerance.
- Log only on state transitions (fresh→stale, stale→fresh) at Info level — not every poll

### PreferredLeaderService evolution
- PreferredLeaderService remains a singleton (not BackgroundService)
- Quartz job injects PreferredLeaderService and calls a method to update the volatile bool
- No IHostedService registration needed — Quartz handles the scheduling lifecycle

### Claude's Discretion
- Method name on PreferredLeaderService for updating stamp freshness (e.g. UpdateStampFreshness(bool))
- PreferredHeartbeatJob options class naming
- Exact Quartz job registration pattern (trigger builder, job builder)

</decisions>

<specifics>
## Specific Ideas

- PreferredHeartbeatJob follows exact same Quartz registration pattern as HeartbeatJob/SnapshotJob
- Job config in appsettings: `"PreferredHeartbeatJob": { "IntervalSeconds": 10 }`
- LeaseOptions.RenewIntervalSeconds is independent — controls LeaderElector library RetryPeriod only
- PreferredHeartbeatJob.IntervalSeconds controls the Quartz job cadence for heartbeat lease read+write

</specifics>

<deferred>
## Deferred Ideas

- Writer path (gated by IsPreferredPod) — Phase 86
- Readiness gate for writer — Phase 86
- Heartbeat lease shutdown cleanup — Phase 86

</deferred>

---

*Phase: 85-preferredheartbeatservice-reader-path*
*Context gathered: 2026-03-26*
