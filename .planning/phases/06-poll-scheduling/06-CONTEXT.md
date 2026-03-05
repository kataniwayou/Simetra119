# Phase 6: Poll Scheduling - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Quartz executes SNMP GET polls on configured intervals per device, publishes results to MediatR via ISender.Send, handles device unreachability gracefully, and the thread pool scales to the total job count without starvation. Job registration, timeout, and unreachability tracking are all in scope. Leader-gated export is Phase 7.

</domain>

<decisions>
## Implementation Decisions

### Unreachability policy
- Consecutive failure threshold: 3 failures before marking unreachable
- Polling behavior when unreachable: keep polling on schedule (no backoff) — detect recovery immediately when device comes back
- Recovery: immediate — one successful response resets to healthy, resume normal metrics
- Observability: counter per event — `snmp.poll.unreachable` increments on transition to unreachable, `snmp.poll.recovered` on transition back to healthy

### Poll result handling
- Dispatch: one SnmpOidReceived per OID — each varbind from GET response dispatched individually via ISender.Send, consistent with trap path
- Partial responses: publish what we got — send SnmpOidReceived for each returned OID, don't penalize device for missing OIDs
- Source tag: SnmpSource.Poll for poll-originated events (matching SnmpSource.Trap for traps)
- sysUpTime: polled in the same GET request as OIDs — prepend sysUpTime OID to every poll's OID list for atomic snapshot with less network overhead

### Timeout and failure logging
- Individual poll timeouts: Warning level — each timeout is a Warning, visible in default log filters
- Successful polls: no per-poll log — rely on snmp.poll.executed counter + pipeline LoggingBehavior to avoid log flood
- Unreachable transition: device name + IP + consecutive failure count — "Device {Name} ({Ip}) unreachable after {N} consecutive failures"
- Startup: Information level — "Registered {N} poll jobs across {M} devices, thread pool size: {T}" for capacity validation

### SNMP GET mechanics
- GET strategy: single GET with all OIDs per poll group — fewer packets, atomic response
- Community string: same as traps — DeviceInfo.CommunityString with SnmpListenerOptions.CommunityString fallback, consistent auth model
- Thread pool: auto-calculate from job count — no manual config needed (Claude picks formula)
- Concurrency: DisallowConcurrentExecution on MetricPollJob — Quartz skips fire if previous instance still running, prevents pile-up on slow devices

### Claude's Discretion
- Thread pool sizing formula (e.g., jobs * 1.5, jobs + 2, etc.)
- Exact Quartz scheduler configuration details
- How to handle SNMP noSuchObject / noSuchInstance varbind errors
- Internal structure of the failure counter (per-device tracking mechanism)
- Whether sysUpTime value is set on SnmpOidReceived before or after dispatch

</decisions>

<specifics>
## Specific Ideas

- Poll timeout is 80% of interval (from ROADMAP SC#2) — leaves response window before next scheduled fire
- Quartz job identity follows established convention: `metric-poll-{deviceName}-{pollIndex}` (from Phase 2 DEVC-04)
- snmp.poll.executed must increment after every completed poll regardless of success/failure (ROADMAP SC#4)
- Same ISender.Send path as traps — behaviors (Logging, Exception, Validation, OidResolution) all execute

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 06-poll-scheduling*
*Context gathered: 2026-03-05*
