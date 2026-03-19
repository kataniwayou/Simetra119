# CGS NPB-2E (Network Packet Broker) — SNMP Analysis

> **MIB Files:** CGS.mib, NPB-NPB.mib, NPB-2E.mib, NPB-SYSTEM.mib, NPB-PORTS.mib, NPB-TRAPS.mib, NPB-HA.mib, NPB-LB.mib, NPB-HB.mib, NPB-INLINE.mib, NPB-FILTERS.mib
> **Enterprise OID:** `1.3.6.1.4.1.47477` (cgs / GLSUN Corporation)
> **Device Base OID:** `1.3.6.1.4.1.47477.100.4` (npb -> npb-2e)

---

## 1. What Is the NPB Device?

The CGS NPB-2E is a **Network Packet Broker** — a purpose-built appliance that sits between network TAPs/SPAN ports and monitoring/security tools. Its job is to **aggregate, filter, replicate, and distribute** network traffic to the right tools efficiently.

```
Network TAPs / SPAN --> NPB-2E --> IDS/IPS, SIEM, Forensics, APM, etc.
                         |
                    Aggregates, filters,
                    load-balances, deduplicates,
                    slices, timestamps
```

### Key Capabilities

| Feature | Description |
|---------|-------------|
| **Traffic Aggregation** | Combine multiple network links into tool ports |
| **Load Balancing** | Distribute traffic across multiple tool ports (hash, round-robin, dynamic) |
| **Inline Tool Support** | Insert tools inline with heartbeat monitoring and automatic bypass on failure |
| **High Availability** | Active-active or active-standby clustering with automatic failover |
| **Port Management** | QSFP breakout, FEC, VLAN tagging, MPLS stripping, packet slicing, timestamping |
| **Transceiver DDM** | Digital Diagnostic Monitoring for all optics (temp, voltage, power, bias) |

---

## 2. OID Hierarchy

```
1.3.6.1.4.1.47477                              <-- enterprises -> cgs
  +-- 100                                       <-- npb
       +-- 4                                    <-- npb-2e
            |-- 1  (systemMib)                  <-- NPB-SYSTEM.mib
            |    +-- 1  (system)
            |         |-- 1   systemHw
            |         |-- 2   systemConfigFiles
            |         |-- 3   systemLog
            |         |-- 4   systemAaa
            |         |-- 6   systemAlarms
            |         |-- 7   systemSecurity
            |         |-- 8   systemSyslog
            |         |-- 9   systemTimeAndDate
            |         |-- 10  systemConsolePort
            |         |-- 11  systemUserMgmt
            |         |-- 12  systemDetails
            |         |-- 13  systemHwStatus
            |         |-- 14  systemSwUpgrade
            |         |-- 15  systemStatus
            |         |-- 16  systemAudit
            |         |-- 17  systemDns
            |         +-- 18  systemInterface
            |-- 2  (portsMib)                   <-- NPB-PORTS.mib
            |    |-- 1  (ports)
            |    |    |-- 1  portsBreakout
            |    |    |-- 3  portsMpls
            |    |    |-- 4  portsPortTable
            |    |    +-- 5  portsGroupTable
            |    |-- 2  (portStatistics)
            |    |    |-- 1  packetSize
            |    |    |-- 2  trafficTypes
            |    |    |-- 3  errors
            |    |    |-- 4  utilization
            |    |    |-- 5  summary
            |    |    |-- 7  prbs
            |    |    |-- 8  fec
            |    |    +-- 9  actions
            |    +-- 3  (transceivers)
            |         +-- 1  transceiversDdmTable
            |-- 4  (lbMib)                      <-- NPB-LB.mib
            |    +-- 1  (lb)
            |         |-- 1  lbHash
            |         |-- 2  lbInfo
            |         +-- 3  lbGroupTable
            |-- 6  (haMib)                      <-- NPB-HA.mib
            |    +-- 1  (ha)
            |         |-- 1-7  config scalars
            |         |-- 8  haStatus
            |         +-- 9  haOper
            |-- 7  (inlineMib)                  <-- NPB-INLINE.mib
            |    +-- 1  (inline)
            |         |-- 1  inlineToolTable
            |         +-- 2  inlineToolchainTable
            |-- 3  (filtersMib)                  <-- NPB-FILTERS.mib
            |    |-- 1  (filters)
            |    |    |-- 1  filtersGroups
            |    |    |     |-- 1  filtersGroupsGroupTable
            |    |    |-- 2  filtersMode
            |    |    |-- 4  filtersUdfWindow
            |    |    |     |-- 4  filtersUdfWindowUdfTable
            |    |    |-- 5  filtersLbOperMode
            |    |    |-- 8  filtersFilterTable
            |    |    +-- 9  filtersIpListTable
            |    |-- 2  (filterMemory)
            |    |-- 3  filtersFilterUdfTable
            |    +-- 4  filtersGroupsGroupFilterTable
            |-- 8  (hbMib)                      <-- NPB-HB.mib
            |    +-- 1  (heartbeat)
            |         +-- 1  heartbeatProfileTable
            +-- 10 (notificationsMib)           <-- NPB-TRAPS.mib
                 |-- 1  (variables)
                 +-- 2  (notifications)
```

### Table Indexing Convention

Unlike the OBP device which uses 32 fixed per-link OID branches, the NPB uses standard **SNMP tables** with row indexes. To query a specific instance, you append the index to the column OID:

```
Column OID . index = instance OID

Example: port #1 link status
  1.3.6.1.4.1.47477.100.4.2.1.4.1.3.1  =  portsPortStatusLinkStatus for port 1
                                     ^--- port index
```

**Index types by table:**
- **Port tables:** `LogicalPortNum` (integer port ID) — port 1 = `.1`, port 5 = `.5`
- **Fan / PSU / Temp tables:** `UnsignedShort` — fan 1 = `.1`, PSU 2 = `.2`
- **Stats tables:** `portOrder` (Unsigned32) — entry 1 = `.1`, entry 5 = `.5`
- **Transceiver DDM:** `QsfpId` — QSFP slot 1 = `.1`, slot 3 = `.3`
- **LB Group / HB Profile / Inline Tool:** String-indexed (name encoded as ASCII octets in OID)

---

## 3. SNMP Traps (Asynchronous Notifications)

Traps are **push-based** — the device sends them to a configured SNMP manager when an event occurs.

**MIB File:** `NPB-TRAPS.mib`
**Base OID:** `1.3.6.1.4.1.47477.100.4.10` (notificationsMib)
**Notifications OID:** `1.3.6.1.4.1.47477.100.4.10.2` (notifications)
**Variables OID:** `1.3.6.1.4.1.47477.100.4.10.1` (trap varbind definitions)

**Total: 27 trap types organized into 7 categories**

### 3.1 Trap Variables (Varbinds)

These are the data fields carried inside traps.

| # | Variable Name | Full OID | Type | Used In |
|---|---------------|----------|------|---------|
| 1 | `module` | `1.3.6.1.4.1.47477.100.4.10.1.1` | AlarmModule | recovery, licenseValidation |
| 2 | `severity` | `1.3.6.1.4.1.47477.100.4.10.1.2` | AlarmSeverity | recovery, licenseValidation |
| 3 | `type` | `1.3.6.1.4.1.47477.100.4.10.1.3` | AlarmType | recovery, licenseValidation |
| 4 | `message` | `1.3.6.1.4.1.47477.100.4.10.1.4` | String | recovery, licenseValidation |
| 5 | `transceiverLevel` | `1.3.6.1.4.1.47477.100.4.10.1.5` | String | transceiver traps |
| 6 | `transceiverChannel` | `1.3.6.1.4.1.47477.100.4.10.1.6` | String | transceiver traps |
| 7 | `aaaContext` | `1.3.6.1.4.1.47477.100.4.10.1.7` | String | imageInstallation traps |
| 8 | `weightStr` | `1.3.6.1.4.1.47477.100.4.10.1.8` | String | loadBalance traps |
| 9 | `haClusterIdStr` | `1.3.6.1.4.1.47477.100.4.10.1.9` | String | HA traps |
| 10 | `haPeerIpStr` | `1.3.6.1.4.1.47477.100.4.10.1.10` | String | HA traps |
| 11 | `msgStr` | `1.3.6.1.4.1.47477.100.4.10.1.11` | String | HA traps |
| 12 | `remoteUrl` | `1.3.6.1.4.1.47477.100.4.10.1.12` | String | reboot, software traps |

### 3.2 Port Link Traps

| # | Trap Name | Full OID | Varbinds | Description |
|---|-----------|----------|----------|-------------|
| 1 | `portLinkUp` | `1.3.6.1.4.1.47477.100.4.10.2.101` | Port number, port speed | Link status changed to UP |
| 2 | `portLinkDown` | `1.3.6.1.4.1.47477.100.4.10.2.102` | Port number | Link status changed to DOWN |

### 3.3 Port Utilization Traps

| # | Trap Name | Full OID | Varbinds | Description |
|---|-----------|----------|----------|-------------|
| 3 | `portUtilizationRx` | `1.3.6.1.4.1.47477.100.4.10.2.121` | Port number, RX bps (1sec), RX % (1sec), raise threshold | RX utilization crossed threshold |
| 4 | `portUtilizationTx` | `1.3.6.1.4.1.47477.100.4.10.2.122` | Port number, TX bps (1sec), TX % (1sec), raise threshold | TX utilization crossed threshold |

### 3.4 Hardware Traps

| # | Trap Name | Full OID | Varbinds | Description |
|---|-----------|----------|----------|-------------|
| 5 | `fan` | `1.3.6.1.4.1.47477.100.4.10.2.201` | Fan number, front speed, rear speed | Fan status alarm |
| 6 | `psuPower` | `1.3.6.1.4.1.47477.100.4.10.2.202` | PSU number, voltage, PSU temp | PSU power status alarm |
| 7 | `psuTemperature` | `1.3.6.1.4.1.47477.100.4.10.2.203` | PSU number, voltage, PSU temp | PSU temperature alarm |
| 8 | `boardTemperature` | `1.3.6.1.4.1.47477.100.4.10.2.204` | Sensor temp, high threshold, sensor number | Board temperature alarm |

### 3.5 Transceiver DDM Traps

| # | Trap Name | Full OID | Varbinds | Description |
|---|-----------|----------|----------|-------------|
| 9 | `transceiverTemperature` | `1.3.6.1.4.1.47477.100.4.10.2.221` | QSFP ID, temperature, alarm level | Transceiver temperature alarm |
| 10 | `transceiverVoltage` | `1.3.6.1.4.1.47477.100.4.10.2.222` | QSFP ID, Vcc, alarm level | Transceiver voltage alarm |
| 11 | `transceiverCurrent` | `1.3.6.1.4.1.47477.100.4.10.2.223` | QSFP ID, channel, TX bias, alarm level | Transceiver bias current alarm |
| 12 | `transceiverTxPower` | `1.3.6.1.4.1.47477.100.4.10.2.224` | QSFP ID, channel, TX power, alarm level | Transceiver TX power alarm |
| 13 | `transceiverRxPower` | `1.3.6.1.4.1.47477.100.4.10.2.225` | QSFP ID, channel, RX power, alarm level | Transceiver RX power alarm |

### 3.6 System Lifecycle Traps

| # | Trap Name | Full OID | Varbinds | Description |
|---|-----------|----------|----------|-------------|
| 14 | `recovery` | `1.3.6.1.4.1.47477.100.4.10.2.300` | Module, severity, type, message | Recovery event occurred |
| 15 | `rebootCompleted` | `1.3.6.1.4.1.47477.100.4.10.2.301` | Remote URL, current boot bank | Reboot completed |
| 16 | `unexpectedShutdown` | `1.3.6.1.4.1.47477.100.4.10.2.304` | Remote URL, current boot bank | Unexpected shutdown |
| 17 | `imageInstallationStarted` | `1.3.6.1.4.1.47477.100.4.10.2.321` | Username, AAA context, remote URL, boot bank | Image installation started |
| 18 | `imageInstallationCompleted` | `1.3.6.1.4.1.47477.100.4.10.2.322` | Username, AAA context, remote URL, boot bank, upgrade status | Image installation completed |
| 19 | `softwareChanged` | `1.3.6.1.4.1.47477.100.4.10.2.323` | Remote URL, boot bank | Software changed |
| 20 | `licenseValidation` | `1.3.6.1.4.1.47477.100.4.10.2.330` | Module, severity, type, message | License validation event |

