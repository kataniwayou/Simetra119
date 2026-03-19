# OTS3000-BPS (Optical Bypass Switch) — SNMP Analysis (OBP4)

> **MIB File:** `BYPASS-CGS.mib`
> **MIB Module:** `OTS3000-BPS-8L`
> **Enterprise OID:** `1.3.6.1.4.1.47477` (cgs / GLSUN Corporation)
> **Device Base OID:** `1.3.6.1.4.1.47477.10.21` (EBP-1U2U4U → bypass)
> **Device Model:** OBP4 — 4 receivers (R1–R4), 1310nm / 1550nm, single-fiber

---

## 1. What Is the OBP Device?

The OTS3000-BPS is an **Optical Bypass Switch** that sits inline on fiber links between the network and security/monitoring tools (IDS, IPS, firewalls, etc.). Its purpose is to guarantee network continuity: if an inline tool fails, the OBP automatically reroutes traffic through a **bypass path**, skipping the failed tool so the network stays up.

```
Normal:   Network ──► Inline Tool ──► Network   (primary channel)
Failure:  Network ─────────────────► Network   (bypass channel, tool skipped)
```

### Architecture

The device manages **32 independent optical bypass links** (link1–link32). Each link has its own OID branch with an identical set of metrics, traps, and commands. Above the links sits the **NMU (Network Management Unit)** layer for device-level settings.

---

## 2. OID Hierarchy

```
1.3.6.1.4.1.47477                          ← enterprises → cgs
  └── 10                                    ← EBP-1U2U4U
       └── 21                               ← bypass
            ├── 1                            ← link1
            │    └── 3                       ← link1OBP (polled metrics & commands)
            │         ├── .1–.68             ← individual metric/command OIDs
            │         └── 50                 ← link1OBPTrap
            │              └── .1–.25        ← individual trap OIDs
            ├── 2                            ← link2
            │    └── 3                       ← link2OBP
            │         └── 50                 ← link2OBPTrap
            ├── ...                          ← link3 through link31
            ├── 32                           ← link32
            │    └── 3                       ← link32OBP
            │         └── 50                 ← link32OBPTrap
            └── 60                           ← nmu (device-level)
                 ├── .1–.16                  ← NMU metrics & commands
                 └── 50                      ← nmuTrap
                      └── .1–.2              ← NMU trap OIDs
```

**OID Pattern for any link N:**
- Polled metrics / commands: `1.3.6.1.4.1.47477.10.21.{N}.3.{suffix}`
- Traps: `1.3.6.1.4.1.47477.10.21.{N}.3.50.{suffix}`

---

## 3. SNMP Traps (Asynchronous Notifications)

Traps are **push-based** — the device sends them to a configured SNMP manager when an event occurs. No polling required.

**Total: 2 NMU-level + 15 per-link x 32 links = 482 trap instances**

### 3.1 NMU-Level Traps

**OID Path:** `1.3.6.1.4.1.47477.10.21.60.50.{suffix}`

| # | Trap Name | Full OID | Type | Description |
|---|-----------|----------|------|-------------|
| 1 | `systemStartup` | `...60.50.1` | DisplayString | System startup notification |
| 2 | `cardStatusChanged` | `...60.50.2` | DisplayString | Card/module status alarm |

### 3.2 Per-Link Traps (x32 links)

**OID Path:** `1.3.6.1.4.1.47477.10.21.{N}.3.50.{suffix}`

> Every trap below is replicated for link1 through link32. The table shows the generic pattern and a concrete **link1** example OID.

#### Mode & State Change Traps

| # | Trap Name | Suffix | Link1 OID | Type | Description |
|---|-----------|--------|-----------|------|-------------|
| 1 | `linkN_WorkModeChange` | `.1` | `...1.3.50.1` | INTEGER: manualMode(0), autoMode(1) | Link mode changed |
| 2 | `linkN_StateChange` | `.2` | `...1.3.50.2` | INTEGER: bypass(0), primary(1) | Link channel changed |

