# Phase 1: Infrastructure Foundation - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

A running .NET 9 Generic Host with OTel SDK registered, structured logging active, OTLP push pipeline configured, and startup configuration validated. This is the foundation every subsequent phase builds into. No business logic, no SNMP, no MediatR — just the host, logging, telemetry export, and config validation.

</domain>

<decisions>
## Implementation Decisions

### Simetra Pattern Fidelity
- Copy Simetra's logging and OTel code as starting point, then modify for this project's architecture
- Console formatter follows Simetra's structure `[timestamp] [level] [site|role|correlationId] category message` — minor tweaks allowed for readability (e.g., field widths, abbreviations)
- OTLP log enrichment processor adds site_name, role, and correlationId to all exported logs — same as Simetra
- DI registration uses named extension methods: `AddSnmpLogging()`, `AddSnmpTelemetry()`, etc. — modular pattern matching Simetra's `AddSimetraLogging()`, `AddSimetraTelemetry()`

### Correlation ID Design
- Format: GUID without hyphens (`Guid.NewGuid().ToString("N")`) — matches Simetra
- Global rotation interval: 30 seconds via CorrelationJob with configurable `IntervalSeconds` — matches Simetra
- Per-operation scope: one AsyncLocal operation ID per Quartz job execution or per trap batch received — groups related OIDs under a single operation ID
- Console display: both global and operation correlation IDs shown — `[site|role|globalId|operationId]` for full traceability
- Lock-free concurrent service pattern: volatile string with single-writer (CorrelationJob), multiple-reader

### Local Dev Stack
- Docker Compose provides full local pipeline: OTel Collector + Prometheus + Grafana
- Grafana auto-provisioned with Prometheus datasource — instant dashboarding on `docker compose up`
- All deploy configs tracked in repo under `deploy/` folder:
  - `deploy/docker-compose.yml`
  - `deploy/otel-collector-config.yaml`
  - `deploy/prometheus.yml`
  - `deploy/grafana/` (provisioning configs)

### appsettings Structure
- Full skeleton created in Phase 1 with all sections present (Site, Devices, OidMap, Otlp, Logging, CorrelationJob, SnmpListener) — placeholder values for sections not yet active
- Progressive config validation: Phase 1 validates Site + Otlp + Logging + CorrelationJob only — each subsequent phase adds ValidateOnStart for its own sections
- Standard environment overrides: appsettings.json (base) + appsettings.Development.json (local defaults) + appsettings.Production.json
- Development.json has safe defaults for local dev (localhost endpoints, default community string) — no user secrets needed for a monitoring tool

### Claude's Discretion
- Exact timestamp format in console formatter (ISO 8601 vs custom)
- Log level abbreviation style (Info vs INF vs Information)
- OTel SDK package versions (pinned at 1.15.0 per research)
- Docker Compose version and base images for Collector/Prometheus/Grafana
- Prometheus scrape config details for OTel Collector remote_write target

</decisions>

<specifics>
## Specific Ideas

- Simetra's `RotatingCorrelationService` is the reference implementation for the lock-free correlation service
- Simetra's `CorrelationJobOptions` with `[Range(1, int.MaxValue)]` validation is the pattern for job config options
- Console formatter should show operation ID alongside global ID: `[site-nyc-01|leader|a3f7b2c1|op-9d8e7f6a]`
- The deploy/ folder pattern keeps infrastructure config separate from application source

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-infrastructure-foundation*
*Context gathered: 2026-03-05*
