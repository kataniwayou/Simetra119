# Technology Stack — v1.7 Configuration Consistency & Tenant Commands

**Project:** Simetra119 SNMP Collector
**Researched:** 2026-03-14
**Milestone scope:** SET command data model, CommunityString validation, self-describing tenant entries, config rename tenantvector.json → tenants.json
**SET execution:** OUT OF SCOPE — data model and validation only

---

## Executive Decision

**Zero new NuGet packages.** All required types exist in SharpSnmpLib 12.5.7, the .NET 9 BCL, and existing configuration infrastructure. The v1.7 changes are purely additive: new fields on existing config models, a new config options class, and extended validator logic.

---

## Existing Stack (No Changes)

| Technology | Version | Role |
|------------|---------|------|
| .NET / C# | 9.0 | Runtime |
| Lextm.SharpSnmpLib | 12.5.7 | SNMP protocol — SnmpType enum, ISnmpData hierarchy |
| MediatR | 12.5.0 | Pipeline behaviors |
| KubernetesClient | 18.0.13 | ConfigMap watchers |
| Microsoft.Extensions.Options | 9.0.0 | Validated config binding |
| System.Collections.Frozen | BCL | FrozenDictionary volatile-swap pattern |

---

## Device Analysis: Actual SET Command Value Types

### OBP Device (OTS3000-BPS) — Source: Docs/OBP-Device-Analysis.md

The OBP exposes 31 SET-able commands across NMU and per-link levels. Full type inventory:

| SNMP Type | Commands |
|-----------|----------|
| `Integer32` | `tcpPort`, `startDelay`, `deviceAddress`, `ReturnDelay`, `BackDelay`, `ActiveSendInterval`, `ActiveTimeOut`, `ActiveLossBypass`, `PassiveTimeOut`, `PassiveLossBypass` |
| `INTEGER` (enum) | `keyLock` (lock=0/unlock=1), `buzzerSet` (off=0/on=1), `WorkMode` (manualMode=0/autoMode=1), `Channel` (bypass=0/primary=1), `R1Wave`–`R4Wave` (w1310nm=0/w1550nm=1/na=2), `BackMode`, `SwitchProtect`, `ActiveHeartSwitch`, `PassiveHeartSwitch`, `PowerAlarmBypass4` (7 values) |
| `IpAddress` | `ipAddress`, `subnetMask`, `gateWay` (NMU network config), `PingIpAddress` (per-link heartbeat) |
| `DisplayString` | `R1AlarmPower`–`R4AlarmPower` (dBm threshold values such as "-20.5") |

Note: `INTEGER` (SNMP enumerated type) is transmitted as `Integer32` on the wire. The config format does not need to distinguish between "plain integer" and "enumerated integer" — `Integer32` covers both.

### NPB Device (CGS NPB-2E) — Source: Docs/NPB-Device-Analysis.md

The NPB exposes ~250+ SET-able commands. Key type distribution:

| SNMP Type | Representative Commands |
|-----------|------------------------|
| `Integer32` / `Unsigned32` | `haHysteresis` (0-3600 seconds), `portsPortVlan` (1-4094), `portsPortRxTimestampId` (0-8388607), utilization thresholds (0-100) |
| `INTEGER` (enum) | `haMode`, `haFailoverMode`, `portsPortSpeed`, `portsPortFec`, `portsPortTxLaser`, `portsPortAdmin`, `systemLogLevel`, `systemTimeAndDateNtpAdmin` |
| `String` / `DisplayString` | `systemDetailsHostname` (max 32), `systemDetailsDescription` (max 140), `haVirtualIp`, `haMonitoredPorts`, `systemDnsNameservers`, `portsPortPortName` (max 48) |
| `TruthValue` | `systemAlarmsSyslogEnabled`, `systemAlarmsTrapEnabled`, `systemSecurityBlockIncomingPing`, `portsPortPrbsEnabled` |

Note: `TruthValue` in SNMPv2 is `Integer32` on the wire (1 = true, 2 = false). It does not require a separate config type.

### Consolidated Type Set Across Both Devices

After surveying all writable OIDs in both device MIBs, three wire types cover 100% of actual SET targets:

1. **Integer32** — plain integers (ports, delays, thresholds, counts) and all enumerated types (on/off, mode, channel, wavelength, bypass trigger, TruthValue). Everything that sends a numeric value to the device.
2. **IpAddress** — IPv4 address fields (OBP NMU network config, OBP ping target). Not present in NPB writable commands.
3. **OctetString / DisplayString** — human-readable string values (dBm thresholds like "-20.5", hostnames, descriptions, nameserver lists).

