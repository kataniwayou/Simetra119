# Quick Task 074 Summary: Fix command registry lookup to use config address

## What was done

Fixed a bug where `ValidateAndBuildTenants` overwrote `cmd.Ip` with the resolved IP (`device.ResolvedIp`), causing `CommandWorkerService.TryGetByIpPort` to fail because `DeviceRegistry` is keyed by `ConfigAddress` (the original hostname), not `ResolvedIp`.

**Root cause:** Phase 56 added command IP resolution that mirrored the metric IP resolution pattern. But unlike metrics (consumed by SnapshotJob's threshold logic), commands are consumed by `CommandWorkerService` which needs the config address for its device registry lookup — exactly like `MetricPollJob` uses `configAddress`.

**Fix:** Removed `cmd.Ip = resolvedCmdIp` so the command retains the original config address. The resolved IP is still used for duplicate detection to correctly identify hostname+IP variants of the same device.

## Files changed

| File | Change |
|------|--------|
| `src/SnmpCollector/Services/TenantVectorWatcherService.cs` | Removed cmd.Ip overwrite, use resolvedCmdIp only for dedup key |
| `tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs` | Updated test to assert config address preserved (was: assert resolved IP) |

## Test results

- 453/453 unit tests pass
- Commit: 9bc5ab1
