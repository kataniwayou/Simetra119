# Quick Task 074: Fix command registry lookup to use config address

## Objective

Fix CommandWorkerService device registry lookup failure caused by tenant validation overwriting `cmd.Ip` with the resolved IP. The registry is keyed by ConfigAddress (hostname), not ResolvedIp.

## Tasks

### Task 1: Stop overwriting cmd.Ip in tenant validation

**File:** `src/SnmpCollector/Services/TenantVectorWatcherService.cs`

- Remove `cmd.Ip = resolvedCmdIp` (line 424)
- Use `resolvedCmdIp` only for duplicate detection key
- Add comment explaining why cmd.Ip must not be overwritten

**File:** `tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs`

- Update `CommandIpResolved_MatchesDeviceResolvedIp` → `CommandIpResolved_PreservesConfigAddress`
- Assert that cmd.Ip retains the original config address, not the resolved IP

## Verification

- All 453 unit tests pass
- E2E: `snmp_command_sent_total` should increment after deployment