No writable OID in either target device requires `Gauge32`, `Counter32`, `Counter64`, `TimeTicks`, or `ObjectIdentifier`. Those are read-only metric types on these devices.

---

## Recommended: ValueType as a String in Config

### Decision: string, not C# enum

Represent `ValueType` as a `string` property on `CommandSlotOptions`, validated at config load time against a fixed allowed set. Do not use a C# `enum` type.

**Why string, not C# enum:**

- Operators edit JSON directly. A C# `enum` provides zero IDE assistance for a JSON config file. The failure mode for an invalid value is worse ("cannot convert 'integer32' to SnmpValueType") than a custom validator message ("ValueType must be one of: Integer32, IpAddress, OctetString").
- The allowed set is small and stable — 3 values cover both current target devices entirely. Adding a value in the future is a one-line validator change with no serialization migration.
- Consistent with the existing project pattern: `MetricName`, `CommandName`, `CommunityString` are all `string` fields validated at load time rather than typed at the model level.
- Using a C# enum would require `[JsonConverter(typeof(JsonStringEnumConverter))]` on the property and adds ceremony for no material benefit in a data model that is never switched on until SET execution (which is out of scope this milestone).

**Allowed ValueType strings:** `"Integer32"`, `"IpAddress"`, `"OctetString"`

These names match MIB terminology and are close to (but not identical to) SharpSnmpLib enum member names — see the mapping note in the next section.

### SharpSnmpLib Type Mapping

Verified by reading source code: `ValueExtractionBehavior.cs` and `OtelMetricHandler.cs` both switch on `SnmpType`. SharpSnmpLib 12.5.7 (confirmed in `SnmpCollector.csproj`) provides:

| Config ValueType string | SharpSnmpLib SnmpType enum member | SharpSnmpLib concrete class | Constructor |
|------------------------|-----------------------------------|-----------------------------|-------------|
| `"Integer32"` | `SnmpType.Integer32` | `Integer32` | `new Integer32(int value)` |
| `"IpAddress"` | `SnmpType.IPAddress` | `IP` | `new IP(string ipv4DotNotation)` |
| `"OctetString"` | `SnmpType.OctetString` | `OctetString` | `new OctetString(string value)` |

The one mapping mismatch: the SharpSnmpLib enum is `SnmpType.IPAddress` (all-caps IP), but the config string is `"IpAddress"` (MIB Pascal case). The future SET executor needs a single switch or dictionary to convert config string to SharpSnmpLib type. This is a known, one-time translation — not a design inconsistency. Keeping the config string as `"IpAddress"` is better than `"IPAddress"` because it is easier for operators to type correctly and matches how device MIB docs write it.

---

## Recommended Data Models

### CommandSlotOptions (new file)

```csharp
namespace SnmpCollector.Configuration;

public sealed class CommandSlotOptions
{
    /// Human-readable device name. Self-describing — no DeviceRegistry lookup.
    /// Optional informational field. Routing uses Ip + Port.
    public string Device { get; set; } = string.Empty;

    /// IP address of the target device. Must be a valid IPv4 or IPv6 address.
    public string Ip { get; set; } = string.Empty;

    /// SNMP port. Defaults to 161. Must be 1–65535.
    public int Port { get; set; } = 161;

    /// SNMP community string. Must start with "Simetra." prefix.
    public string CommunityString { get; set; } = string.Empty;

    /// Human-readable command name. Must exist in commandmap.json.
    public string CommandName { get; set; } = string.Empty;

    /// The value to SET, stored as a string. Interpreted per ValueType at execution time.
    /// Examples: "1", "0", "10.0.0.1", "-20.5"
    public string Value { get; set; } = string.Empty;

    /// SNMP wire type. Must be one of: Integer32, IpAddress, OctetString.
    public string ValueType { get; set; } = string.Empty;
}
```

### MetricSlotOptions (extended — existing file)

Two new fields added to the existing model. Existing fields unchanged.

```csharp
public sealed class MetricSlotOptions
{
    // NEW: self-describing device name. Informational; routing uses Ip + Port.
    public string Device { get; set; } = string.Empty;

    public string Ip { get; set; } = string.Empty;              // existing
    public int Port { get; set; } = 161;                        // existing

    // NEW: community string. Must start with "Simetra." prefix.
    public string CommunityString { get; set; } = string.Empty;

    public string MetricName { get; set; } = string.Empty;      // existing
    public int TimeSeriesSize { get; set; } = 1;                // existing
}
```

