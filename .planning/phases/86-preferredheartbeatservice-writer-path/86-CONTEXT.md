# Phase 86: PreferredHeartbeatService Writer Path and Readiness Gate - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Add the writer path to the existing `PreferredHeartbeatJob`. The preferred pod creates/renews the heartbeat lease so non-preferred pods can detect it via the reader (Phase 85). Writing is gated by `IsPreferredPod` and readiness. Shutdown uses TTL expiry (no explicit delete).

</domain>

<decisions>
## Implementation Decisions

### Readiness gate mechanism
- Use `IHostApplicationLifetime.ApplicationStarted` — fires when all hosted services start, including Quartz scheduler
- "Ready" means scheduler is working — no need to wait for first poll data
- Check once at startup: register callback that sets a bool. Job checks bool on every tick — fast after first pass.
- Before `ApplicationStarted` fires, writer is a no-op even on the preferred pod

### Lease creation vs update
- Preferred pod always overwrites the heartbeat lease regardless of existing holderIdentity — it's a heartbeat stamp, not an election
- Claude's discretion on exact K8s API pattern (try-read-then-create-or-update vs create-or-replace)

### Writer gating behavior
- Non-preferred pods: silent skip, no log — reader path runs every tick, that's enough activity
- Execution order in PreferredHeartbeatJob: write first (if preferred + ready), then read. Preferred pod stamps then reads its own stamp (confirms freshness immediately). Non-preferred skips write, reads.

### Shutdown
- TTL expiry, NOT explicit delete (already locked from earlier discussion)
- No changes to GracefulShutdownService — Quartz standby stops the job, lease expires naturally via DurationSeconds

### Claude's Discretion
- Exact K8s API pattern for create-or-replace (try/catch 409, or read-then-create-or-update)
- Where to store the ApplicationStarted readiness bool (on PreferredLeaderService or on the job itself)
- `leaseDurationSeconds` value on the heartbeat lease object (should match LeaseOptions.DurationSeconds)

</decisions>

<specifics>
## Specific Ideas

- The writer adds to the existing `PreferredHeartbeatJob.Execute` method — write before read
- Heartbeat lease name: `"{LeaseOptions.Name}-preferred"` (already used by reader)
- Lease spec fields: `holderIdentity` = pod identity, `renewTime` = DateTime.UtcNow, `leaseDurationSeconds` = DurationSeconds
- `acquireTime` set on first create only

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 86-preferredheartbeatservice-writer-path*
*Context gathered: 2026-03-26*