### 3.7 HA, Load Balancing, Heartbeat & Inline Traps

| # | Trap Name | Full OID | Varbinds | Description |
|---|-----------|----------|----------|-------------|
| 21 | `haStatusChanged` | `1.3.6.1.4.1.47477.100.4.10.2.350` | HA mode, cluster ID, peer IP | HA status changed |
| 22 | `haPeerConnection` | `1.3.6.1.4.1.47477.100.4.10.2.351` | HA mode, cluster ID, peer IP, message | HA peer connection status |
| 23 | `haDataReplicationStatus` | `1.3.6.1.4.1.47477.100.4.10.2.352` | Replication status, HA mode, cluster ID, peer IP | HA DB replication status |
| 24 | `loadBalancePortStatus` | `1.3.6.1.4.1.47477.100.4.10.2.400` | LB group ID, port number, weight | LB port status changed |
| 25 | `loadBalanceStandbyEvent` | `1.3.6.1.4.1.47477.100.4.10.2.401` | LB group ID, port numbers, weights | LB standby event |
| 26 | `heartbeatStatusChange` | `1.3.6.1.4.1.47477.100.4.10.2.450` | Profile name, port numbers, direction | Heartbeat status change |
| 27 | `inlineToolStatusChange` | `1.3.6.1.4.1.47477.100.4.10.2.451` | Tool name, port numbers, failover action | Inline tool status change |

---

## 4. Polled Metrics (SNMP GET -- Pull-Based)

**Total: ~390+ unique metrics across 8 MIB files**

> All examples use **port #1**, **fan #1**, **PSU #1**, **sensor #1**, **QSFP #1**, or **entry #1** as the row index.

### 4.1 System Details (Scalars)

**MIB File:** `NPB-SYSTEM.mib`
**OID Path:** `1.3.6.1.4.1.47477.100.4.1.1.12.{suffix}` -- scalars, append `.0` for SNMP GET

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Access | Description |
|---|-------------|----------|---------------------|------|--------|-------------|
| 1 | `systemDetailsModelName` | `1.3.6.1.4.1.47477.100.4.1.1.12.1` | `1.3.6.1.4.1.47477.100.4.1.1.12.1.0` | String | RO | Device model name |
| 2 | `systemDetailsSerialNumber` | `1.3.6.1.4.1.47477.100.4.1.1.12.2` | `1.3.6.1.4.1.47477.100.4.1.1.12.2.0` | String | RO | Device serial number |
| 3 | `systemDetailsSwVersion` | `1.3.6.1.4.1.47477.100.4.1.1.12.3` | `1.3.6.1.4.1.47477.100.4.1.1.12.3.0` | String | RO | Software version |
| 4 | `systemDetailsHwVersion` | `1.3.6.1.4.1.47477.100.4.1.1.12.4` | `1.3.6.1.4.1.47477.100.4.1.1.12.4.0` | String | RO | Hardware version |
| 5 | `systemDetailsSwitchName` | `1.3.6.1.4.1.47477.100.4.1.1.12.5` | `1.3.6.1.4.1.47477.100.4.1.1.12.5.0` | String | RO | Switch model name |
| 6 | `systemDetailsSwitchVersion` | `1.3.6.1.4.1.47477.100.4.1.1.12.6` | `1.3.6.1.4.1.47477.100.4.1.1.12.6.0` | String | RO | Switch version |
| 7 | `systemDetailsCpuType` | `1.3.6.1.4.1.47477.100.4.1.1.12.7` | `1.3.6.1.4.1.47477.100.4.1.1.12.7.0` | String | RO | CPU type and version |
| 8 | `systemDetailsHostname` | `1.3.6.1.4.1.47477.100.4.1.1.12.8` | `1.3.6.1.4.1.47477.100.4.1.1.12.8.0` | String (max 32) | RW | Device hostname |
| 9 | `systemDetailsDeviceHostname` | `1.3.6.1.4.1.47477.100.4.1.1.12.9` | `1.3.6.1.4.1.47477.100.4.1.1.12.9.0` | String | RO | Device hostname (operational) |
| 10 | `systemDetailsDescription` | `1.3.6.1.4.1.47477.100.4.1.1.12.10` | `1.3.6.1.4.1.47477.100.4.1.1.12.10.0` | String (max 140) | RW | Device description |
| 11 | `systemDetailsAppUptime` | `1.3.6.1.4.1.47477.100.4.1.1.12.11` | `1.3.6.1.4.1.47477.100.4.1.1.12.11.0` | String | RO | Application uptime |
| 12 | `systemDetailsSysUptime` | `1.3.6.1.4.1.47477.100.4.1.1.12.12` | `1.3.6.1.4.1.47477.100.4.1.1.12.12.0` | String | RO | System uptime |
| 13 | `systemDetailsHostnameDefault` | `1.3.6.1.4.1.47477.100.4.1.1.12.13` | `1.3.6.1.4.1.47477.100.4.1.1.12.13.0` | String (max 32) | RW | Reset hostname to default |

### 4.2 System Status -- CPU (Scalars)

**OID Path:** `1.3.6.1.4.1.47477.100.4.1.1.15.1.1.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Description |
|---|-------------|----------|---------------------|------|-------------|
| 1 | `systemStatusCpuCpuLoadAvgMin1` | `1.3.6.1.4.1.47477.100.4.1.1.15.1.1.1` | `1.3.6.1.4.1.47477.100.4.1.1.15.1.1.1.0` | String | 1-minute CPU load average |
| 2 | `systemStatusCpuCpuLoadAvgMin5` | `1.3.6.1.4.1.47477.100.4.1.1.15.1.1.2` | `1.3.6.1.4.1.47477.100.4.1.1.15.1.1.2.0` | String | 5-minute CPU load average |
| 3 | `systemStatusCpuCpuLoadAvgMin15` | `1.3.6.1.4.1.47477.100.4.1.1.15.1.1.3` | `1.3.6.1.4.1.47477.100.4.1.1.15.1.1.3.0` | String | 15-minute CPU load average |

### 4.3 System Status -- Memory (Scalars)

**OID Path:** `1.3.6.1.4.1.47477.100.4.1.1.15.2.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Description |
|---|-------------|----------|---------------------|------|-------------|
| 1 | `systemStatusMemoryTotal` | `1.3.6.1.4.1.47477.100.4.1.1.15.2.1` | `1.3.6.1.4.1.47477.100.4.1.1.15.2.1.0` | String (Bytes) | Total memory |
| 2 | `systemStatusMemoryUsed` | `1.3.6.1.4.1.47477.100.4.1.1.15.2.2` | `1.3.6.1.4.1.47477.100.4.1.1.15.2.2.0` | String (Bytes) | Used memory |
| 3 | `systemStatusMemoryAvailable` | `1.3.6.1.4.1.47477.100.4.1.1.15.2.3` | `1.3.6.1.4.1.47477.100.4.1.1.15.2.3.0` | String (Bytes) | Available memory |

### 4.4 Hardware Status -- Fans (per-fan table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.{column}.{fanIndex}`
**Index:** `systemHwStatusFanNumber` (UnsignedShort)

| # | Metric Name | Column OID | Fan #1 Example | Type | Description |
|---|-------------|------------|----------------|------|-------------|
| 1 | `systemHwStatusFanNumber` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.1` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.1.1` | UnsignedShort | Fan index (not-accessible) |
| 2 | `systemHwStatusFanAvailable` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.2` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.2.1` | TruthValue | Fan present/available |
| 3 | `systemHwStatusFanDirection` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.3` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.3.1` | Enum (Direction) | Airflow direction |
| 4 | `systemHwStatusFanFrontSpeed` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.4` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.4.1` | String (RPM) | Front fan speed |
| 5 | `systemHwStatusFanRearSpeed` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.5` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.5.1` | String (RPM) | Rear fan speed |
| 6 | `systemHwStatusFanStatus` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.6` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.6.1` | Enum: ok(0), alarm(1), unknown(2) | Fan status |
| 7 | `systemHwStatusFanLastFailureTimestamp` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.7` | `1.3.6.1.4.1.47477.100.4.1.1.13.1.1.7.1` | String | Last failure timestamp |

### 4.5 Hardware Status -- PSU (per-PSU table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.{column}.{psuIndex}`
**Index:** `systemHwStatusPsuNumber` (UnsignedShort)

| # | Metric Name | Column OID | PSU #1 Example | Type | Description |
|---|-------------|------------|----------------|------|-------------|
| 1 | `systemHwStatusPsuNumber` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.1` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.1.1` | UnsignedShort | PSU index (not-accessible) |
| 2 | `systemHwStatusPsuAvailable` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.2` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.2.1` | TruthValue | PSU present/available |
| 3 | `systemHwStatusPsuPsuDescr` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.3` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.3.1` | String | PSU description |
| 4 | `systemHwStatusPsuPowerGood` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.4` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.4.1` | Enum (ok/fail) | PSU power good |
| 5 | `systemHwStatusPsuVoltage` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.5` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.5.1` | String (Volts) | PSU voltage |
| 6 | `systemHwStatusPsuCurrent` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.6` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.6.1` | String (Amps) | PSU current draw |
| 7 | `systemHwStatusPsuPower` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.7` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.7.1` | String (Watts) | PSU power consumption |
| 8 | `systemHwStatusPsuPsuTemp` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.8` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.8.1` | Integer32 (C) | PSU temperature |
| 9 | `systemHwStatusPsuPsuOverTemp` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.9` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.9.1` | Enum (PsuOverType) | PSU over-temperature |
| 10 | `systemHwStatusPsuLastFailureTimestamp` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.10` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.10.1` | String | Last failure timestamp |
| 11 | `systemHwStatusPsuLowThresholdInVolts` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.11` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.11.1` | String (Volts) | Low voltage threshold |
| 12 | `systemHwStatusPsuHighThresholdInVolts` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.12` | `1.3.6.1.4.1.47477.100.4.1.1.13.2.1.12.1` | String (Volts) | High voltage threshold |

### 4.6 Hardware Status -- Temperature Sensors (per-sensor table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.{column}.{sensorIndex}`
**Index:** `systemHwStatusTemperatureSensorsNumber` (UnsignedShort)

| # | Metric Name | Column OID | Sensor #1 Example | Type | Description |
|---|-------------|------------|-------------------|------|-------------|
| 1 | `...TemperatureSensorsNumber` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.1` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.1.1` | UnsignedShort | Sensor index (not-accessible) |
| 2 | `...TemperatureSensorsDescr` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.2` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.2.1` | String | Sensor description |
| 3 | `...TemperatureSensorsStatus` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.3` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.3.1` | Enum: ok(0), alarm(1), unknown(2) | Sensor status |
| 4 | `...TemperatureSensorsTemp` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.4` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.4.1` | String (C) | Current temperature |
| 5 | `...TemperatureSensorsHighThresholdInCelsius` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.5` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.5.1` | String (C) | High temp threshold |
| 6 | `...TemperatureSensorsLowThresholdInCelsius` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.6` | `1.3.6.1.4.1.47477.100.4.1.1.13.3.1.6.1` | String (C) | Low temp threshold |

### 4.7 Software Upgrade Status (Scalars)

**OID Path:** `1.3.6.1.4.1.47477.100.4.1.1.14.1.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Description |
|---|-------------|----------|---------------------|------|-------------|
| 1 | `...BootBankCur` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.1` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.1.0` | Enum (SwBootBank) | Bank for next reboot |
| 2 | `...ActiveBootBank` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.2` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.2.0` | Enum (SwBootBank) | Bank actually booted from |
| 3 | `...ActiveSwVersion` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.3` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.3.0` | String | Active software version |
| 4 | `...ActiveSwImageFile` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.4` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.4.0` | String | SW image file of last upgrade |
| 5 | `...UpgradeStatus` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.5` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.5.0` | Enum (SwUpgradeStatus) | Last/current upgrade status |
| 6 | `...DownloadProgess` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.6` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.6.0` | Enum (SwUpgradeProgress) | Download progress % |
| 7 | `...ErrorMessage` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.7` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.7.0` | String | Upgrade error message |

