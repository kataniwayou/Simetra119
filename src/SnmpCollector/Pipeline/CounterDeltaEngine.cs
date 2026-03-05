using System.Collections.Concurrent;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Computes and records counter deltas. Maintains per-OID+agent state to track
/// previous cumulative values and per-device sysUpTime for reboot detection.
/// </summary>
public interface ICounterDeltaEngine
{
    /// <summary>
    /// Compute and record a counter delta. Returns true if a delta was emitted
    /// to the metric factory, false on first-poll baseline (stored but not emitted).
    /// </summary>
    bool RecordDelta(
        string oid,
        string agent,
        string source,
        string metricName,
        SnmpType typeCode,
        ulong currentValue,
        uint? sysUpTimeCentiseconds);
}

/// <summary>
/// Singleton service implementing all five counter delta computation paths:
/// first-poll baseline skip, normal increment, Counter32 wrap-around at 2^32,
/// Counter64 decrease (always treated as reboot), and sysUpTime-based reboot detection.
/// </summary>
public sealed class CounterDeltaEngine : ICounterDeltaEngine
{
    /// <summary>2^32 — the rollover point for 32-bit SNMP counters.</summary>
    private const ulong Counter32Max = 4_294_967_296UL;

    /// <summary>
    /// Previous cumulative value per OID+agent combination.
    /// Key format: "oid|agent" — unique per OID per device.
    /// </summary>
    private readonly ConcurrentDictionary<string, ulong> _lastValues = new();

    /// <summary>
    /// Most recent sysUpTime (centiseconds) per device (agent).
    /// One uptime value is shared across all OIDs for a given device.
    /// </summary>
    private readonly ConcurrentDictionary<string, uint> _lastSysUpTimes = new();

    private readonly ISnmpMetricFactory _metricFactory;
    private readonly ILogger<CounterDeltaEngine> _logger;

    public CounterDeltaEngine(ISnmpMetricFactory metricFactory, ILogger<CounterDeltaEngine> logger)
    {
        _metricFactory = metricFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool RecordDelta(
        string oid,
        string agent,
        string source,
        string metricName,
        SnmpType typeCode,
        ulong currentValue,
        uint? sysUpTimeCentiseconds)
    {
        var key = $"{oid}|{agent}";

        // Atomically store currentValue and capture the previous value if one existed.
        ulong? previousValue = null;
        _lastValues.AddOrUpdate(
            key,
            addValueFactory: _ => currentValue,
            updateValueFactory: (_, prev) =>
            {
                previousValue = prev;
                return currentValue;
            });

        // Detect reboot: sysUpTime decreased since the last poll for this device.
        bool sysUpTimeDecreased =
            sysUpTimeCentiseconds.HasValue &&
            _lastSysUpTimes.TryGetValue(agent, out var lastUpTime) &&
            sysUpTimeCentiseconds.Value < lastUpTime;

        // Update per-device sysUpTime regardless of decrease direction.
        if (sysUpTimeCentiseconds.HasValue)
            _lastSysUpTimes[agent] = sysUpTimeCentiseconds.Value;

        // First poll: baseline stored, no delta emitted.
        if (previousValue is null)
        {
            _logger.LogDebug(
                "First poll baseline stored: Oid={Oid} Agent={Agent} Value={Value}",
                oid, agent, currentValue);
            return false;
        }

        double delta;

        if (currentValue >= previousValue.Value)
        {
            // Path 1: Normal increment — counter advanced forward.
            delta = currentValue - previousValue.Value;
        }
        else if (sysUpTimeDecreased)
        {
            // Path 2: Reboot confirmed by sysUpTime decrease — use current value as delta.
            delta = currentValue;
            _logger.LogInformation(
                "Reboot detected (sysUpTime decreased): Oid={Oid} Agent={Agent}",
                oid, agent);
        }
        else if (typeCode == SnmpType.Counter32)
        {
            // Path 3: Counter32 wrap-around — counter rolled over at 2^32.
            delta = (Counter32Max - (uint)previousValue.Value) + currentValue;
            _logger.LogDebug(
                "Counter32 wrap-around: Oid={Oid} Agent={Agent} Previous={Previous} Current={Current}",
                oid, agent, previousValue.Value, currentValue);
        }
        else
        {
            // Path 4: Counter64 decrease with no wrap (64-bit counters don't wrap in practice).
            //          Also covers Counter32 decrease when sysUpTime is unavailable.
            //          Treat conservatively as reboot — use current value as delta.
            delta = currentValue;
            _logger.LogInformation(
                "Reboot detected (counter decreased, no wrap): Oid={Oid} Agent={Agent} TypeCode={TypeCode}",
                oid, agent, typeCode);
        }

        // Clamp to non-negative — counters must never decrease in Prometheus.
        delta = Math.Max(0.0, delta);
        _metricFactory.RecordCounter(metricName, oid, agent, source, delta);
        return true;
    }
}
