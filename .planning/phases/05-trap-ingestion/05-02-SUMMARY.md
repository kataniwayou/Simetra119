---
phase: 05-trap-ingestion
plan: 02
subsystem: pipeline
tags: [snmp, traps, udp, sharpsnmplib, channels, backpressure, dotnet, backgroundservice]

# Dependency graph
requires:
  - phase: 05-01
    provides: VarbindEnvelope, IDeviceChannelManager, IDeviceChannelManager.CompleteAll, PipelineMetricService trap counters
  - phase: 02-device-registry-and-oid-map
    provides: IDeviceRegistry.TryGetDevice(IPAddress) for device lookup and community string auth
  - phase: 01-infrastructure-foundation
    provides: SnmpListenerOptions (BindAddress, Port, CommunityString)
provides:
  - SnmpTrapListenerService: BackgroundService that receives SNMPv2c traps on UDP, authenticates, and writes varbinds to per-device channels
  - UDP receive loop bound to SnmpListenerOptions.BindAddress:Port
  - Device lookup before community auth (correct ordering: DeviceInfo holds expected community string)
  - Unknown-device drop with Warning log and IncrementTrapUnknownDevice
  - Auth-failure drop with Warning log and IncrementTrapAuthFailed
  - First-contact Information log per device via ConcurrentDictionary.TryAdd
  - Malformed packet Warning log with source IP, continues listening
  - StopAsync calling CompleteAll for graceful consumer drain
affects: [05-03-plan, 05-04-plan]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - BackgroundService UDP receive loop using UdpClient.ReceiveAsync(CancellationToken)
    - MessageFactory.ParseMessages with reused UserRegistry for SNMPv2c (no USM)
    - ConcurrentDictionary<string, byte>.TryAdd as atomic first-contact gate (byte value = no allocation)
    - MapToIPv4() on RemoteEndPoint.Address to normalize IPv6-mapped addresses
    - ProcessDatagram as synchronous helper — TryWrite is non-blocking, no async overhead needed

key-files:
  created:
    - src/SnmpCollector/Services/SnmpTrapListenerService.cs
  modified: []

key-decisions:
  - "Device lookup (TryGetDevice) happens before community auth — DeviceInfo holds the expected community string, so auth is impossible before lookup"
  - "MapToIPv4() called on senderIp in ProcessDatagram — RemoteEndPoint.Address may be IPv6-mapped on dual-stack systems; IDeviceRegistry is keyed on IPv4"
  - "UserRegistry created once in constructor and reused across all datagrams — SharpSnmpLib requires it even for v2c; avoids allocation per datagram"
  - "ProcessDatagram is synchronous — TryWrite is a non-blocking channel write; async overhead would add latency with no benefit"
  - "StopAsync: base.StopAsync first (cancels ExecuteAsync loop), then CompleteAll (signals consumers) — ordering ensures the producer is fully stopped before consumers are told to drain"
  - "MediatR references are zero in this file — architectural constraint enforced by code structure (no using directives for MediatR namespaces)"

patterns-established:
  - "UDP trap listener pattern: UdpClient.ReceiveAsync(CancellationToken) in while(!stoppingToken.IsCancellationRequested) loop with OperationCanceledException break"
  - "SNMPv2c parse pattern: MessageFactory.ParseMessages + foreach + 'is not TrapV2Message' continue guard"
  - "Auth ordering pattern: device lookup first (registry), community auth second (DeviceInfo.CommunityString) — always in this order for trap listeners"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 5 Plan 02: SnmpTrapListenerService Summary

**SNMPv2c trap listener BackgroundService using SharpSnmpLib MessageFactory — UDP receive loop with per-device community auth, first-contact logging, and VarbindEnvelope channel writes; zero MediatR references enforced**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-05T05:12:45Z
- **Completed:** 2026-03-05T05:14:11Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- SnmpTrapListenerService BackgroundService created and compiles cleanly with zero warnings
- All architectural constraints enforced: no MediatR/ISender/IPublisher/IMediator references anywhere in the file
- Device lookup correctly ordered before community auth (DeviceInfo holds expected community string)
- StopAsync overridden to call CompleteAll after base.StopAsync for proper graceful drain ordering
- All 64 existing tests continue passing (no regressions)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create SnmpTrapListenerService BackgroundService** - `4042de7` (feat)

**Plan metadata:** (docs commit to follow)

## Files Created/Modified
- `src/SnmpCollector/Services/SnmpTrapListenerService.cs` - BackgroundService: UDP receive loop, SharpSnmpLib parse, device lookup + community auth, VarbindEnvelope channel writes, first-contact logging, malformed packet handling, graceful drain via StopAsync/CompleteAll

## Decisions Made
- Device lookup (`TryGetDevice`) is ordered before community string auth because `DeviceInfo.CommunityString` is the authoritative expected community string — you cannot check auth without first finding the device
- `MapToIPv4()` is called on `result.RemoteEndPoint.Address` before passing to `TryGetDevice` — dual-stack hosts may receive IPv6-mapped IPv4 addresses (e.g., `::ffff:192.168.1.1`); the device registry is keyed on pure IPv4
- `UserRegistry` instantiated once in the constructor and reused — SharpSnmpLib's `MessageFactory.ParseMessages` requires it even for v2c (no USM users needed); creating it per datagram would waste allocations
- `ProcessDatagram` is a synchronous method — `ChannelWriter<T>.TryWrite` is non-blocking; making it async would introduce unnecessary overhead on the hot trap receive path
- `StopAsync` calls `base.StopAsync` first, then `CompleteAll` — ensures the ExecuteAsync receive loop is cancelled and exits before signaling consumers to drain, preventing a race where consumers drain while the producer is still active

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- SnmpTrapListenerService is complete and ready; Plan 05-03 (ChannelConsumerService) can now consume from IDeviceChannelManager.GetReader per device and call ISender.Send per VarbindEnvelope
- Plan 05-04 (DI registration) must register SnmpTrapListenerService as a hosted service and DeviceChannelManager as a singleton before the full pipeline is operational
- No blockers

---
*Phase: 05-trap-ingestion*
*Completed: 2026-03-05*
