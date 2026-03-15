# Phase 39: Pipeline Bypass Guards - Context

**Gathered:** 2026-03-15
**Status:** Ready for planning

<domain>
## Phase Boundary

Add SnmpSource.Synthetic enum member, bypass OidResolutionBehavior for synthetic messages, ensure ValidationBehavior passes sentinel OID "0.0". Three small changes that unlock Phase 40 synthetic dispatch.

</domain>

<decisions>
## Implementation Decisions

### All Decisions Pre-Locked (from STATE.md)
- **SnmpSource.Synthetic** — new enum member alongside Poll and Trap
- **Bypass guard:** Option B — `if (msg.Source == SnmpSource.Synthetic) { await next(); return; }` in OidResolutionBehavior, before the existing OID resolution logic
- **Sentinel OID:** `"0.0"` — passes existing ValidationBehavior regex `^\d+(\.\d+){1,}$` without any changes to ValidationBehavior
- **No changes to ValidationBehavior** — the regex already accepts "0.0"
- Existing Poll and Trap messages are completely unaffected

### Claude's Discretion
- Exact placement of the guard within OidResolutionBehavior (before or after the null-check)
- Test file organization (extend existing behavior tests or new file)

</decisions>

<specifics>
## Specific Ideas

No specific requirements — decisions are locked.

</specifics>

<deferred>
## Deferred Ideas

None.

</deferred>

---

*Phase: 39-pipeline-bypass-guards*
*Context gathered: 2026-03-15*