#### Wavelength Change Traps

| # | Trap Name | Suffix | Link1 OID | Type | Description |
|---|-----------|--------|-----------|------|-------------|
| 3 | `linkN_R1WaveSet` | `.3` | `...1.3.50.3` | INTEGER: w1310nm(0), w1550nm(1) | R1 wavelength changed |
| 4 | `linkN_R2WaveSet` | `.4` | `...1.3.50.4` | INTEGER: w1310nm(0), w1550nm(1) | R2 wavelength changed |
| 5 | `linkN_R3WaveSet` | `.5` | `...1.3.50.5` | INTEGER: w1310nm(0), w1550nm(1) | R3 wavelength changed |
| 6 | `linkN_R4WaveSet` | `.6` | `...1.3.50.6` | INTEGER: w1310nm(0), w1550nm(1) | R4 wavelength changed |

#### Alarm Threshold Change Traps

| # | Trap Name | Suffix | Link1 OID | Type | Description |
|---|-----------|--------|-----------|------|-------------|
| 7 | `linkN_R1AlarmSet` | `.7` | `...1.3.50.7` | DisplayString | R1 alarm threshold changed |
| 8 | `linkN_R2AlarmSet` | `.8` | `...1.3.50.8` | DisplayString | R2 alarm threshold changed |
| 9 | `linkN_R3AlarmSet` | `.9` | `...1.3.50.9` | DisplayString | R3 alarm threshold changed |
| 10 | `linkN_R4AlarmSet` | `.10` | `...1.3.50.10` | DisplayString | R4 alarm threshold changed |

#### Bypass Configuration Change Trap

| # | Trap Name | Suffix | Link1 OID | Type | Description |
|---|-----------|--------|-----------|------|-------------|
| 11 | `linkN_PowerAlarmBypass4Changed` | `.20` | `...1.3.50.20` | INTEGER: off(0), R1(1), R2(2), R3(3), R4(4), anyAlarmR1-R4(5), allAlarmR1-R4(6) | Bypass trigger config changed |

#### Power Status Alarm Traps

| # | Trap Name | Suffix | Link1 OID | Type | Description |
|---|-----------|--------|-----------|------|-------------|
| 12 | `linkN_powerAlarmR1` | `.22` | `...1.3.50.22` | DisplayString | R1 power crossed alarm threshold |
| 13 | `linkN_powerAlarmR2` | `.23` | `...1.3.50.23` | DisplayString | R2 power crossed alarm threshold |
| 14 | `linkN_powerAlarmR3` | `.24` | `...1.3.50.24` | DisplayString | R3 power crossed alarm threshold |
| 15 | `linkN_powerAlarmR4` | `.25` | `...1.3.50.25` | DisplayString | R4 power crossed alarm threshold |

---

## 4. Polled Metrics (SNMP GET — Pull-Based)

These are values you **actively query** from the device. The same OIDs that are `read-write` also appear in the Commands section (Section 5).

**Total: 16 NMU-level + 32 per-link x 32 links = 1,040 metric instances**

### 4.1 NMU (Device-Level) Metrics

**OID Path:** `1.3.6.1.4.1.47477.10.21.60.{suffix}`

