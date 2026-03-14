# Phase 34: CommunityString Validation & MetricPollJob Cleanup - Context

**Gathered:** 2026-03-14
**Status:** Ready for planning

<domain>
## Phase Boundary

Validate every CommunityString in every config layer (devices + tenants) at load time. Skip invalid entries with structured Error logs. Catch duplicate IP+Port on devices. Skip zero-OID poll groups. Enforce tenant completeness (TEN-13: at least 1 Resolved + 1 Evaluate + 1 Command after validation). Validate Role, ValueType, and MetricName resolution on tenant entries. All validation is per-entry skip — never reject an entire reload.

</domain>

<decisions>
## Implementation Decisions

### Skip vs Reject Semantics
- **Per-entry skip everywhere** — never reject an entire config reload for one bad entry
- Invalid CommunityString on device = skip that device only; other devices load normally
- Invalid tenant metric/command entry (bad CommunityString via IP+Port lookup, unresolvable MetricName, invalid Role, invalid ValueType) = skip that entry only; sibling entries in same tenant unaffected
- Structural failures (null Ip, port out of range, empty MetricName) = skip that entry only; consistent with semantic failures
- **TEN-13 is a post-validation gate**: after all individual entries are validated and bad ones skipped, check if the tenant still has ≥1 Resolved metric + ≥1 Evaluate metric + ≥1 command. If not, skip the entire tenant with Error log.

### Tenant Completeness Rules (TEN-13)
- TEN-13 runs AFTER individual entry validation — skipped entries reduce the count
- If individual skips cause the tenant to fall below threshold, the whole tenant is dropped
- Error log is specific: names which role/requirement is missing (e.g. "no Evaluate metrics remaining after validation")
- Invalid Role (not "Evaluate" or "Resolved") = skip that metric entry with Error log, consistent with all other per-entry validation

### Duplicate Device Handling (DEV-10)
- **IP+Port is the real device identity** — not the CommunityString/name
- Same CommunityString + different IP+Port = both load normally, **Warning log** only (operator awareness)
- Same IP+Port (regardless of CommunityString) = skip second device, Error log (existing validation, real conflict)
- DEV-10 is a Warning, not a validation error — no devices are skipped for duplicate CommunityString alone

### Validation Ordering
- **All per-entry validation happens in Reload/load loops** — single pass per entry
- `TenantVectorOptionsValidator` stays minimal (JSON parsed, Tenants array exists) — NOT per-entry
- `DevicesOptionsValidator` keeps existing IP+Port duplicate check; CommunityString validation added to DeviceRegistry constructor/ReloadAsync loop
- No split between validator and registry — avoids redundant code and complexity
- Validation order within Reload for each tenant entry:
  1. Structural: non-empty Ip, port 1-65535, non-empty MetricName/CommandName
  2. Role validation: must be "Evaluate" or "Resolved"
  3. ValueType validation: must be "Integer32" / "IpAddress" / "OctetString"
  4. Value validation: non-empty
  5. Semantic: MetricName resolved against OidMap (TEN-05), IP+Port found in DeviceRegistry (TEN-07)
  6. Post-loop: TEN-13 completeness gate

### Trap Listener Consistency (CS-06)
- Trap listener continues using `Simetra.*` pattern extraction unchanged — verified in Phase 33
- No changes to SnmpTrapListenerService in this phase

### Operator Config Ordering (CS-07)
- Document recommended ordering: oidmaps/commandmaps → devices → tenants
- Each file has independent watcher — no cross-watcher coupling
- Operator responsible for alignment

### Claude's Discretion
- Exact structured log field names and format
- Whether to use a shared validation helper method or inline checks
- Test organization and naming conventions
- Whether DevicesOptionsValidator gains CommunityString format check or it stays only in DeviceRegistry

</decisions>

<specifics>
## Specific Ideas

- Error log for TEN-13 must be specific: "Tenant 'primary' skipped: no Evaluate metrics remaining after validation" — not a generic message
- Warning log for duplicate CommunityString: "Devices[2] CommunityString 'Simetra.NPB-01' also used by Devices[0] — both loaded (different IP+Port)"
- All skip logs should include enough context for the operator to find and fix the config entry (index, field value, reason)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 34-communitystring-validation-and-metricpolljob-cleanup*
*Context gathered: 2026-03-14*
