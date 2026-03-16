# Phase 49: Observability & Dashboard - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Add Stopwatch-based command execution logs to CommandWorkerService and 3 `snmp.command.*` panels to the operations dashboard. Pure observability additions — no logic changes.

</domain>

<decisions>
## Implementation Decisions

### Command execution logs
- Follow existing log patterns in MetricPollJob and CommandWorkerService
- Structured log fields consistent with existing conventions (device name, command/metric name, duration)
- Information level for successful SET (device, command name, round-trip ms)
- Warning level for failed SET (device, command name, error, round-trip ms)
- Duration via Stopwatch around SetAsync call, same pattern as MetricPollJob
- Metric labels: `device_name` tag, consistent with all other `snmp.*` counters

### Dashboard panels
- 3 panels on a single row (Row 5) in Pipeline Counters group: `snmp.command.sent`, `snmp.command.failed`, `snmp.command.suppressed`
- w=8 each (same as polls row and traps row)
- Same panel style as existing pipeline counter panels (time series, pod filter `$pod`, `rate(...[$__rate_interval])`)
- Panel ordering: sent | failed | suppressed (left to right) — consistent with polls row (executed | recovered | unreachable)
- Y-offset follows Row 4 (y=31 + h=8 = y=39)
- .NET Runtime row shifts down accordingly

### Claude's Discretion
- Exact Stopwatch placement (wrap just SetAsync or the entire command processing)
- Whether to add duration to the existing log entries or create new dedicated log entries
- Dashboard panel IDs (auto-increment from existing)
- Panel colors/thresholds

</decisions>

<specifics>
## Specific Ideas

- Keep full consistency with existing patterns — logs, dashboard, labels should look like they were always part of the system
- The 3 counters already exist in PipelineMetricService (Phase 46-03) — dashboard panels just visualize them

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 49-observability-and-dashboard*
*Context gathered: 2026-03-16*