| # | Metric Name | Suffix | Full OID | Type | Access | Description |
|---|-------------|--------|----------|------|--------|-------------|
| 1 | `deviceType` | `.1` | `...60.1` | DisplayString | read-only | Device type identifier |
| 2 | `ipAddress` | `.2` | `...60.2` | IpAddress | read-write | Device IP address |
| 3 | `subnetMask` | `.3` | `...60.3` | IpAddress | read-write | Subnet mask |
| 4 | `gateWay` | `.4` | `...60.4` | IpAddress | read-write | Default gateway |
| 5 | `macAddress` | `.5` | `...60.5` | DisplayString | read-only | MAC address |
| 6 | `tcpPort` | `.6` | `...60.6` | Integer32 | read-write | TCP port |
| 7 | `startDelay` | `.7` | `...60.7` | Integer32 | read-write | Start delay (seconds) |
| 8 | `keyLock` | `.8` | `...60.8` | INTEGER: lock(0), unlock(1) | read-write | Keyboard lock status |
| 9 | `buzzerSet` | `.9` | `...60.9` | INTEGER: off(0), on(1) | read-write | Buzzer on/off |
| 10 | `deviceAddress` | `.10` | `...60.10` | Integer32 | read-write | Device address |
| 11 | `power1State` | `.11` | `...60.11` | INTEGER: off(0), on(1) | read-only | Power supply 1 status |
| 12 | `power2State` | `.12` | `...60.12` | INTEGER: off(0), on(1) | read-only | Power supply 2 status |
| 13 | `softwareVersion` | `.13` | `...60.13` | DisplayString | read-only | Software version |
| 14 | `hardwareVersion` | `.14` | `...60.14` | DisplayString | read-only | Hardware version |
| 15 | `serialNumber` | `.15` | `...60.15` | DisplayString | read-only | Serial number |
| 16 | `manufacturingdate` | `.16` | `...60.16` | DisplayString | read-only | Manufacturing date |

### 4.2 Per-Link Metrics (x32 links)

**OID Path:** `1.3.6.1.4.1.47477.10.21.{N}.3.{suffix}`

> Every metric below is replicated for link1 through link32. The table shows the OID suffix within `linkNOBP` and a concrete **link1** example.

#### State & Configuration

| # | Metric Name | Suffix | Link1 OID | Type | Access | Description |
|---|-------------|--------|-----------|------|--------|-------------|
| 1 | `linkN_State` | `.1` | `...1.3.1` | INTEGER: off(0), on(1) | read-only | Link working status |
| 2 | `linkN_DeviceType` | `.2` | `...1.3.2` | DisplayString | read-only | Card type in this link slot |
| 3 | `linkN_WorkMode` | `.3` | `...1.3.3` | INTEGER: manualMode(0), autoMode(1) | read-write | Work mode (manual/auto) |
| 4 | `linkN_Channel` | `.4` | `...1.3.4` | INTEGER: bypass(0), primary(1) | read-write | Active channel |

#### Optical Power (R1–R4)

| # | Metric Name | Suffix | Link1 OID | Type | Access | Description |
|---|-------------|--------|-----------|------|--------|-------------|
| 5 | `linkN_R1Power` | `.5` | `...1.3.5` | DisplayString (dBm) | read-only | R1 optical power |
| 6 | `linkN_R2Power` | `.6` | `...1.3.6` | DisplayString (dBm) | read-only | R2 optical power |
| 7 | `linkN_R3Power` | `.35` | `...1.3.35` | DisplayString (dBm) | read-only | R3 optical power |
| 8 | `linkN_R4Power` | `.36` | `...1.3.36` | DisplayString (dBm) | read-only | R4 optical power |

#### Wavelength Configuration (R1–R4)

| # | Metric Name | Suffix | Link1 OID | Type | Access | Description |
|---|-------------|--------|-----------|------|--------|-------------|
| 9 | `linkN_R1Wave` | `.7` | `...1.3.7` | INTEGER: w1310nm(0), w1550nm(1), na(2) | read-write | R1 wavelength |
| 10 | `linkN_R2Wave` | `.8` | `...1.3.8` | INTEGER: w1310nm(0), w1550nm(1), na(2) | read-write | R2 wavelength |
| 11 | `linkN_R3Wave` | `.27` | `...1.3.27` | INTEGER: w1310nm(0), w1550nm(1), na(2) | read-write | R3 wavelength |
| 12 | `linkN_R4Wave` | `.28` | `...1.3.28` | INTEGER: w1310nm(0), w1550nm(1), na(2) | read-write | R4 wavelength |