### TenantOptions (extended — existing file)

```csharp
public sealed class TenantOptions
{
    public int Priority { get; set; }
    public List<MetricSlotOptions> Metrics { get; set; } = [];
    public List<CommandSlotOptions> Commands { get; set; } = []; // NEW
}
```

### TenantsOptions (renamed from TenantVectorOptions)

```csharp
public sealed class TenantsOptions
{
    public const string SectionName = "Tenants";   // was "TenantVector"
    public List<TenantOptions> Tenants { get; set; } = [];
}
```

---

## CommunityString Validation

### The existing authoritative source

`CommunityStringHelper` (verified by reading source at `Pipeline/CommunityStringHelper.cs`) defines:

```csharp
private const string CommunityPrefix = "Simetra.";

internal static bool TryExtractDeviceName(string community, out string deviceName)
{
    if (community.StartsWith(CommunityPrefix, StringComparison.Ordinal)
        && community.Length > CommunityPrefix.Length)
    { ... return true; }
    ...
}
```

The validation rule must use this exact method to ensure the validator and the runtime extraction logic can never diverge. Do not duplicate the prefix string or write a separate regex.

### Accessibility requirement

`CommunityStringHelper` is currently `internal static` in the `SnmpCollector.Pipeline` namespace. The validator will be in `SnmpCollector.Configuration.Validators`. Both are in the same assembly, so `internal` access is sufficient — no visibility change needed.

Add a new public surface method to `CommunityStringHelper`:

```csharp
internal static bool IsValidCommunityString(string community)
    => community.StartsWith(CommunityPrefix, StringComparison.Ordinal)
       && community.Length > CommunityPrefix.Length;
```

This keeps `TryExtractDeviceName` (which has an `out` parameter) for runtime use and gives the validator a clean single-concern method.

### Validator logic

The existing `TenantVectorOptionsValidator` is currently a no-op. For v1.7, it becomes a real validator:

```csharp
// Validate MetricSlotOptions.CommunityString
if (!CommunityStringHelper.IsValidCommunityString(slot.CommunityString))
{
    failures.Add(
        $"Tenants[{i}].Metrics[{j}].CommunityString '{slot.CommunityString}' " +
        "must start with 'Simetra.' followed by a device name");
}

// Validate CommandSlotOptions.ValueType
private static readonly HashSet<string> AllowedValueTypes =
    new(StringComparer.Ordinal) { "Integer32", "IpAddress", "OctetString" };

if (!AllowedValueTypes.Contains(cmd.ValueType))
{
    failures.Add(
        $"Tenants[{i}].Commands[{j}].ValueType '{cmd.ValueType}' " +
        "must be one of: Integer32, IpAddress, OctetString");
}
```

---

## Config File Rename and JSON Shape

### File rename

| Aspect | Current | New |
|--------|---------|-----|
| Config file | `config/tenantvector.json` | `config/tenants.json` |
| C# class | `TenantVectorOptions` | `TenantsOptions` |
| JSON section key | `"TenantVector"` | `"Tenants"` |
| Watcher service | `TenantVectorWatcherService` | Rename to `TenantsWatcherService` or keep name |
| K8s ConfigMap key | `tenantvector.json` | `tenants.json` |

### Recommended JSON shape for tenants.json

The current format has redundant nesting: the file has a `"TenantVector"` wrapper object containing a `"Tenants"` array. Flattening is possible with the rename but requires changing how `IConfiguration` binds the section. The simpler path is to keep the wrapper but change its key name:

```json
{
  "Tenants": {
    "Tenants": [
      {
        "Priority": 1,
        "Metrics": [
          {
            "Device": "obp-core-01",
            "Ip": "10.0.10.1",
            "Port": 161,
            "CommunityString": "Simetra.obp-core-01",
            "MetricName": "obp_link_state_L1",
            "TimeSeriesSize": 1
          }
        ],
        "Commands": [
          {
            "Device": "obp-core-01",
            "Ip": "10.0.10.1",
            "Port": 161,
            "CommunityString": "Simetra.obp-core-01",
            "CommandName": "linkN_Channel",
            "Value": "0",
            "ValueType": "Integer32"
          }
        ]
      }
    ]
  }
}
```

Whether to flatten (single `"Tenants": [...]` at root) or keep the wrapper object is an implementation decision. The wrapper approach requires less code change (only `SectionName` constant changes). The flatten approach is cleaner for operators but requires updating the `Configure<TenantsOptions>` binding call. Either is acceptable — the research does not require one over the other.