**Bank A:** `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Description |
|---|-------------|----------|---------------------|------|-------------|
| 8 | `...SwBankABank` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.1` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.1.0` | Enum (SwBootBank) | Bank A identifier |
| 9 | `...SwBankASwVersion` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.2` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.2.0` | String | Bank A software version |
| 10 | `...SwBankASwImageFile` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.3` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.3.0` | String | Bank A image file |
| 11 | `...SwBankAStatus` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.4` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.8.4.0` | Enum (valid/corrupt/empty) | Bank A status |

**Bank B:** `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Description |
|---|-------------|----------|---------------------|------|-------------|
| 12 | `...SwBankBBank` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.1` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.1.0` | Enum (SwBootBank) | Bank B identifier |
| 13 | `...SwBankBSwVersion` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.2` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.2.0` | String | Bank B software version |
| 14 | `...SwBankBSwImageFile` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.3` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.3.0` | String | Bank B image file |
| 15 | `...SwBankBStatus` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.4` | `1.3.6.1.4.1.47477.100.4.1.1.14.1.9.4.0` | Enum (valid/corrupt/empty) | Bank B status |

### 4.8 Alarms Table

**OID Path:** `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.{column}.{alarmIndex}`

| # | Metric Name | Column OID | Alarm #1 Example | Type | Description |
|---|-------------|------------|------------------|------|-------------|
| 1 | `...AlarmLastUpdated` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.1` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.1.1` | Unsigned32 | Last updated timestamp |
| 2 | `...AlarmStatus` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.2` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.2.1` | Enum (AlarmManagerStatus) | Alarm status |
| 3 | `...AlarmCreation` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.3` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.3.1` | String | Alarm creation timestamp |
| 4 | `...AlarmClearance` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.4` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.4.1` | String | Alarm clearance timestamp |
| 5 | `...AlarmId` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.5` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.5.1` | Unsigned32 | Alarm ID |
| 6 | `...AlarmModule` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.6` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.6.1` | Enum (AlarmModule) | Alarm module source |
| 7 | `...AlarmSeverity` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.7` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.7.1` | Enum (AlarmSeverity) | Alarm severity |
| 8 | `...AlarmMessage` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.8` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.8.1` | String (max 256) | Alarm message text |
| 9 | `...AlarmType` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.9` | `1.3.6.1.4.1.47477.100.4.1.1.6.3.1.9.1` | Enum (AlarmType) | Alarm type |

### 4.9 Port Configuration & Status (per-port table)

**MIB File:** `NPB-PORTS.mib`
**OID Path:** `1.3.6.1.4.1.47477.100.4.2.1.4.1.{column}.{portIndex}`
**Index:** `portsPortLogicalPortNumber` (IMPLIED LogicalPortNum)

| # | Metric Name | Column OID | Port #1 Example | Type | Access | Description |
|---|-------------|------------|-----------------|------|--------|-------------|
| 1 | `portsPortLogicalPortNumber` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.1` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.1.1` | LogicalPortNum | NA | Port index (not-accessible) |
| 2 | `portsPortStatusMtu` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.2` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.2.1` | String | RO | Port MTU |
| 3 | `portsPortStatusLinkStatus` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.3` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.3.1` | Enum (LinkStatusType) | RO | Link status |
| 4 | `portsPortSpeed` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.4` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.4.1` | Enum (PortSpeedConfig) | RW | Configured speed |
| 5 | `portsPortFec` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.5` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.5.1` | Enum (FecStatus) | RW | FEC configuration |
| 6 | `portsPortVlan` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.6` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.6.1` | UnsignedShort (1-4094) | RW | VLAN tag |
| 7 | `portsPortIngressAction` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.7` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.7.1` | Enum (no-action/add/replace/remove) | RW | Ingress traffic action |
| 8 | `portsPortEgressAction` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.8` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.8.1` | Enum (no-action/remove) | RW | Egress traffic action |
| 9 | `portsPortMode` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.9` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.9.1` | Enum (PortRxTxModes) | RW | Port RX/TX mode |
| 10 | `portsPortTxLaser` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.10` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.10.1` | Enum: on(0), off(1) | RW | TX laser mode |
| 11 | `portsPortAdmin` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.11` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.11.1` | Enum: enable(0), disable(1) | RW | Admin enable/disable |
| 12 | `portsPortPortName` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.12` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.12.1` | String (max 48) | RW | Port name |
| 13 | `portsPortDescription` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.13` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.13.1` | String (max 140) | RW | Port description |
| 14 | `portsPortDoubleTag` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.14` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.14.1` | INTEGER | RW | Double-tag ethertype |
| 15 | `portsPortSetControlledPorts` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.15` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.15.1` | String | RW | Controlled ports |
| 16 | `portsPortMplsRemove` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.16` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.16.1` | Enum: enable(0), disable(1) | RW | MPLS header stripping |
| 17 | `portsPortPrbsType` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.17` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.17.1` | Enum (PrbsPolynomial) | RW | PRBS polynomial type |
| 18 | `portsPortPrbsEnabled` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.18` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.18.1` | TruthValue | RW | Enable/disable PRBS |
| 19 | `portsPortRxTimestamp` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.19` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.19.1` | Enum: enable(0), disable(1) | RW | RX timestamp insertion |
| 20 | `portsPortRxTimestampId` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.20` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.20.1` | Unsigned32 (0-8388607) | RW | RX timestamp ID |
| 21 | `portsPortTxTimestamp` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.21` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.21.1` | Enum: enable(0), disable(1) | RW | TX timestamp insertion |
| 22 | `portsPortTxTimestampId` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.22` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.22.1` | Unsigned32 (0-8388607) | RW | TX timestamp ID |
| 23 | `portsPortUtilAlertsRxRaiseThreshold` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.23` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.23.1` | Unsigned32 (0-100) | RW | RX utilization raise threshold % |
| 24 | `portsPortUtilAlertsRxClearThreshold` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.24` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.24.1` | Unsigned32 (0-100) | RW | RX utilization clear threshold % |
| 25 | `portsPortUtilAlertsRxAdmin` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.25` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.25.1` | Enum: enable(0), disable(1) | RW | RX utilization alert admin |
| 26 | `portsPortUtilAlertsTxRaiseThreshold` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.26` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.26.1` | Unsigned32 (0-100) | RW | TX utilization raise threshold % |
| 27 | `portsPortUtilAlertsTxClearThreshold` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.27` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.27.1` | Unsigned32 (0-100) | RW | TX utilization clear threshold % |
| 28 | `portsPortUtilAlertsTxAdmin` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.28` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.28.1` | Enum: enable(0), disable(1) | RW | TX utilization alert admin |
| 29 | `portsPortTxDescription` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.29` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.29.1` | String (max 140) | RW | Port TX description (simplex) |
| 30 | `portsPortRxDescription` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.30` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.30.1` | String (max 140) | RW | Port RX description (simplex) |
| 31 | `portsPortRowstatus` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.33` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.33.1` | RowStatus | RC | Create/delete port entry |

### 4.10 Port Statistics -- Summary (per-port table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.{column}.{portOrder}`
**Index:** `portOrder` (Unsigned32)

| # | Metric Name | Column OID | Port #1 Example | Type | Description |
|---|-------------|------------|-----------------|------|-------------|
| 1 | `...PortLogicalPortNumber` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.1` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.1.1` | LogicalPortNum | Port number |
| 2 | `...PortRxOctets` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.3` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.3.1` | Counter64 | Received octets |
| 3 | `...PortTxOctets` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.4` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.4.1` | Counter64 | Transmitted octets |
| 4 | `...PortRxPackets` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.5` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.5.1` | Counter64 | Received packets |
| 5 | `...PortTxPackets` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.6` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.6.1` | Counter64 | Transmitted packets |
| 6 | `...PortRxDiscards` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.7` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.7.1` | Counter64 | Received discards |
| 7 | `...PortTxDiscards` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.8` | `1.3.6.1.4.1.47477.100.4.2.2.5.1.1.8.1` | Counter64 | Transmitted discards |

### 4.11 Port Statistics -- Utilization (per-port table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.{column}.{portOrder}`
**Index:** `portStatisticsUtilizationPortOrder` (Unsigned32)

| # | Metric Name | Column OID | Port #1 Example | Type | Description |
|---|-------------|------------|-----------------|------|-------------|
| 1 | `...PortLogicalPortNumber` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.1` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.1.1` | LogicalPortNum | Port number |
| 3 | `...PortRxStatus` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.3` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.3.1` | Enum: ok(0), alarm(1), unknown(2) | RX utilization alert status |
| 4 | `...PortRxLastFailureTimestamp` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.4` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.4.1` | String | RX last failure |
| 5 | `...PortRxUtilPercentsAvg5min` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.5` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.5.1` | ConfdString (%) | RX utilization 5-min avg |
| 6 | `...PortRxUtilBpsAvg5min` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.6` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.6.1` | ConfdString (bps) | RX bandwidth 5-min avg |
| 7 | `...PortRxUtilPercentsCurrent1sec` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.7` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.7.1` | ConfdString (%) | RX utilization 1-sec |
| 8 | `...PortRxUtilBpsCurrent1sec` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.8` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.8.1` | ConfdString (bps) | RX bandwidth 1-sec |
| 9 | `...PortRxUtilPpsCurrent1sec` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.9` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.9.1` | ConfdString (pps) | RX packets/sec 1-sec |
| 10 | `...PortRxUtilTimestamp` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.10` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.10.1` | String | RX utilization timestamp |
| 11 | `...PortRxUtilPeakPercents` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.11` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.11.1` | ConfdString (%) | RX peak utilization % |
| 12 | `...PortRxUtilPeakBps` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.12` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.12.1` | ConfdString (bps) | RX peak bandwidth |
| 13 | `...PortRxUtilPeakTimestamp` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.13` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.13.1` | String | RX peak timestamp |
| 14 | `...PortTxStatus` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.14` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.14.1` | Enum: ok(0), alarm(1), unknown(2) | TX utilization alert status |
| 15 | `...PortTxLastFailureTimestamp` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.15` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.15.1` | String | TX last failure |
| 16 | `...PortTxUtilPercentsAvg5min` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.16` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.16.1` | ConfdString (%) | TX utilization 5-min avg |
| 17 | `...PortTxUtilBpsAvg5min` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.17` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.17.1` | ConfdString (bps) | TX bandwidth 5-min avg |
| 18 | `...PortTxUtilPercentsCurrent1sec` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.18` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.18.1` | ConfdString (%) | TX utilization 1-sec |
| 19 | `...PortTxUtilBpsCurrent1sec` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.19` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.19.1` | ConfdString (bps) | TX bandwidth 1-sec |
| 20 | `...PortTxUtilPpsCurrent1sec` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.20` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.20.1` | ConfdString (pps) | TX packets/sec 1-sec |
| 21 | `...PortTxUtilTimestamp` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.21` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.21.1` | String | TX utilization timestamp |
| 22 | `...PortTxUtilPeakPercents` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.22` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.22.1` | ConfdString (%) | TX peak utilization % |
| 23 | `...PortTxUtilPeakBps` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.23` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.23.1` | ConfdString (bps) | TX peak bandwidth |
| 24 | `...PortTxUtilPeakTimestamp` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.24` | `1.3.6.1.4.1.47477.100.4.2.2.4.1.1.24.1` | String | TX peak timestamp |

### 4.12 Port Statistics -- Errors (per-port table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.{column}.{portOrder}`