#### Alarm Thresholds (R1–R4)

| # | Metric Name | Suffix | Link1 OID | Type | Access | Description |
|---|-------------|--------|-----------|------|--------|-------------|
| 13 | `linkN_R1AlarmPower` | `.9` | `...1.3.9` | DisplayString (dBm) | read-write | R1 power alarm threshold |
| 14 | `linkN_R2AlarmPower` | `.10` | `...1.3.10` | DisplayString (dBm) | read-write | R2 power alarm threshold |
| 15 | `linkN_R3AlarmPower` | `.29` | `...1.3.29` | DisplayString (dBm) | read-write | R3 power alarm threshold |
| 16 | `linkN_R4AlarmPower` | `.30` | `...1.3.30` | DisplayString (dBm) | read-write | R4 power alarm threshold |

#### Bypass Mode & Failover Configuration

| # | Metric Name | Suffix | Link1 OID | Type | Access | Description |
|---|-------------|--------|-----------|------|--------|-------------|
| 17 | `linkN_PowerAlarmBypass4` | `.67` | `...1.3.67` | INTEGER: off(0), R1(1), R2(2), R3(3), R4(4), anyAlarmR1-R4(5), allAlarmR1-R4(6), na(7) | read-write | Bypass trigger mode |
| 18 | `linkN_ReturnDelay` | `.12` | `...1.3.12` | Integer32 (seconds) | read-write | Auto-return delay |
| 19 | `linkN_BackMode` | `.13` | `...1.3.13` | INTEGER: autoNoBack(0), autoBack(1) | read-write | Back mode |
| 20 | `linkN_BackDelay` | `.14` | `...1.3.14` | Integer32 (seconds) | read-write | Back delay |
| 21 | `linkN_SwitchProtect` | `.23` | `...1.3.23` | INTEGER: off(0), on(1) | read-write | Switch protection (anti-flap) |

#### Active Heartbeat Configuration

| # | Metric Name | Suffix | Link1 OID | Type | Access | Description |
|---|-------------|--------|-----------|------|--------|-------------|
| 22 | `linkN_ActiveHeartSwitch` | `.15` | `...1.3.15` | INTEGER: off(0), on(1) | read-write | Active heartbeat on/off |
| 23 | `linkN_ActiveSendInterval` | `.16` | `...1.3.16` | Integer32 (ms) | read-write | Active HB send interval |
| 24 | `linkN_ActiveTimeOut` | `.17` | `...1.3.17` | Integer32 (ms) | read-write | Active HB timeout |
| 25 | `linkN_ActiveLossBypass` | `.18` | `...1.3.18` | Integer32 | read-write | Consecutive losses before bypass |
| 26 | `linkN_PingIpAddress` | `.19` | `...1.3.19` | IpAddress | read-write | Ping target IP for heartbeat |

#### Passive Heartbeat Configuration

| # | Metric Name | Suffix | Link1 OID | Type | Access | Description |
|---|-------------|--------|-----------|------|--------|-------------|
| 27 | `linkN_PassiveHeartSwitch` | `.20` | `...1.3.20` | INTEGER: off(0), on(1) | read-write | Passive heartbeat on/off |
| 28 | `linkN_PassiveTimeOut` | `.21` | `...1.3.21` | Integer32 (ms) | read-write | Passive HB timeout |
| 29 | `linkN_PassiveLossBypass` | `.22` | `...1.3.22` | Integer32 | read-write | Passive HB loss bypass threshold |

#### Status Indicators (read-only)

