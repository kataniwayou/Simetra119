# Phase 10: Metrics Redesign — UAT

**Date:** 2026-03-06
**Tester:** User
**Status:** Complete — 6/8 PASS, 2 ISSUES

## Tests

| # | Test | Expected | Status |
|---|------|----------|--------|
| 1 | Metric names and labels | All metric names and label keys listed | PASS |
| 2 | snmp_gauge vs snmp_info labels | Same base labels, snmp_info adds `value` | PASS |
| 2.1 | `ip` is a separate label | `ip` label present on all business metrics | PASS |
| 2.2 | Runtime metrics contain host_name | All 11 pipeline counters tagged with `host_name` | ISSUE |
| 3 | Log pattern uses host_name | Console formatter shows hostname, not site_name | PASS |
| 4 | Same community string flow for traps/polls | Both use CommunityStringHelper convention | ISSUE |
| 5 | OID map sets metric_name or fallback | MetricName set from OidMap or "Unknown" | PASS |
| 6 | Flow description for traps and polls | End-to-end paths described | PASS |

## Results

### Test 1 — PASS
Metric names and labels verified across SnmpMetricFactory and PipelineMetricService.

### Test 2 — PASS
snmp_gauge and snmp_info share 7 base labels; snmp_info adds `value` (truncated at 128 chars).

### Test 2.1 — PASS
`ip` is a standalone label on both snmp_gauge and snmp_info.

### Test 2.2 — ISSUE (severity: medium)
host_name resolution uses `HOSTNAME` env var (= pod name in K8s, changes every restart) or `MachineName` (= container hostname). User has multiple servers each running one K8s + one collector, all pushing to one Prometheus. Needs **physical server identity** (persistent, unique across servers, no human config).
**Fix:** Use Downward API `spec.nodeName` → `NODE_NAME` env var. Code resolution: `NODE_NAME → MachineName`. Affects SnmpMetricFactory, PipelineMetricService, SnmpConsoleFormatter, and deployment YAML.

### Test 3 — PASS
Log format uses `hostname` from `Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName`. No `site_name` or `SiteOptions` reference. Same NODE_NAME gap as test 2.2 applies.

### Test 4 — ISSUE (severity: high)
Community string convention is shared (`Simetra.*`), but polls derive it from device name instead of using a configured value. Three gaps:
1. **DeviceInfo** missing `Port` and `CommunityString` properties — currently only has Name, IpAddress, PollGroups
2. **MetricPollJob** hardcodes port 161 and derives community via `CommunityStringHelper.DeriveFromDeviceName` — should use `device.Port` and `device.CommunityString` directly
3. **Config model** needs `Port` and `CommunityString` fields per device, with `Simetra.*` validation at load time
**Fix:** Add Port + CommunityString to DeviceInfo and DevicesOptions config. MetricPollJob reads from device. Validate community follows Simetra.* convention at startup.
4. **sysUpTime prepend** — remove hardcoded `SysUpTimeOid` injection from MetricPollJob. Poll job should only send configured OIDs. Users who want sysUpTime add it to the device's OID list in appsettings.

### Test 5 — PASS
OidResolutionBehavior sets MetricName from OidMapService. OtelMetricHandler falls back to `OidMapService.Unknown` ("Unknown") when MetricName is null.

### Test 6 — PASS
Flow described and confirmed:
- **Poll**: Quartz trigger → MetricPollJob → SNMP GET → varbinds → ISender.Send → MediatR pipeline → OtelMetricHandler → SnmpMetricFactory (source=poll)
- **Trap**: UDP listener → ParseMessages → CommunityStringHelper validation → VarbindEnvelope → ITrapChannel → ChannelConsumerService → ISender.Send → same MediatR pipeline → OtelMetricHandler → SnmpMetricFactory (source=trap)
- Common path from MediatR onward: LoggingBehavior → ExceptionBehavior → ValidationBehavior → OidResolutionBehavior → OtelMetricHandler