| # | Metric Name | Column OID | Port #1 Example | Type | Description |
|---|-------------|------------|-----------------|------|-------------|
| 2 | `...PortRxErrPkts` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.2` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.2.1` | Counter64 | Received error packets |
| 3 | `...PortTxErrPkts` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.3` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.3.1` | Counter64 | Transmitted error packets |
| 4 | `...PortRxIpErrPkts` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.4` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.4.1` | Counter64 | Received IP errors |
| 5 | `...PortRxJabber` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.5` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.5.1` | Counter64 | Received jabber |
| 6 | `...PortRxDropEvents` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.6` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.6.1` | Counter64 | Received drop events |
| 7 | `...PortRxFragments` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.7` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.7.1` | Counter64 | Received fragments |
| 8 | `...PortRxCollisions` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.8` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.8.1` | Counter64 | Collisions |
| 9 | `...PortRxDeferredCollisions` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.9` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.9.1` | Counter64 | Deferred collisions |
| 10 | `...PortRxLateCollisions` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.10` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.10.1` | Counter64 | Late collisions |
| 11 | `...PortRxExcessiveCollisions` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.11` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.11.1` | Counter64 | Excessive collisions |
| 12 | `...PortRxAlignErr` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.12` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.12.1` | Counter64 | Alignment errors |
| 13 | `...PortRxFcsErr` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.13` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.13.1` | Counter64 | FCS errors |
| 14 | `...PortRxMacErr` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.14` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.14.1` | Counter64 | RX MAC errors |
| 15 | `...PortTxMacErr` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.15` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.15.1` | Counter64 | TX MAC errors |
| 16 | `...PortRxCarrierSenseErr` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.16` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.16.1` | Counter64 | Carrier sense errors |
| 17 | `...PortRxFrameTooLong` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.17` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.17.1` | Counter64 | Long frame errors |
| 18 | `...PortRxSymbolErr` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.18` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.18.1` | Counter64 | Symbol errors |
| 19 | `...PortRxUnknownOpcode` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.19` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.19.1` | Counter64 | Unknown opcode |
| 20 | `...PortRxPausePkts` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.20` | `1.3.6.1.4.1.47477.100.4.2.2.3.1.1.20.1` | Counter64 | Pause packets |

### 4.13 Port Statistics -- FEC (per-port table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.2.2.8.1.1.{column}.{portOrder}`

| # | Metric Name | Column OID | Port #1 Example | Type | Description |
|---|-------------|------------|-----------------|------|-------------|
| 2 | `...PortFecCorrected` | `1.3.6.1.4.1.47477.100.4.2.2.8.1.1.2` | `1.3.6.1.4.1.47477.100.4.2.2.8.1.1.2.1` | Counter64 | FEC corrected codewords |
| 3 | `...PortFecUncorrected` | `1.3.6.1.4.1.47477.100.4.2.2.8.1.1.3` | `1.3.6.1.4.1.47477.100.4.2.2.8.1.1.3.1` | Counter64 | FEC uncorrected codewords |

### 4.14 Transceiver DDM (per-transceiver table)

**MIB File:** `NPB-PORTS.mib`
**OID Path:** `1.3.6.1.4.1.47477.100.4.2.3.1.1.{column}.{qsfpId}`
**Index:** `transceiversDdmQsfpId` (QsfpId)

| # | Metric Name | Column OID | QSFP #1 Example | Type | Description |
|---|-------------|------------|------------------|------|-------------|
| 1 | `...QsfpId` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.1` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.1.1` | QsfpId | QSFP ID (not-accessible) |
| 2 | `...SfpPresent` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.2` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.2.1` | Enum (SfpPresentType) | SFP present status |
| 3 | `...SfpTrxId` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.3` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.3.1` | String | Transceiver identifier |
| 4 | `...SfpTransceiver` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.4` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.4.1` | Enum (Transceiver) | Transceiver type |
| 5 | `...EncodingValues` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.5` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.5.1` | String | Encoding values |
| 6 | `...ConnectorTypes` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.6` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.6.1` | String | Connector type |
| 7 | `...SfpLength50um` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.7` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.7.1` | UnsignedShort (m) | Fiber length 50um |
| 8 | `...SfpLength625um` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.8` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.8.1` | UnsignedShort (m) | Fiber length 62.5um |
| 9 | `...SfpLengthOM3` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.9` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.9.1` | UnsignedShort (m) | Fiber length OM3 |
| 10 | `...SfpVendorName` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.10` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.10.1` | String | Vendor name |
| 11 | `...SfpVendorOui` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.11` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.11.1` | String | Vendor OUI |
| 12 | `...SfpVendorPN` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.12` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.12.1` | String | Vendor part number |
| 13 | `...SfpVendorSN` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.14` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.14.1` | String | Vendor serial number |
| 14 | `...QsfpTemperatureFlags` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.17` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.17.1` | Enum (SfpMonitorStatus) | Temperature alarm flags |
| 15 | `...QsfpVccFlags` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.18` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.18.1` | Enum (SfpMonitorStatus) | Supply voltage alarm flags |
| 16-23 | `...QsfpRxPowerFlags` 1-8 | `.19` through `.26` | `...2.3.1.1.19.1` through `...2.3.1.1.26.1` | Enum (SfpMonitorStatus) | RX power alarm flags (lanes 1-8) |
| 24-31 | `...QsfpTxBiasFlags` 1-8 | `.27` through `.34` | `...2.3.1.1.27.1` through `...2.3.1.1.34.1` | Enum (SfpMonitorStatus) | TX bias current alarm flags (lanes 1-8) |
| 32-39 | `...QsfpTxPowerFlags` 1-8 | `.35` through `.42` | `...2.3.1.1.35.1` through `...2.3.1.1.42.1` | Enum (SfpMonitorStatus) | TX power alarm flags (lanes 1-8) |
| 40 | `...SfpRtTemperature` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.43` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.43.1` | ConfdString (C) | Real-time temperature |
| 41 | `...SfpRtVcc` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.44` | `1.3.6.1.4.1.47477.100.4.2.3.1.1.44.1` | ConfdString (V) | Real-time supply voltage |

### 4.15 High Availability

**MIB File:** `NPB-HA.mib`

#### HA Configuration (Scalars)

**OID Path:** `1.3.6.1.4.1.47477.100.4.6.1.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Access | Description |
|---|-------------|----------|---------------------|------|--------|-------------|
| 1 | `haTouch` | `1.3.6.1.4.1.47477.100.4.6.1.1` | `1.3.6.1.4.1.47477.100.4.6.1.1.0` | Unsigned32 | RW | Touch/apply HA changes |
| 2 | `haMode` | `1.3.6.1.4.1.47477.100.4.6.1.2` | `1.3.6.1.4.1.47477.100.4.6.1.2.0` | Enum (HaMode) | RW | HA mode (active-active/active-standby) |
| 3 | `haFailoverMode` | `1.3.6.1.4.1.47477.100.4.6.1.3` | `1.3.6.1.4.1.47477.100.4.6.1.3.0` | Enum (HaFailoverMode) | RW | Failover mode (retain/revert) |
| 4 | `haHysteresis` | `1.3.6.1.4.1.47477.100.4.6.1.4` | `1.3.6.1.4.1.47477.100.4.6.1.4.0` | Unsigned32 (0-3600) | RW | Switchover hysteresis (seconds) |
| 5 | `haVirtualIp` | `1.3.6.1.4.1.47477.100.4.6.1.5` | `1.3.6.1.4.1.47477.100.4.6.1.5.0` | String | RW | Virtual IP for cluster |
| 6 | `haConflictResolveMode` | `1.3.6.1.4.1.47477.100.4.6.1.6` | `1.3.6.1.4.1.47477.100.4.6.1.6.0` | Enum (use-primary/manual) | RW | Conflict resolve mode |
| 7 | `haMonitoredPorts` | `1.3.6.1.4.1.47477.100.4.6.1.7` | `1.3.6.1.4.1.47477.100.4.6.1.7.0` | String | RW | Monitored ports for election |

#### HA Status & Operational Data

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Description |
|---|-------------|----------|---------------------|------|-------------|
| 8 | `haStatusHaModeUptime` | `1.3.6.1.4.1.47477.100.4.6.1.8.1` | `1.3.6.1.4.1.47477.100.4.6.1.8.1.0` | String | HA mode uptime |
| 9 | `haOperDataStatus` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.1` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.1.0` | Boolean | HA operational status |
| 10 | `haOperDataRole` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.2` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.2.0` | Enum (HaRole) | HA role |
| 11 | `haOperDataPeerIp` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.3` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.3.0` | String | Peer IP address |
| 12 | `haOperDataClusterId` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.4` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.4.0` | Unsigned32 | Cluster ID |
| 13 | `haOperDataUnsyncedCommits` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.5` | `1.3.6.1.4.1.47477.100.4.6.1.9.1.5.0` | Unsigned32 | Unsynced commits |
| 14 | `haOperHaMode` | `1.3.6.1.4.1.47477.100.4.6.1.9.2` | `1.3.6.1.4.1.47477.100.4.6.1.9.2.0` | Enum (pending/slave/master) | HA operational mode |
| 15 | `haOperDetectedPeerIp` | `1.3.6.1.4.1.47477.100.4.6.1.9.3` | `1.3.6.1.4.1.47477.100.4.6.1.9.3.0` | String | Detected peer IP |
| 16 | `haOperPeerStatus` | `1.3.6.1.4.1.47477.100.4.6.1.9.4` | `1.3.6.1.4.1.47477.100.4.6.1.9.4.0` | Boolean | Peer status |
| 17 | `haOperDataReplicationStatus` | `1.3.6.1.4.1.47477.100.4.6.1.9.5` | `1.3.6.1.4.1.47477.100.4.6.1.9.5.0` | Enum (unsynced/synced/conflict) | Replication status |
| 18 | `haOperLastSyncTimestamp` | `1.3.6.1.4.1.47477.100.4.6.1.9.6` | `1.3.6.1.4.1.47477.100.4.6.1.9.6.0` | String | Last sync timestamp |
| 19 | `haOperHaEnabledActiveStandbySlave` | `1.3.6.1.4.1.47477.100.4.6.1.9.7` | `1.3.6.1.4.1.47477.100.4.6.1.9.7.0` | Boolean | Active-standby slave enabled |
| 20 | `haOperLastHaModeChangedMsg` | `1.3.6.1.4.1.47477.100.4.6.1.9.8` | `1.3.6.1.4.1.47477.100.4.6.1.9.8.0` | String | Last HA mode change message |

### 4.16 Load Balancing

**MIB File:** `NPB-LB.mib`

#### LB Global Hash Configuration (Scalars)

**OID Path:** `1.3.6.1.4.1.47477.100.4.4.1.1.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Access | Description |
|---|-------------|----------|---------------------|------|--------|-------------|
| 1 | `lbHashHash` | `1.3.6.1.4.1.47477.100.4.4.1.1.1` | `1.3.6.1.4.1.47477.100.4.4.1.1.1.0` | ConfdString | RW | Hash function parameters |
| 2 | `lbHashSymmetric` | `1.3.6.1.4.1.47477.100.4.4.1.1.2` | `1.3.6.1.4.1.47477.100.4.4.1.1.2.0` | Enum (enable/disable) | RW | Symmetric hash |
| 3 | `lbHashFunc` | `1.3.6.1.4.1.47477.100.4.4.1.1.3` | `1.3.6.1.4.1.47477.100.4.4.1.1.3.0` | Enum (LbHashFunc) | RW | Hash function selection |
| 4 | `lbHashDlbMinThreshold` | `1.3.6.1.4.1.47477.100.4.4.1.1.4` | `1.3.6.1.4.1.47477.100.4.4.1.1.4.0` | Unsigned32 (0-100) | RW | DLB min threshold % |
| 5 | `lbHashDlbMaxThreshold` | `1.3.6.1.4.1.47477.100.4.4.1.1.5` | `1.3.6.1.4.1.47477.100.4.4.1.1.5.0` | Unsigned32 (0-100) | RW | DLB max threshold % |