| # | Metric Name | Suffix | Link1 OID | Type | Access | Description |
|---|-------------|--------|-----------|------|--------|-------------|
| 30 | `linkN_ActiveHeartStatus` | `.24` | `...1.3.24` | INTEGER: alarm(0), normal(1), off(2), na(3) | read-only | Active heartbeat health |
| 31 | `linkN_PassiveHeartStatus` | `.25` | `...1.3.25` | INTEGER: alarm(0), normal(1), off(2), na(3) | read-only | Passive heartbeat health |
| 32 | `linkN_PowerAlarmStatus` | `.26` | `...1.3.26` | INTEGER: off(0), alarm(1), normal(2), na(3) | read-only | Power alarm status |

---

## 5. SNMP Commands (SET Operations)

Commands are SNMP SET operations — you write values to change device behavior. Every `read-write` metric from Section 4 is also a command.

**Total: 8 NMU-level + 23 per-link x 32 links = 744 command instances**

### 5.1 NMU-Level Commands

**OID Path:** `1.3.6.1.4.1.47477.10.21.60.{suffix}`

| # | Command Name | Suffix | Type | Description |
|---|-------------|--------|------|-------------|
| 1 | `ipAddress` | `.2` | IpAddress | Set device IP address |
| 2 | `subnetMask` | `.3` | IpAddress | Set subnet mask |
| 3 | `gateWay` | `.4` | IpAddress | Set default gateway |
| 4 | `tcpPort` | `.6` | Integer32 | Set TCP port |
| 5 | `startDelay` | `.7` | Integer32 | Set start delay (seconds) |
| 6 | `keyLock` | `.8` | INTEGER: lock(0), unlock(1) | Lock/unlock front panel |
| 7 | `buzzerSet` | `.9` | INTEGER: off(0), on(1) | Enable/disable buzzer |
| 8 | `deviceAddress` | `.10` | Integer32 | Set device address |

### 5.2 Per-Link Commands (x32 links)

**OID Path:** `1.3.6.1.4.1.47477.10.21.{N}.3.{suffix}`

> Each command below is replicated for link1–link32.

#### Work Mode & Channel

| # | Command Name | Suffix | Type | Description |
|---|-------------|--------|------|-------------|
| 1 | `linkN_WorkMode` | `.3` | INTEGER: manualMode(0), autoMode(1) | Set manual or auto mode |
| 2 | `linkN_Channel` | `.4` | INTEGER: bypass(0), primary(1) | Force bypass or primary channel |

#### Wavelength Configuration (R1–R4)

| # | Command Name | Suffix | Type | Description |
|---|-------------|--------|------|-------------|
| 3 | `linkN_R1Wave` | `.7` | INTEGER: w1310nm(0), w1550nm(1), na(2) | Set R1 wavelength |
| 4 | `linkN_R2Wave` | `.8` | INTEGER: w1310nm(0), w1550nm(1), na(2) | Set R2 wavelength |
| 5 | `linkN_R3Wave` | `.27` | INTEGER: w1310nm(0), w1550nm(1), na(2) | Set R3 wavelength |
| 6 | `linkN_R4Wave` | `.28` | INTEGER: w1310nm(0), w1550nm(1), na(2) | Set R4 wavelength |

#### Alarm Thresholds (R1–R4)

| # | Command Name | Suffix | Type | Description |
|---|-------------|--------|------|-------------|
| 7 | `linkN_R1AlarmPower` | `.9` | DisplayString (dBm) | Set R1 alarm threshold |
| 8 | `linkN_R2AlarmPower` | `.10` | DisplayString (dBm) | Set R2 alarm threshold |
| 9 | `linkN_R3AlarmPower` | `.29` | DisplayString (dBm) | Set R3 alarm threshold |
| 10 | `linkN_R4AlarmPower` | `.30` | DisplayString (dBm) | Set R4 alarm threshold |

#### Bypass Mode

| # | Command Name | Suffix | Type | Description |
|---|-------------|--------|------|-------------|
| 11 | `linkN_PowerAlarmBypass4` | `.67` | INTEGER: off(0), R1(1), R2(2), R3(3), R4(4), anyAlarmR1-R4(5), allAlarmR1-R4(6), na(7) | Set bypass trigger mode |

