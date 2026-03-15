# Phase 36: Config File Renames - Context

**Gathered:** 2026-03-15
**Status:** Ready for planning

<domain>
## Phase Boundary

Mechanical rename of three config files, their ConfigMap names, config keys, C# constants, and all references. No logic changes. Pure find-and-replace.

</domain>

<decisions>
## Implementation Decisions

### Renames
- `tenantvector.json` → `tenants.json`, ConfigMap `simetra-tenantvector` → `simetra-tenants`
- `oidmaps.json` → `oid_metric_map.json`, ConfigMap `simetra-oidmaps` → `simetra-oid-metric-map`
- `commandmaps.json` → `oid_command_map.json`, ConfigMap `simetra-commandmaps` → `simetra-oid-command-map`

### No discussion needed
- User skipped discussion — renames are clear from requirements
- No logic changes, no gray areas

### Claude's Discretion
- Whether to rename in one commit per file or all three at once
- Whether TenantVectorOptions.SectionName changes (user did not specify)

</decisions>

<specifics>
## Specific Ideas

No specific requirements — mechanical rename.

</specifics>

<deferred>
## Deferred Ideas

None.

</deferred>

---

*Phase: 36-config-file-renames*
*Context gathered: 2026-03-15*