#### LB Group Configuration (per-group table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.4.1.3.1.{column}.{groupIndex}`
**Index:** `lbGroupLbId` (String -- encoded as ASCII octets in OID)

| # | Metric Name | Column OID | Type | Access | Description |
|---|-------------|------------|------|--------|-------------|
| 1 | `lbGroupLbId` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.1` | String | RO | LB group ID (index) |
| 2 | `lbGroupDlbInactivity` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.2` | Unsigned32 (16-32000 us) | RW | DLB inactivity duration |
| 3 | `lbGroupHashGtpSize` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.3` | Enum (HashGtpFiltersNumber) | RW | GTP hash filter count |
| 4 | `lbGroupName` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.4` | String (max 48) | RW | LB group name |
| 5 | `lbGroupDescription` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.5` | String (max 128) | RW | LB group description |
| 6 | `lbGroupAlgo` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.6` | Enum (hash/round-robin/dlb) | RW | LB algorithm |
| 7 | `lbGroupOutputs` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.7` | String | RW | Output port members |
| 8 | `lbGroupOverload` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.8` | Enum (re-hash/drop) | RW | Overload behavior |
| 9 | `lbGroupFailoverHoldtime` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.9` | Unsigned32 (0-10000 ms) | RW | Failover hold time |
| 10 | `lbGroupStandby` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.10` | String | RW | Standby ports |
| 11 | `lbGroupStandbyFailover` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.11` | Enum (LbFailover) | RW | Standby failover behavior |
| 12 | `lbGroupFailoverAction` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.12` | Enum (nw-bypass/nw-drop/nw-down/per-tool/lb-bypass/lb-drop) | RW | Failover action |
| 13 | `lbGroupFailoverThreshold` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.13` | Unsigned32 (0=all) | RW | Failed tools threshold |
| 14 | `lbGroupShowActivePorts` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.14` | String | RO | Currently active ports |
| 15 | `lbGroupTouch` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.15` | Unsigned32 | RW | Touch/apply LB changes |
| 16 | `lbGroupRowstatus` | `1.3.6.1.4.1.47477.100.4.4.1.3.1.17` | RowStatus | RC | Create/delete LB group |

### 4.17 Heartbeat Profiles (per-profile table)

**MIB File:** `NPB-HB.mib`
**OID Path:** `1.3.6.1.4.1.47477.100.4.8.1.1.1.{column}.{profileIndex}`
**Index:** `heartbeatProfileName` (String -- encoded as ASCII octets in OID)

| # | Metric Name | Column OID | Type | Access | Description |
|---|-------------|------------|------|--------|-------------|
| 1 | `heartbeatProfileName` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.1` | String | RO | Profile name (index) |
| 2 | `heartbeatProfileDescription` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.2` | String (max 140) | RW | Profile description |
| 3 | `heartbeatProfilePktFormat` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.3` | Enum (ipx/garp/user-defined) | RW | HB packet format |
| 4 | `heartbeatProfilePktPattern` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.4` | String (max 512) | RW | User-defined packet pattern |
| 5 | `heartbeatProfileInterval` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.5` | Unsigned32 (30-5000 ms) | RW | Interval between HB packets |
| 6 | `heartbeatProfileTimeout` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.6` | Unsigned32 (20-3000 ms) | RW | Packet return timeout |
| 7 | `heartbeatProfileRetry` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.7` | Unsigned32 (0-5) | RW | Timed-out packets before failover |
| 8 | `heartbeatProfileRecoveryCount` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.8` | Unsigned32 (1-120) | RW | Successful packets before recovery |
| 9 | `heartbeatProfileDirection` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.9` | Enum (HbDirection) | RW | Traffic direction |
| 10 | `heartbeatProfileRowstatus` | `1.3.6.1.4.1.47477.100.4.8.1.1.1.11` | RowStatus | RC | Create/delete HB profile |

### 4.18 Inline Tools (per-tool table)

**MIB File:** `NPB-INLINE.mib`
**OID Path:** `1.3.6.1.4.1.47477.100.4.7.1.1.1.{column}.{toolIndex}`
**Index:** `inlineToolName` (String -- encoded as ASCII octets in OID)

| # | Metric Name | Column OID | Type | Access | Description |
|---|-------------|------------|------|--------|-------------|
| 1 | `inlineToolName` | `1.3.6.1.4.1.47477.100.4.7.1.1.1.1` | String | RO | Tool name (index) |
| 2 | `inlineToolDescription` | `1.3.6.1.4.1.47477.100.4.7.1.1.1.2` | String (max 140) | RW | Tool description |
| 3 | `inlineToolPortA` | `1.3.6.1.4.1.47477.100.4.7.1.1.1.3` | String | RW | Port A (to inline tool) |
| 4 | `inlineToolPortB` | `1.3.6.1.4.1.47477.100.4.7.1.1.1.4` | String | RW | Port B (from inline tool) |
| 5 | `inlineToolFailoverAction` | `1.3.6.1.4.1.47477.100.4.7.1.1.1.5` | Enum (InlineToolFailoverAction) | RW | Failover action on HB failure |
| 6 | `inlineToolHeartbeatProfile` | `1.3.6.1.4.1.47477.100.4.7.1.1.1.6` | String (max 16) | RW | Heartbeat profile name |
| 7 | `inlineToolStatus` | `1.3.6.1.4.1.47477.100.4.7.1.1.1.7` | String | RO | Inline tool status |
| 8 | `inlineToolRowstatus` | `1.3.6.1.4.1.47477.100.4.7.1.1.1.8` | RowStatus | RC | Create/delete inline tool |

### 4.19 Inline Toolchains (per-toolchain table)

**OID Path:** `1.3.6.1.4.1.47477.100.4.7.1.2.1.{column}.{chainIndex}`
**Index:** `inlineToolchainName` (String -- encoded as ASCII octets in OID)

| # | Metric Name | Column OID | Type | Access | Description |
|---|-------------|------------|------|--------|-------------|
| 1 | `inlineToolchainName` | `1.3.6.1.4.1.47477.100.4.7.1.2.1.1` | String | RO | Toolchain name (index) |
| 2 | `inlineToolchainDescription` | `1.3.6.1.4.1.47477.100.4.7.1.2.1.2` | String (max 140) | RW | Toolchain description |
| 3 | `inlineToolchainTools` | `1.3.6.1.4.1.47477.100.4.7.1.2.1.3` | String (max 3140) | RW | Comma-separated tool names |
| 4 | `inlineToolchainFailoverAction` | `1.3.6.1.4.1.47477.100.4.7.1.2.1.4` | Enum (InlineToolchainFailoverAction) | RW | Failover action |
| 5 | `inlineToolchainStatus` | `1.3.6.1.4.1.47477.100.4.7.1.2.1.5` | String | RO | Toolchain status |
| 6 | `inlineToolchainRowstatus` | `1.3.6.1.4.1.47477.100.4.7.1.2.1.6` | RowStatus | RC | Create/delete toolchain |

### 4.20 Filter Configuration -- Global Scalars

**MIB File:** `NPB-FILTERS.mib`
**OID Path:** `1.3.6.1.4.1.47477.100.4.3.1.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Access | Description |
|---|-------------|----------|---------------------|------|--------|-------------|
| 1 | `filtersMode` | `1.3.6.1.4.1.47477.100.4.3.1.2` | `1.3.6.1.4.1.47477.100.4.3.1.2.0` | Enum: l2-l4-ipv4(1), l3-l4-ipv4-udf(2), l3-l4-ipv4-ipv6-mpls(3), l3-l4-ipv4-udf-vlb(4), l2-l4-ipv6(6) | RW | Filter qualifier mode |
| 2 | `filtersModes` | `1.3.6.1.4.1.47477.100.4.3.1.3` | `1.3.6.1.4.1.47477.100.4.3.1.3.0` | TruthValue | RO | Filter mode capabilities |
| 3 | `filtersLbOperMode` | `1.3.6.1.4.1.47477.100.4.3.1.5` | `1.3.6.1.4.1.47477.100.4.3.1.5.0` | Enum: advanced(1), basic(2) | RW | Filters and LB groups operation mode |
| 4 | `filtersClear` | `1.3.6.1.4.1.47477.100.4.3.1.6` | `1.3.6.1.4.1.47477.100.4.3.1.6.0` | TruthValue | RW | Clear all filter statistics |
| 5 | `filtersTouch` | `1.3.6.1.4.1.47477.100.4.3.1.7` | `1.3.6.1.4.1.47477.100.4.3.1.7.0` | Unsigned32 | RW | Touch/apply filter changes |

#### UDF Window Configuration (Scalars)

**OID Path:** `1.3.6.1.4.1.47477.100.4.3.1.4.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Access | Description |
|---|-------------|----------|---------------------|------|--------|-------------|
| 6 | `filtersUdfWindowTunnel` | `1.3.6.1.4.1.47477.100.4.3.1.4.1` | `1.3.6.1.4.1.47477.100.4.3.1.4.1.0` | Enum: l2tp(1), none(2), gre(3), gre-ipv6(5), mpls(6), gtp(7), gtp-ipv4-and-ipv6-src-msb(8), gtp-ipv4-ipv6-dst-msb(9), pppoe(10) | RW | UDF tunnel type selection |
| 7 | `filtersUdfWindowTunnelsInfo` | `1.3.6.1.4.1.47477.100.4.3.1.4.2` | `1.3.6.1.4.1.47477.100.4.3.1.4.2.0` | TruthValue | RO | Tunnels UDF information |
| 8 | `filtersUdfWindowFormatsInfo` | `1.3.6.1.4.1.47477.100.4.3.1.4.3` | `1.3.6.1.4.1.47477.100.4.3.1.4.3.0` | TruthValue | RO | UDF formats information |

### 4.21 Filter Memory Status (Scalars)

**OID Path:** `1.3.6.1.4.1.47477.100.4.3.2.{suffix}`