#### Failover Configuration

| # | Command Name | Suffix | Type | Description |
|---|-------------|--------|------|-------------|
| 12 | `linkN_ReturnDelay` | `.12` | Integer32 (seconds) | Set auto-return delay |
| 13 | `linkN_BackMode` | `.13` | INTEGER: autoNoBack(0), autoBack(1) | Set back mode |
| 14 | `linkN_BackDelay` | `.14` | Integer32 (seconds) | Set back delay |
| 15 | `linkN_SwitchProtect` | `.23` | INTEGER: off(0), on(1) | Enable/disable switch protection |

#### Active Heartbeat Configuration

| # | Command Name | Suffix | Type | Description |
|---|-------------|--------|------|-------------|
| 16 | `linkN_ActiveHeartSwitch` | `.15` | INTEGER: off(0), on(1) | Enable/disable active heartbeat |
| 17 | `linkN_ActiveSendInterval` | `.16` | Integer32 (ms) | Set ping send interval |
| 18 | `linkN_ActiveTimeOut` | `.17` | Integer32 (ms) | Set ping response timeout |
| 19 | `linkN_ActiveLossBypass` | `.18` | Integer32 | Set consecutive losses before bypass |
| 20 | `linkN_PingIpAddress` | `.19` | IpAddress | Set ping target IP |

#### Passive Heartbeat Configuration

| # | Command Name | Suffix | Type | Description |
|---|-------------|--------|------|-------------|
| 21 | `linkN_PassiveHeartSwitch` | `.20` | INTEGER: off(0), on(1) | Enable/disable passive heartbeat |
| 22 | `linkN_PassiveTimeOut` | `.21` | Integer32 (ms) | Set passive HB timeout |
| 23 | `linkN_PassiveLossBypass` | `.22` | Integer32 | Set loss threshold before bypass |

---

## 6. Bypass Mechanism — Core Capabilities

This is the heart of the OBP device. The bypass mechanism provides **three layers of protection** to detect inline tool failure and reroute traffic.

### 6.1 Layer 1: Manual/Auto Channel Switching

The most basic control — directly choosing where traffic goes.

| OID Suffix | Link1 OID | What it controls |
|------------|-----------|------------------|
| `.3` | `...1.3.3` | **WorkMode** — `manualMode(0)` or `autoMode(1)` |
| `.4` | `...1.3.4` | **Channel** — `bypass(0)` or `primary(1)` |

- **Manual mode**: An operator explicitly switches between primary (through the tool) and bypass (skip the tool).
- **Auto mode**: The device monitors conditions (power levels, heartbeat) and switches automatically.

### 6.2 Layer 2: Power Alarm Bypass (Optical Power Monitoring)

The device continuously reads optical power levels on each receiver (R1–R4). When power drops below a configurable threshold (in dBm), a power alarm fires. The **bypass mode** setting determines which alarm conditions trigger an automatic switch to bypass.

**How it works:**

```
Receiver Power ──► Compare against threshold ──► Alarm? ──► Check bypass mode ──► Switch to bypass
     (dBm)          (R1–R4 AlarmPower)                │       (PowerAlarmBypass4)
                                                       └─ No ──► Stay on primary
```

**Bypass mode options (OID suffix `.67`):**

| Value | Enum | Meaning |
|-------|------|---------|
| 0 | `off` | Power alarms do NOT trigger bypass |
| 1 | `powerAlarmR1` | Bypass only if R1 power drops |
| 2 | `powerAlarmR2` | Bypass only if R2 power drops |
| 3 | `powerAlarmR3` | Bypass only if R3 power drops |
| 4 | `powerAlarmR4` | Bypass only if R4 power drops |
| 5 | `anyAlarmR1-R4` | Bypass if **any single** receiver loses power |
| 6 | `allAlarmR1-R4` | Bypass only if **all** receivers lose power simultaneously |
| 7 | `na` | Not applicable (no card in slot) |