---

## What NOT to Add

| Omission | Rationale |
|----------|-----------|
| C# `enum` for ValueType | No operator benefit in JSON config; string validated at load time is simpler and more operator-friendly |
| `ISnmpData` factory at config load time | SET execution is out of scope; Value+ValueType → ISnmpData conversion belongs in the future SET executor, not the config model |
| Regex-based community string validation | `CommunityStringHelper.IsValidCommunityString` is the authoritative parser; a regex duplicate is a consistency hazard |
| `Gauge32`, `Counter32`, `Counter64` in AllowedValueTypes | Not present as a writable type in either target device's MIB; add only when a new device requires it |
| Hex-encoded OctetString support | All string SETs in both device MIBs are human-readable text (dBm values, hostnames); raw hex encoding is not needed |
| New NuGet packages | All required types are in BCL, SharpSnmpLib 12.5.7, or existing Microsoft.Extensions packages |
| Per-command OID resolution at config load | CommandName → OID mapping happens via ICommandMapService at execution time, same as how MetricName → OID works today. Do not pre-resolve at config load. |
| TenantId field | Priority field handles ordering; an explicit ID is scope creep not required by any consumer |

---

## Integration with Existing Patterns

The v1.7 changes follow every established pattern in the codebase:

| Pattern | Existing example | v1.7 application |
|---------|-----------------|-----------------|
| String field validated at load against allowed set | `DevicesOptionsValidator` port range check | `AllowedValueTypes` HashSet check on `CommandSlotOptions.ValueType` |
| Community string convention helper | `CommunityStringHelper.TryExtractDeviceName` in pipeline | `CommunityStringHelper.IsValidCommunityString` in validator |
| `IValidateOptions<T>` with `List<string>` failures | `DevicesOptionsValidator`, `LeaseOptionsValidator` | Extended `TenantsOptionsValidator` |
| FrozenDictionary volatile-swap for config models | `TenantVectorRegistry`, `CommandMapService` | No change — these already exist |
| `List<CommandSlotOptions>` pattern | n/a (new) | Mirrors `List<MetricSlotOptions>` shape |

No new architectural patterns are introduced. All v1.7 code will be immediately recognizable to anyone familiar with the existing codebase.

---

## Confidence Assessment

| Area | Confidence | Source |
|------|------------|--------|
| OBP SET type inventory | HIGH | Read exhaustively from Docs/OBP-Device-Analysis.md Section 5 |
| NPB SET type inventory | HIGH | Read exhaustively from Docs/NPB-Device-Analysis.md Section 5 |
| Three types cover all current devices | HIGH | Derived from complete MIB analysis above |
| SharpSnmpLib type names and versions | HIGH | Read from ValueExtractionBehavior.cs, OtelMetricHandler.cs, SnmpCollector.csproj |
| CommunityStringHelper convention | HIGH | Read directly from CommunityStringHelper.cs source |
| Existing MetricSlotOptions / TenantOptions shape | HIGH | Read directly from MetricSlotOptions.cs, TenantOptions.cs, TenantVectorOptions.cs |
| JSON shape recommendation | MEDIUM | Design inference; exact binding approach (wrapper vs flat) is an implementation choice |

---

## Sources

All sources are in the repository and were read directly:

- `Docs/OBP-Device-Analysis.md` — Section 5: SNMP Commands, complete writable OID type inventory
- `Docs/NPB-Device-Analysis.md` — Section 5: SNMP Commands, complete writable OID type inventory
- `src/SnmpCollector/Pipeline/Behaviors/ValueExtractionBehavior.cs` — live SnmpType enum usage confirming Integer32, Gauge32, TimeTicks, Counter32, Counter64, OctetString, IPAddress, ObjectIdentifier members
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — corroborating SnmpType usage
- `src/SnmpCollector/Pipeline/CommunityStringHelper.cs` — authoritative community string convention source
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — existing metric slot model
- `src/SnmpCollector/Configuration/TenantOptions.cs` — existing tenant model
- `src/SnmpCollector/Configuration/TenantVectorOptions.cs` — existing top-level config shape
- `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` — current no-op validator
- `src/SnmpCollector/SnmpCollector.csproj` — SharpSnmpLib 12.5.7 version confirmed
- `src/SnmpCollector/config/tenantvector.json` — current config file shape and section key

---

*Stack research for: v1.7 Configuration Consistency & Tenant Commands*
*Researched: 2026-03-14*