| # | Metric Name | Full OID | GET Instance (`.0`) | Type | Description |
|---|-------------|----------|---------------------|------|-------------|
| 1 | `filterMemoryAvailableRanges` | `1.3.6.1.4.1.47477.100.4.3.2.1` | `...3.2.1.0` | Unsigned32 | Available filter ranges |
| 2 | `filterMemoryUsedRanges` | `1.3.6.1.4.1.47477.100.4.3.2.2` | `...3.2.2.0` | Unsigned32 | Used filter ranges |
| 3 | `filterMemoryTotalRanges` | `1.3.6.1.4.1.47477.100.4.3.2.3` | `...3.2.3.0` | Unsigned32 | Total filter ranges |
| 4 | `filterMemoryTotalRangesPerc` | `1.3.6.1.4.1.47477.100.4.3.2.4` | `...3.2.4.0` | ConfdString (%) | Total ranges utilization % |
| 5 | `filterMemoryAvailableEm` | `1.3.6.1.4.1.47477.100.4.3.2.5` | `...3.2.5.0` | Unsigned32 | Available exact-match entries |
| 6 | `filterMemoryUsedEm` | `1.3.6.1.4.1.47477.100.4.3.2.6` | `...3.2.6.0` | Unsigned32 | Used exact-match entries |
| 7 | `filterMemoryTotalEm` | `1.3.6.1.4.1.47477.100.4.3.2.7` | `...3.2.7.0` | Unsigned32 | Total exact-match entries |
| 8 | `filterMemoryTotalEmPerc` | `1.3.6.1.4.1.47477.100.4.3.2.8` | `...3.2.8.0` | ConfdString (%) | Exact-match utilization % |
| 9 | `filterMemoryAvailableSlices` | `1.3.6.1.4.1.47477.100.4.3.2.9` | `...3.2.9.0` | Unsigned32 | Available slices |
| 10 | `filterMemoryUsedSlices` | `1.3.6.1.4.1.47477.100.4.3.2.10` | `...3.2.10.0` | Unsigned32 | Used slices |
| 11 | `filterMemoryTotalSlices` | `1.3.6.1.4.1.47477.100.4.3.2.11` | `...3.2.11.0` | Unsigned32 | Total slices |
| 12 | `filterMemoryTotalSlicesPerc` | `1.3.6.1.4.1.47477.100.4.3.2.12` | `...3.2.12.0` | ConfdString (%) | Slices utilization % |
| 13 | `filterMemoryAvailableFilters` | `1.3.6.1.4.1.47477.100.4.3.2.13` | `...3.2.13.0` | Unsigned32 | Available filters |
| 14 | `filterMemoryUsedFilters` | `1.3.6.1.4.1.47477.100.4.3.2.14` | `...3.2.14.0` | Unsigned32 | Used filters |
| 15 | `filterMemoryTotalFilters` | `1.3.6.1.4.1.47477.100.4.3.2.15` | `...3.2.15.0` | Unsigned32 | Total filters (max 3070) |
| 16 | `filterMemoryTotalFiltersPerc` | `1.3.6.1.4.1.47477.100.4.3.2.16` | `...3.2.16.0` | ConfdString (%) | Filters utilization % |
| 17 | `filterMemoryAvailableIntFilters` | `1.3.6.1.4.1.47477.100.4.3.2.17` | `...3.2.17.0` | Unsigned32 | Available internal filters |
| 18 | `filterMemoryUsedIntFilters` | `1.3.6.1.4.1.47477.100.4.3.2.18` | `...3.2.18.0` | Unsigned32 | Used internal filters |
| 19 | `filterMemoryTotalIntFilters` | `1.3.6.1.4.1.47477.100.4.3.2.19` | `...3.2.19.0` | Unsigned32 | Total internal filters |
| 20 | `filterMemoryTotalIntFiltersPerc` | `1.3.6.1.4.1.47477.100.4.3.2.20` | `...3.2.20.0` | ConfdString (%) | Internal filters utilization % |
| 21 | `filterMemoryHwUpdate` | `1.3.6.1.4.1.47477.100.4.3.2.21` | `...3.2.21.0` | Unsigned32 | HW filter update status |

### 4.22 Filter Table (per-filter)

**OID Path:** `1.3.6.1.4.1.47477.100.4.3.1.8.1.{column}.{filterId}`
**Index:** `filtersFilterFilterId` (FilterId: 1-3070)

#### Filter Identity, Admin & Statistics

| # | Metric Name | Column | Filter #1 Example | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 1 | `filtersFilterFilterId` | `.1` | `...3.1.8.1.1.1` | FilterId (1-3070) | NA | Filter ID (index) |
| 2 | `filtersFilterGroup` | `.2` | `...3.1.8.1.2.1` | Fgroup (0-16 chars) | RC | Filter group name |
| 3 | `filtersFilterCreatedBy` | `.3` | `...3.1.8.1.3.1` | String (0-255) | RC | Filter creator |
| 4 | `filtersFilterStatsHitsPackets` | `.4` | `...3.1.8.1.4.1` | Counter64 | RO | Packets matched |
| 5 | `filtersFilterStatsHitsBytes` | `.5` | `...3.1.8.1.5.1` | Counter64 | RO | Bytes matched |
| 6 | `filtersFilterStatsHitsPps` | `.6` | `...3.1.8.1.6.1` | Counter64 | RO | Packets/sec matched |
| 7 | `filtersFilterStatsHitsBps` | `.7` | `...3.1.8.1.7.1` | Counter64 | RO | Bits/sec matched |
| 8 | `filtersFilterUsedHwFilters` | `.8` | `...3.1.8.1.8.1` | Integer32 | RO | HW filter entries used |
| 9 | **`filtersFilterAdmin`** | **`.9`** | **`...3.1.8.1.9.1`** | **INTEGER: enable(1), disable(2)** | **RC** | **Enable/disable filter** |
| 10 | `filtersFilterName` | `.10` | `...3.1.8.1.10.1` | String (0-48) | RC | Filter name |
| 11 | `filtersFilterDescription` | `.11` | `...3.1.8.1.11.1` | String (0-140) | RC | Filter description |
| 12 | `filtersFilterAction` | `.12` | `...3.1.8.1.12.1` | Enum: redirect(1), drop(2), copy(3) | RC | Match action |
| 13 | `filtersFilterOperator` | `.13` | `...3.1.8.1.13.1` | Enum: or(0), and(1) | RC | Classifier operator |
| 14 | `filtersFilterTags` | `.14` | `...3.1.8.1.14.1` | ConfdString | RC | Filter tags |
| 15 | `filtersFilterNot` | `.47` | `...3.1.8.1.47.1` | ConfdString | RC | Negate classifier match |

#### L2 Classifiers

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 16 | `filtersFilterL2Ethertype` | `.48` | String | RC | Ethertype match |
| 17 | `filtersFilterL2Smac` | `.49` | HexList (6 bytes) | RC | Source MAC |
| 18 | `filtersFilterL2SmacMask` | `.50` | HexList (6 bytes) | RC | Source MAC mask |
| 19 | `filtersFilterL2Dmac` | `.51` | HexList (6 bytes) | RC | Destination MAC |
| 20 | `filtersFilterL2DmacMask` | `.52` | HexList (6 bytes) | RC | Destination MAC mask |
| 21 | `filtersFilterL2Vlan` | `.53` | L2VlanType | RC | VLAN ID(s), 0=untagged |
| 22 | `filtersFilterL2InnerVlan` | `.54` | L2InnerVlanType | RC | Inner VLAN (Q-in-Q) |
| 23 | `filtersFilterL2InnerVlanMask` | `.15` | String | RC | Inner VLAN mask |
| 24 | `filtersFilterL2MplsLabel1` | `.16` | L2MplsType | RC | MPLS label 1 |
| 25 | `filtersFilterL2MplsLabel1Mask` | `.17` | String | RC | MPLS label 1 mask |
| 26 | `filtersFilterL2MplsLabel2` | `.18` | L2MplsType | RC | MPLS label 2 |
| 27 | `filtersFilterL2MplsLabel2Mask` | `.19` | String | RC | MPLS label 2 mask |
| 28 | `filtersFilterL2MplsLabel3` | `.20` | L2MplsType | RC | MPLS label 3 |
| 29 | `filtersFilterL2MplsLabel3Mask` | `.21` | String | RC | MPLS label 3 mask |
| 30 | `filtersFilterL2MplsLabel4` | `.22` | L2MplsType | RC | MPLS label 4 |
| 31 | `filtersFilterL2MplsLabel4Mask` | `.23` | String | RC | MPLS label 4 mask |

#### L3 Classifiers

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 32 | `filtersFilterL3Frag` | `.55` | Enum: any(0), first(1), not-first(2), none(3) | RC | IP fragmentation match |
| 33 | `filtersFilterL3ProtocolNumber` | `.56` | L3ProtType | RC | IP protocol number(s) |
| 34 | `filtersFilterL3Dscp` | `.57` | L3DscpType (1-63) | RC | DSCP value |
| 35 | `filtersFilterL3PktLen` | `.24` | PktLenRange | RC | IP packet length range |
| 36 | `filtersFilterL3Ipv4Addr` | `.58` | L3Ipv4Type | RC | IPv4 src or dst address |
| 37 | `filtersFilterL3Ipv4SrcAddr` | `.62` | L3Ipv4Type | RC | IPv4 source address |
| 38 | `filtersFilterL3Ipv4SrcNetmask` | `.25` | IpAddress | RC | IPv4 source netmask |
| 39 | `filtersFilterL3Ipv4DstAddr` | `.63` | L3Ipv4Type | RC | IPv4 destination address |
| 40 | `filtersFilterL3Ipv4DstNetmask` | `.26` | IpAddress | RC | IPv4 destination netmask |
| 41 | `filtersFilterL3Ipv6Addr` | `.64` | L3Ipv6Type | RC | IPv6 src or dst address |
| 42 | `filtersFilterL3Ipv6SrcAddr` | `.65` | L3Ipv6Type | RC | IPv6 source address |
| 43 | `filtersFilterL3Ipv6SrcPrefix` | `.27` | Unsigned32 | RC | IPv6 source prefix length |
| 44 | `filtersFilterL3Ipv6DstAddr` | `.66` | L3Ipv6Type | RC | IPv6 destination address |
| 45 | `filtersFilterL3Ipv6DstPrefix` | `.28` | Unsigned32 | RC | IPv6 destination prefix length |
| 46 | `filtersFilterL3IpList` | `.59` | String | RC | IP list reference (src+dst) |
| 47 | `filtersFilterL3IpSrcList` | `.60` | String | RC | IP source list reference |
| 48 | `filtersFilterL3IpDstList` | `.61` | String | RC | IP destination list reference |
| 49 | `filtersFilterL3Ipv4Session` | `.67` | L3Ipv4SessionType | RC | IPv4 session (IP+port) |
| 50 | `filtersFilterL3Ipv4SrcSession` | `.68` | L3Ipv4SessionType | RC | IPv4 source session |
| 51 | `filtersFilterL3Ipv4DstSession` | `.69` | L3Ipv4SessionType | RC | IPv4 destination session |
| 52 | `filtersFilterL3Ipv6Session` | `.70` | L3Ipv6SessionType | RC | IPv6 session |
| 53 | `filtersFilterL3Ipv6SrcSession` | `.71` | L3Ipv6SessionType | RC | IPv6 source session |
| 54 | `filtersFilterL3Ipv6DstSession` | `.72` | L3Ipv6SessionType | RC | IPv6 destination session |

#### L4 Classifiers

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 55 | `filtersFilterL4Port` | `.73` | L4PortType | RC | L4 src or dst port |
| 56 | `filtersFilterL4Sport` | `.74` | L4PortType | RC | L4 source port |
| 57 | `filtersFilterL4Dport` | `.75` | L4PortType | RC | L4 destination port |
| 58 | `filtersFilterL4TcpflagUrg` | `.76` | TruthValue | RC | TCP URG flag match |
| 59 | `filtersFilterL4TcpflagAck` | `.77` | TruthValue | RC | TCP ACK flag match |
| 60 | `filtersFilterL4TcpflagPsh` | `.78` | TruthValue | RC | TCP PSH flag match |
| 61 | `filtersFilterL4TcpflagRst` | `.79` | TruthValue | RC | TCP RST flag match |
| 62 | `filtersFilterL4TcpflagSyn` | `.80` | TruthValue | RC | TCP SYN flag match |
| 63 | `filtersFilterL4TcpflagFin` | `.81` | TruthValue | RC | TCP FIN flag match |