### 6.3 Layer 3: Heartbeat Monitoring

Two independent heartbeat mechanisms verify the inline tool is alive and passing traffic.

#### Active Heartbeat (ICMP Ping)

The OBP sends ICMP pings to a configured IP address (typically the inline tool's management IP). If pings fail, bypass triggers.

| Parameter | Suffix | Link1 OID | Purpose |
|-----------|--------|-----------|---------|
| `ActiveHeartSwitch` | `.15` | `...1.3.15` | Enable/disable (off=0, on=1) |
| `ActiveSendInterval` | `.16` | `...1.3.16` | How often to send pings (ms) |
| `ActiveTimeOut` | `.17` | `...1.3.17` | Response timeout (ms) |
| `ActiveLossBypass` | `.18` | `...1.3.18` | Consecutive missed pings before triggering bypass |
| `PingIpAddress` | `.19` | `...1.3.19` | Target IP to ping |
| `ActiveHeartStatus` | `.24` | `...1.3.24` | Status: alarm(0), normal(1), off(2), na(3) |

#### Passive Heartbeat (Traffic Flow Monitor)

The OBP monitors whether traffic is actually flowing through the link. If traffic stops for too long, bypass triggers.

| Parameter | Suffix | Link1 OID | Purpose |
|-----------|--------|-----------|---------|
| `PassiveHeartSwitch` | `.20` | `...1.3.20` | Enable/disable (off=0, on=1) |
| `PassiveTimeOut` | `.21` | `...1.3.21` | Silence timeout before alarm (ms) |
| `PassiveLossBypass` | `.22` | `...1.3.22` | Loss threshold before triggering bypass |
| `PassiveHeartStatus` | `.25` | `...1.3.25` | Status: alarm(0), normal(1), off(2), na(3) |

### 6.4 Failover Recovery Settings

Once the device has switched to bypass, these settings control what happens when the inline tool comes back online.

| Parameter | Suffix | Link1 OID | Values | Purpose |
|-----------|--------|-----------|--------|---------|
| `ReturnDelay` | `.12` | `...1.3.12` | Integer32 (seconds) | Wait time before switching back from bypass to primary |
| `BackMode` | `.13` | `...1.3.13` | autoNoBack(0), autoBack(1) | `autoNoBack` = stay in bypass until manual intervention; `autoBack` = auto-return to primary when tool recovers |
| `BackDelay` | `.14` | `...1.3.14` | Integer32 (seconds) | Delay before executing the back-switch |
| `SwitchProtect` | `.23` | `...1.3.23` | off(0), on(1) | Anti-flap protection — prevents rapid toggling between bypass and primary |

### 6.5 End-to-End Bypass Flow

```
                    ┌─────────────────────────────────────────────┐
                    │              AUTO MODE ACTIVE                │
                    │                                             │
                    │  ┌─────────────┐    ┌────────────────────┐  │
                    │  │ Power Alarm │    │ Active Heartbeat   │  │
                    │  │ Monitoring  │    │ (ICMP Ping)        │  │
                    │  │             │    │                    │  │
                    │  │ R1 < -20dBm?│    │ Ping 10.0.0.1 ... │  │
                    │  │ R2 < -20dBm?│    │ 3 missed = alarm  │  │
                    │  │ R3 < -20dBm?│    │                    │  │
                    │  │ R4 < -20dBm?│    │                    │  │
                    │  └──────┬──────┘    └─────────┬──────────┘  │
                    │         │                     │             │
                    │         ▼                     ▼             │
                    │  ┌──────────────────────────────────┐       │
                    │  │     ANY failure condition met?    │       │
                    │  └───────────────┬──────────────────┘       │
                    │                  │ YES                       │
                    │                  ▼                           │
                    │  ┌──────────────────────────────────┐       │
                    │  │   SWITCH TO BYPASS CHANNEL (0)   │       │
                    │  │   Send StateChange trap          │       │
                    │  └───────────────┬──────────────────┘       │
                    │                  │                           │
                    │                  ▼                           │
                    │  ┌──────────────────────────────────┐       │
                    │  │   BackMode = autoBack?           │       │
                    │  │   YES: wait ReturnDelay seconds  │       │
                    │  │         then switch to primary   │       │
                    │  │   NO:  stay in bypass (manual)   │       │
                    │  └──────────────────────────────────┘       │
                    └─────────────────────────────────────────────┘
```

---

## 7. Quick OID Reference — Complete Suffix Map

### NMU Suffixes (`...60.{suffix}`)

| Suffix | Object | Access |
|--------|--------|--------|
| `.1` | deviceType | RO |
| `.2` | ipAddress | RW |
| `.3` | subnetMask | RW |
| `.4` | gateWay | RW |
| `.5` | macAddress | RO |
| `.6` | tcpPort | RW |
| `.7` | startDelay | RW |
| `.8` | keyLock | RW |
| `.9` | buzzerSet | RW |
| `.10` | deviceAddress | RW |
| `.11` | power1State | RO |
| `.12` | power2State | RO |
| `.13` | softwareVersion | RO |
| `.14` | hardwareVersion | RO |
| `.15` | serialNumber | RO |
| `.16` | manufacturingdate | RO |
| `.50.1` | systemStartup (trap) | — |
| `.50.2` | cardStatusChanged (trap) | — |

### Per-Link Suffixes (`...{N}.3.{suffix}`)

| Suffix | Object | Access |
|--------|--------|--------|
| `.1` | State | RO |
| `.2` | DeviceType | RO |
| `.3` | WorkMode | RW |
| `.4` | Channel | RW |
| `.5` | R1Power | RO |
| `.6` | R2Power | RO |
| `.7` | R1Wave | RW |
| `.8` | R2Wave | RW |
| `.9` | R1AlarmPower | RW |
| `.10` | R2AlarmPower | RW |
| `.12` | ReturnDelay | RW |
| `.13` | BackMode | RW |
| `.14` | BackDelay | RW |
| `.15` | ActiveHeartSwitch | RW |
| `.16` | ActiveSendInterval | RW |
| `.17` | ActiveTimeOut | RW |
| `.18` | ActiveLossBypass | RW |
| `.19` | PingIpAddress | RW |
| `.20` | PassiveHeartSwitch | RW |
| `.21` | PassiveTimeOut | RW |
| `.22` | PassiveLossBypass | RW |
| `.23` | SwitchProtect | RW |
| `.24` | ActiveHeartStatus | RO |
| `.25` | PassiveHeartStatus | RO |
| `.26` | PowerAlarmStatus | RO |
| `.27` | R3Wave | RW |
| `.28` | R4Wave | RW |
| `.29` | R3AlarmPower | RW |
| `.30` | R4AlarmPower | RW |
| `.35` | R3Power | RO |
| `.36` | R4Power | RO |
| `.67` | PowerAlarmBypass4 | RW |

### Per-Link Trap Suffixes (`...{N}.3.50.{suffix}`)

| Suffix | Trap |
|--------|------|
| `.1` | WorkModeChange |
| `.2` | StateChange |
| `.3` | R1WaveSet |
| `.4` | R2WaveSet |
| `.5` | R3WaveSet |
| `.6` | R4WaveSet |
| `.7` | R1AlarmSet |
| `.8` | R2AlarmSet |
| `.9` | R3AlarmSet |
| `.10` | R4AlarmSet |
| `.20` | PowerAlarmBypass4Changed |
| `.22` | powerAlarmR1 |
| `.23` | powerAlarmR2 |
| `.24` | powerAlarmR3 |
| `.25` | powerAlarmR4 |
