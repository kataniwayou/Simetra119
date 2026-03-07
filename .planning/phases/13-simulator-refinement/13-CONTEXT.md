# Phase 13: Simulator Refinement - Context

**Gathered:** 2026-03-07
**Status:** Ready for planning

## Decisions

### NPB OID Tree Alignment
- **Clean rewrite** of NPB simulator to serve OIDs matching the OID map (`47477.100.1.*` system, `47477.100.2.*` per-port)
- Current simulator OID tree (`47477.100.4.*`) is discarded entirely
- Exactly 68 OIDs served — 1:1 match with oidmap-npb.json, no extras

### OBP Simulator Approach
- **Clean rewrite** of OBP simulator too, for consistency with NPB
- Add 16 missing R1-R4 power OIDs (4 links x 4 receivers) to reach 24 total OIDs matching oidmap-obp.json
- OBP keeps existing OID prefix (`47477.10.21`)

### OBP Power OID Values
- SNMP type: **Integer32** — power in tenths of dBm (e.g., -85 = -8.5 dBm)
- Behavior: **slow random walk** (+-1-2 tenths per poll interval)
- Bounds: -50 to -200 (-5.0 to -20.0 dBm)
- Each link has **different baseline** power levels (distinguishable on dashboard)
- R1-R4 receivers on the same link are **independent** (own baseline, own walk)

### Community String Convention
- Both simulators use `Simetra.{DeviceName}` only — no backward compatibility with `public`
- OBP: `Simetra.OBP-01` (default), NPB: `Simetra.NPB-01` (default)
- Configurable via `COMMUNITY` environment variable
- Reject requests with wrong community string

### Trap Behavior
- **Separate trap OIDs** from poll OIDs (standard SNMP practice)
- OBP keeps existing StateChange trap OID: `47477.10.21.{link}.3.50.2`
- NPB uses new portLinkChange trap OID: `47477.100.3.{port}.0`
- Trap varbinds include the polled OID and its new value

### NPB Counter Behavior
- **Realistic traffic profiles** per port:
  - P1-P2: heavy (large counter increments)
  - P3: light (small increments)
  - P4: zero (port down, no SFP)
  - P5-P6: medium (moderate increments)
  - P7: heavy (large increments)
  - P8: zero (admin disabled)
- Errors/drops: rare events (~1 per 100 poll cycles)

### NPB System OID Behavior
- `npb_cpu_util`: Integer32, random walk 5-40%
- `npb_mem_util`: Integer32, random walk 30-70%
- `npb_sys_temp`: Integer32, random walk 35-55 C
- `npb_uptime`: OctetString, cumulative seconds incrementing each cycle

### NPB Trap Timing
- Ports P1-P3, P5-P7: toggle link up/down every 60-300s (random per port)
- P4 stays down (no SFP), P8 stays down (admin disabled)
- Trap notification OID: `47477.100.3.{port}.0`
- Varbind: port_status poll OID = new value (1=up, 2=down)

## Claude's Discretion

- Exact power baseline values per OBP link and per receiver
- Counter increment magnitudes for each NPB traffic profile
- Random walk step size and update interval for system OIDs
- Code structure and async task organization for both rewrites
- Whether to keep supervised_task pattern or simplify

## Deferred Ideas

None — discussion stayed within phase scope.