#### Tunnel Classifiers

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 64 | `filtersFilterTunnelL2tp` | `.29` | TruthValue | RC | Match L2TP tunnel |
| 65 | `filtersFilterTunnelGre` | `.30` | TruthValue | RC | Match GRE tunnel |
| 66 | `filtersFilterTunnelMpls` | `.31` | TruthValue | RC | Match MPLS tunnel |
| 67 | `filtersFilterTunnelPppoe` | `.32` | TruthValue | RC | Match PPPoE tunnel |
| 68 | `filtersFilterTunnelGtp` | `.82` | TruthValue | RC | Match GTP tunnel |
| 69 | `filtersFilterGtpMsgType` | `.33` | UnsignedByte | RC | GTP message type |
| 70 | `filtersFilterGtpTeid` | `.34` | String | RC | GTP TEID |
| 71 | `filtersFilterGtpTeidMask` | `.35` | String | RC | GTP TEID mask |
| 72 | `filtersFilterTunnelProtocolNumber` | `.83` | L3ProtType | RC | Inner protocol number |
| 73 | `filtersFilterTunnelIpv4Addr` | `.84` | L3Ipv4Type | RC | Inner IPv4 address |
| 74 | `filtersFilterTunnelIpv4SrcAddr` | `.85` | L3Ipv4Type | RC | Inner IPv4 source |
| 75 | `filtersFilterTunnelIpv4SrcNetmask` | `.36` | IpAddress | RC | Inner IPv4 source netmask |
| 76 | `filtersFilterTunnelIpv4DstAddr` | `.86` | L3Ipv4Type | RC | Inner IPv4 destination |
| 77 | `filtersFilterTunnelIpv4DstNetmask` | `.37` | IpAddress | RC | Inner IPv4 dst netmask |
| 78 | `filtersFilterTunnelIpv6Addr` | `.87` | L3Ipv6Type | RC | Inner IPv6 address |
| 79 | `filtersFilterTunnelIpv6SrcAddr` | `.88` | L3Ipv6Type | RC | Inner IPv6 source |
| 80 | `filtersFilterTunnelIpv6SrcPrefix` | `.38` | Unsigned32 | RC | Inner IPv6 src prefix |
| 81 | `filtersFilterTunnelIpv6DstAddr` | `.89` | L3Ipv6Type | RC | Inner IPv6 destination |
| 82 | `filtersFilterTunnelIpv6DstPrefix` | `.39` | Unsigned32 | RC | Inner IPv6 dst prefix |
| 83 | `filtersFilterTunnelL4Port` | `.90` | TunnelL4PortType | RC | Inner L4 port |
| 84 | `filtersFilterTunnelL4Sport` | `.91` | TunnelL4PortType | RC | Inner L4 source port |
| 85 | `filtersFilterTunnelL4SportMask` | `.40` | String | RC | Inner L4 src port mask |
| 86 | `filtersFilterTunnelL4Dport` | `.92` | TunnelL4PortType | RC | Inner L4 destination port |
| 87 | `filtersFilterTunnelL4DportMask` | `.41` | String | RC | Inner L4 dst port mask |

#### Packet Modification Actions

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 88 | `filtersFilterSetSlice` | `.93` | UnsignedShort | RC | Packet slice size |
| 89 | `filtersFilterSetTimestamp` | `.94` | TruthValue | RC | Insert timestamp |
| 90 | `filtersFilterVlanSetOuter` | `.95` | UnsignedShort | RC | Set outer VLAN tag |
| 91 | `filtersFilterSetVirtualLb` | `.96` | VlanRangeString | RC | Virtual LB VLAN range |
| 92 | `filtersFilterSetVirtualLbSource` | `.97` | Enum: primary(0), secondary(1) | RC | Virtual LB source |
| 93 | `filtersFilterBidirectional` | `.98` | TruthValue | RC | Bidirectional filter |
| 94 | `filtersFilterSmacReplace` | `.99` | MacAddress | RC | Replace source MAC |
| 95 | `filtersFilterDmacReplace` | `.100` | MacAddress | RC | Replace destination MAC |

#### Input/Output Port Mapping

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 96 | `filtersFilterInputPorts` | `.101` | String | RC | Input port(s) |
| 97 | `filtersFilterInputInterface` | `.102` | GreInterface | RC | Input GRE interface |
| 98 | `filtersFilterInputLbGroup` | `.103` | LbListAsString | RC | Input LB group |
| 99 | `filtersFilterOutputPorts` | `.104` | String | RC | Output port(s) |
| 100 | `filtersFilterOutputLbGroup` | `.106` | LbListAsString | RC | Output LB group |
| 101 | `filtersFilterOutputInterface` | `.107` | GreInterface | RC | Output GRE interface |
| 102 | `filtersFilterInline` | `.108` | String | RC | Inline toolchain reference |
| 103 | `filtersFilterRowstatus` | `.105` | RowStatus | RC | Create/delete filter |

### 4.23 Filter UDF Patterns (per-filter, per-UDF)

**OID Path:** `1.3.6.1.4.1.47477.100.4.3.3.1.{column}.{filterId}.{udfName}`
**Index:** `filtersFilterFilterId` + `filtersFilterUdfName` (compound)

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 1 | `filtersFilterUdfName` | `.1` | UdfLeafref (1-32 chars) | NA | UDF name (index) |
| 2 | `filtersFilterUdfPattern` | `.2` | UdfPattern | RC | 2-16 byte pattern to match in UDF window |
| 3 | `filtersFilterUdfRowstatus` | `.4` | RowStatus | RC | Create/delete UDF pattern |

### 4.24 IP Lists (per-list)

**OID Path:** `1.3.6.1.4.1.47477.100.4.3.1.9.1.{column}.{listName}`
**Index:** `filtersIpListListName` (String -- encoded as ASCII octets in OID)

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 1 | `filtersIpListListName` | `.1` | String (1-16) | NA | IP list name (index) |
| 2 | `filtersIpListUseHw` | `.2` | INTEGER: enable(1), disable(2) | RC | HW accelerator for large address lists |
| 3 | `filtersIpListTouch` | `.3` | Unsigned32 | RC | Touch/apply IP list changes |
| 4 | `filtersIpListDescription` | `.4` | String (0-32) | RC | IP list description |
| 5 | `filtersIpListRulesNum` | `.8` | Unsigned32 | RO | Number of rules in this list |
| 6 | `filtersIpListRowstatus` | `.7` | RowStatus | RC | Create/delete IP list |

### 4.25 Filter Groups (per-group)

**OID Path:** `1.3.6.1.4.1.47477.100.4.3.1.1.1.1.{column}.{groupName}`
**Index:** `filtersGroupsGroupName` (Fgroup -- encoded as ASCII octets in OID)

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 1 | `filtersGroupsGroupName` | `.1` | Fgroup (0-16) | NA | Group name (index) |
| 2 | `filtersGroupsGroupDescription` | `.2` | String (0-32) | RC | Group description |
| 3 | `filtersGroupsGroupContinueFiltersOnly` | `.3` | TruthValue | RC | Group can only contain continue-action filters |
| 4 | `filtersGroupsGroupPermissions` | `.4` | Enum: admin(0), oper(1) | RC | Write permissions for group |
| 5 | `filtersGroupsGroupNum` | `.7` | Unsigned32 | RO | Number of filters in group |
| 6 | `filtersGroupsGroupRowstatus` | `.6` | RowStatus | RC | Create/delete group |

### 4.26 Filter Group Members (per-group, per-filter)

**OID Path:** `1.3.6.1.4.1.47477.100.4.3.4.1.{column}.{groupName}.{filterId}`
**Index:** `filtersGroupsGroupName` + `filtersGroupsGroupFilterFilterId` (compound)

> This table mirrors the filter table (4.22) but scoped within a group. Each filter-in-group has its own admin enable/disable, classifiers, and actions — identical structure to the main filter table. Key fields:

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 1 | `filtersGroupsGroupFilterFilterId` | `.1` | FilterId (1-3070) | NA | Filter ID within group (index) |
| 2 | `filtersGroupsGroupFilterGroup` | `.2` | Fgroup | RC | Group reference |
| 3 | `filtersGroupsGroupFilterCreatedBy` | `.3` | String (0-255) | RC | Filter creator |
| 4 | **`filtersGroupsGroupFilterAdmin`** | **`.4`** | **INTEGER: enable(1), disable(2)** | **RC** | **Enable/disable filter within group** |
| 5 | `filtersGroupsGroupFilterName` | `.5` | String (0-48) | RC | Filter name |
| 6 | `filtersGroupsGroupFilterDescription` | `.6` | String (0-140) | RC | Filter description |
| 7 | `filtersGroupsGroupFilterAction` | `.7` | Enum: redirect(1), drop(2), copy(3) | RC | Match action |
| 8 | `filtersGroupsGroupFilterOperator` | `.8` | Enum: or(0), and(1) | RC | Classifier operator |
| 9–103 | *(L2/L3/L4/tunnel classifiers, packet modifications, I/O ports)* | | | RC | Same fields as filter table (4.22) |
| 104 | `filtersGroupsGroupFilterRowstatus` | — | RowStatus | RC | Create/delete filter in group |

### 4.27 UDF Window Definitions (per-UDF)

**OID Path:** `1.3.6.1.4.1.47477.100.4.3.1.4.4.1.{column}.{udfName}`
**Index:** `filtersUdfWindowUdfName` (String -- encoded as ASCII octets in OID)

| # | Metric Name | Column | Type | Access | Description |
|---|-------------|--------|------|--------|-------------|
| 1 | `filtersUdfWindowUdfName` | `.1` | String (1-32) | NA | UDF name (index) |
| 2 | `filtersUdfWindowUdfDescription` | `.2` | String (1-128) | RC | UDF description |
| 3 | `filtersUdfWindowUdfStartPoint` | `.3` | Enum: l2(1), l3(2), l4(3), vlan(4), mpls(5), gre(6) | RC | Packet start point |
| 4 | `filtersUdfWindowUdfOffset` | `.4` | PacketOffset (0-126) | RC | Byte offset from start point |
| 5 | `filtersUdfWindowUdfUsed` | `.5` | String | RO | Filters using this UDF |
| 6 | `filtersUdfWindowUdfFormat` | `.6` | PacketUdfFormat (35 values) | RC | Packet format qualifier |
| 7 | `filtersUdfWindowUdfLength` | `.7` | UnsignedByte (1-32) | RC | Match length in bytes |
| 8 | `filtersUdfWindowUdfTermFlow` | `.8` | Enum: none(1), mpls-l2(2), mpls-l3(3) | RC | Tunnel termination flow |
| 9 | `filtersUdfWindowUdfRowstatus` | `.9` | RowStatus | RC | Create/delete UDF |

---

## 5. SNMP Commands (SET Operations)

All `RW` (read-write) and `RC` (read-create) objects from Section 4 are SET-able. This section summarizes the command count by MIB file.

**Total: ~250+ command objects across 8 MIB files**

| MIB File | Commands | Key OID Branches |
|----------|----------|------------------|
| **NPB-FILTERS.mib** | ~100 | Filter admin (enable/disable), L2-L4 classifiers, tunnel classifiers, packet modifications, I/O port mapping, filter groups, IP lists, UDF windows |
| **NPB-SYSTEM.mib** | 68 | System scalars, user mgmt tables, ACL tables, syslog rules, NTP servers, network interfaces, static routes, remote auth servers |
| **NPB-PORTS.mib** | 39 | Global port settings, breakout config, per-port config (speed, FEC, VLAN, MPLS, timestamps, utilization alerts) |
| **NPB-LB.mib** | 19 | Hash config (5 scalars), LB group table (14 columns) |
| **NPB-INLINE.mib** | 10 | Inline tool table (5 RW + 1 RC), toolchain table (3 RW + 1 RC) |
| **NPB-HB.mib** | 9 | Heartbeat profile table (7 RW + 1 RC + name) |
| **NPB-HA.mib** | 7 | HA config scalars (haTouch through haMonitoredPorts) |

### Key System Commands

| # | Command Name | Full OID | Type | Description |
|---|-------------|----------|------|-------------|
| 1 | `systemDetailsHostname` | `1.3.6.1.4.1.47477.100.4.1.1.12.8` | String (max 32) | Set device hostname |
| 2 | `systemDetailsDescription` | `1.3.6.1.4.1.47477.100.4.1.1.12.10` | String (max 140) | Set device description |
| 3 | `systemHwLocationLed` | `1.3.6.1.4.1.47477.100.4.1.1.1.1` | LedControlOper | Toggle location LED |
| 4 | `systemLogLevel` | `1.3.6.1.4.1.47477.100.4.1.1.3.1` | INTEGER (normal/debug) | Set log level |
| 5 | `systemAlarmsSyslogEnabled` | `1.3.6.1.4.1.47477.100.4.1.1.6.1.1` | TruthValue | Enable/disable alarm syslog |
| 6 | `systemAlarmsTrapEnabled` | `1.3.6.1.4.1.47477.100.4.1.1.6.2.1` | TruthValue | Enable/disable alarm traps |
| 7 | `systemSecurityBlockIncomingPing` | `1.3.6.1.4.1.47477.100.4.1.1.7.1` | TruthValue | Block incoming ICMP ping |
| 8 | `systemTimeAndDateNtpAdmin` | `1.3.6.1.4.1.47477.100.4.1.1.9.5.1` | INTEGER (enable/disable) | Enable/disable NTP |
| 9 | `systemDnsNameservers` | `1.3.6.1.4.1.47477.100.4.1.1.17.1` | ConfdString | DNS nameserver list |

### Key Port Commands (per-port -- append `.{portIndex}`)

| # | Command Name | Column OID | Port #1 Example | Type | Description |
|---|-------------|------------|-----------------|------|-------------|
| 1 | `portsPortAdmin` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.11` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.11.1` | Enum (enable/disable) | Port admin status |
| 2 | `portsPortSpeed` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.4` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.4.1` | Enum (PortSpeedConfig) | Port speed |
| 3 | `portsPortFec` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.5` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.5.1` | Enum (FecStatus) | FEC config |
| 4 | `portsPortVlan` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.6` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.6.1` | UnsignedShort (1-4094) | VLAN tag |
| 5 | `portsPortTxLaser` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.10` | `1.3.6.1.4.1.47477.100.4.2.1.4.1.10.1` | Enum (on/off) | TX laser |
| 6 | `portStatisticsClearAll` | `1.3.6.1.4.1.47477.100.4.2.2.6` | `1.3.6.1.4.1.47477.100.4.2.2.6.0` | Unsigned32 | Clear all port stats |

---

## 6. Core Feature Deep Dive -- Filters, Inline Bypass & Load Balancing

### 6.1 Inline Tool Bypass

The NPB can insert security tools **inline** in the network path. Unlike the OBP device (which does optical-layer bypass), the NPB handles bypass at the **packet broker layer** with heartbeat monitoring.

```
Network A --> NPB Port A --> Inline Tool --> NPB Port B --> Network B
                  |                                |
                  +-- Heartbeat packets -----------+
                       (IPX / GARP / custom)
```

**How it works:**

1. **Define a heartbeat profile** (`1.3.6.1.4.1.47477.100.4.8.1.1.1`) -- packet format, interval, timeout, retry count
2. **Define an inline tool** (`1.3.6.1.4.1.47477.100.4.7.1.1.1`) -- port A, port B, heartbeat profile, failover action
3. **Optionally chain tools** (`1.3.6.1.4.1.47477.100.4.7.1.2.1`) -- multiple inline tools in series
4. The NPB sends heartbeat packets through the tool and monitors for their return
5. If heartbeats fail, the configured **failover action** triggers:
   - `tool-bypass` -- skip the failed tool, pass traffic directly
   - `tool-drop` -- drop traffic (tool is mandatory)
   - Other actions per toolchain configuration

**Recovery:** After `heartbeatProfileRecoveryCount` consecutive successful heartbeats, the tool is restored to active.

### 6.2 Traffic Filter Rules

The NPB uses **filter rules** to steer traffic from network/TAP ports to tool ports based on packet attributes (L2-L7). Filters are a separate feature from inline tool bypass — they control **which traffic goes where**, not tool health.

**How it works:**

```
Traffic in (input ports) --> NPB evaluates filters in order (1-3070)
    |
    +--> Filter match? --> Action: redirect / drop / copy --> output ports / LB group / inline toolchain
    |
    +--> No match? --> Next filter (or default action)
```

**Key capabilities:**
- Up to **3070 filters** per device, organized into **filter groups** (max 99 groups)
- Each filter has an **admin enable/disable** toggle (`filtersFilterAdmin`)
- Classifiers combinable with **AND/OR operator** per filter
- L2-L7 deep packet inspection including inner tunnel headers
- **User Defined Fields (UDF)** for custom byte-pattern matching at any packet offset
- **IP lists** for bulk address matching with optional HW acceleration
- Per-filter **hit counters** (packets, bytes, pps, bps) for monitoring
- Filters can reference **inline toolchains** directly via `filtersFilterInline`

**Filter modes (global):**

| Mode | Description |
|------|-------------|
| `l2-l4-ipv4` | L2 through L4 with IPv4 |
| `l3-l4-ipv4-udf` | L3-L4 IPv4 with UDF support |
| `l3-l4-ipv4-ipv6-mpls` | L3-L4 IPv4 + IPv6 + MPLS |
| `l3-l4-ipv4-udf-vlb` | L3-L4 IPv4 + UDF + virtual LB |
| `l2-l4-ipv6` | L2 through L4 with IPv6 |

**SNMP filter enable/disable example:**
```
Enable filter #1:   SET 1.3.6.1.4.1.47477.100.4.3.1.8.1.9.1 = 1  (enable)
Disable filter #1:  SET 1.3.6.1.4.1.47477.100.4.3.1.8.1.9.1 = 2  (disable)
Apply changes:      SET 1.3.6.1.4.1.47477.100.4.3.1.7.0 = 1      (filtersTouch)
```

### 6.3 Load Balancing

The NPB distributes traffic across tool ports to prevent overload and enable horizontal scaling.

**Algorithms:**
- **Hash** -- Deterministic flow-based distribution (same flow always goes to same tool)
- **Round-Robin** -- Even packet distribution across ports
- **DLB (Dynamic Load Balancing)** -- Rebalances based on port utilization thresholds

**Failover chain:**
```
LB Group (ports) -> Port fails -> failoverHoldtime wait -> standby port activates
                                                        -> if all fail -> failoverAction triggers
```

**Failover actions:** `nw-bypass`, `nw-drop`, `nw-down`, `per-tool`, `lb-bypass`, `lb-drop`

### 6.4 High Availability

Two NPB devices form a cluster for redundancy.

**Modes:**
- **Active-Active** -- Both devices process traffic
- **Active-Standby** -- One processes, one waits

**Key parameters:**
- `haHysteresis` (`1.3.6.1.4.1.47477.100.4.6.1.4`) -- Prevents flapping (0-3600 seconds)
- `haFailoverMode` (`1.3.6.1.4.1.47477.100.4.6.1.3`) -- `retain` (stay on new master) or `revert` (return to original)
- `haMonitoredPorts` (`1.3.6.1.4.1.47477.100.4.6.1.7`) -- Ports checked for master/slave election
- `haConflictResolveMode` (`1.3.6.1.4.1.47477.100.4.6.1.6`) -- `use-primary` or `manual` on split-brain

---

## 7. Quick OID Reference -- Branch Map

| Branch OID | MIB File | Purpose |
|------------|----------|---------|
| `1.3.6.1.4.1.47477.100.4.1` | NPB-SYSTEM.mib | System config, status, HW, alarms, users, security |
| `1.3.6.1.4.1.47477.100.4.1.1.1` | NPB-SYSTEM.mib | Hardware (location LED) |
| `1.3.6.1.4.1.47477.100.4.1.1.6` | NPB-SYSTEM.mib | Alarms (syslog, trap, alarm table) |
| `1.3.6.1.4.1.47477.100.4.1.1.12` | NPB-SYSTEM.mib | System details (model, serial, version, hostname) |
| `1.3.6.1.4.1.47477.100.4.1.1.13` | NPB-SYSTEM.mib | Hardware status (fans, PSU, temp sensors) |
| `1.3.6.1.4.1.47477.100.4.1.1.14` | NPB-SYSTEM.mib | Software upgrade status |
| `1.3.6.1.4.1.47477.100.4.1.1.15` | NPB-SYSTEM.mib | System status (CPU, memory) |
| `1.3.6.1.4.1.47477.100.4.3` | NPB-FILTERS.mib | Filters, filter groups, IP lists, UDF windows |
| `1.3.6.1.4.1.47477.100.4.3.1.1` | NPB-FILTERS.mib | Filter groups table |
| `1.3.6.1.4.1.47477.100.4.3.1.4` | NPB-FILTERS.mib | UDF window config & UDF table |
| `1.3.6.1.4.1.47477.100.4.3.1.8` | NPB-FILTERS.mib | Filter table (main, up to 3070 filters) |
| `1.3.6.1.4.1.47477.100.4.3.1.9` | NPB-FILTERS.mib | IP list table |
| `1.3.6.1.4.1.47477.100.4.3.2` | NPB-FILTERS.mib | Filter memory status |
| `1.3.6.1.4.1.47477.100.4.3.3` | NPB-FILTERS.mib | Filter UDF patterns table |
| `1.3.6.1.4.1.47477.100.4.3.4` | NPB-FILTERS.mib | Filter group members table |
| `1.3.6.1.4.1.47477.100.4.2` | NPB-PORTS.mib | Ports, statistics, transceivers |
| `1.3.6.1.4.1.47477.100.4.2.1.4` | NPB-PORTS.mib | Port configuration table |
| `1.3.6.1.4.1.47477.100.4.2.2.1` | NPB-PORTS.mib | Port statistics -- packet size |
| `1.3.6.1.4.1.47477.100.4.2.2.2` | NPB-PORTS.mib | Port statistics -- traffic types |
| `1.3.6.1.4.1.47477.100.4.2.2.3` | NPB-PORTS.mib | Port statistics -- errors |
| `1.3.6.1.4.1.47477.100.4.2.2.4` | NPB-PORTS.mib | Port statistics -- utilization |
| `1.3.6.1.4.1.47477.100.4.2.2.5` | NPB-PORTS.mib | Port statistics -- summary |
| `1.3.6.1.4.1.47477.100.4.2.2.8` | NPB-PORTS.mib | Port statistics -- FEC |
| `1.3.6.1.4.1.47477.100.4.2.3.1` | NPB-PORTS.mib | Transceiver DDM table |
| `1.3.6.1.4.1.47477.100.4.4` | NPB-LB.mib | Load balancing |
| `1.3.6.1.4.1.47477.100.4.4.1.1` | NPB-LB.mib | LB hash configuration |
| `1.3.6.1.4.1.47477.100.4.4.1.3` | NPB-LB.mib | LB group table |
| `1.3.6.1.4.1.47477.100.4.6` | NPB-HA.mib | High availability |
| `1.3.6.1.4.1.47477.100.4.7` | NPB-INLINE.mib | Inline tools & toolchains |
| `1.3.6.1.4.1.47477.100.4.7.1.1` | NPB-INLINE.mib | Inline tool table |
| `1.3.6.1.4.1.47477.100.4.7.1.2` | NPB-INLINE.mib | Inline toolchain table |
| `1.3.6.1.4.1.47477.100.4.8` | NPB-HB.mib | Heartbeat profiles |
| `1.3.6.1.4.1.47477.100.4.10` | NPB-TRAPS.mib | All trap notifications |
| `1.3.6.1.4.1.47477.100.4.10.1` | NPB-TRAPS.mib | Trap variables (varbinds) |
| `1.3.6.1.4.1.47477.100.4.10.2` | NPB-TRAPS.mib | Trap notifications |

---

## 8. NPB vs OBP -- Comparison

| Aspect | NPB-2E | OTS3000-BPS (OBP) |
|--------|--------|---------------------|
| **Purpose** | Traffic visibility -- aggregate, filter, distribute to tools | Optical failover -- bypass inline tools on failure |
| **Layer** | Packet broker (L2/L3/L4 awareness) | Optical layer (physical fiber switching) |
| **MIB Files** | 11 files | 1 file (BYPASS-CGS.mib) |
| **Base OID** | `1.3.6.1.4.1.47477.100.4` | `1.3.6.1.4.1.47477.10.21` |
| **Architecture** | Single device, SNMP tables indexed by port/fan/PSU | 32 independent link branches, each with identical OIDs |
| **Trap Types** | 27 unique traps | 2 NMU + 33 per-link (x32 = 1,058 instances) |
| **Metrics** | ~390+ unique objects | 16 NMU + 58 per-link (x32 = 1,872 instances) |
| **Commands** | ~250+ unique objects | 8 NMU + 33 per-link (x32 = 1,064 instances) |
| **Bypass** | Software-level via inline tool failover | Hardware-level optical switch |
| **Heartbeat** | Configurable profiles (IPX/GARP/custom packets) | Active (ICMP ping) + Passive (traffic flow) |
| **HA** | Full active-active/active-standby clustering | None (standalone) |
| **Load Balancing** | Hash, round-robin, DLB with failover | None |
