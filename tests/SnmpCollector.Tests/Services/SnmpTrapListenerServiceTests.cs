using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Services;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Services;

/// <summary>
/// Unit tests for the ProcessDatagram internal method of <see cref="SnmpTrapListenerService"/>.
/// Verifies Simetra.{DeviceName} community string convention, auth failure counters,
/// successful routing, and correct VarbindEnvelope fields.
///
/// Placed in NonParallelMeterTests collection to prevent cross-test meter contamination
/// (MeterListener is a global listener; parallel tests with the same meter name interfere).
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class SnmpTrapListenerServiceTests : IDisposable
{
    private const string KnownDeviceName = "test-router";
    private const string KnownDeviceIp = "10.0.1.1";

    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;
    private readonly MeterListener _meterListener;
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public SnmpTrapListenerServiceTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _metrics = new PipelineMetricService(
            _sp.GetRequiredService<IMeterFactory>());

        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            _measurements.Add((instrument.Name, value, tags.ToArray()));
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _metrics.Dispose();
        _sp.Dispose();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Build a SNMPv2c trap byte buffer with one varbind.</summary>
    private static byte[] BuildTrapBytes(string community, string oid = "1.3.6.1.2.1.1.1.0")
    {
        var varbinds = new List<Variable>
        {
            new Variable(new ObjectIdentifier(oid), new Integer32(42))
        };
        var msg = new TrapV2Message(
            requestId: 1,
            VersionCode.V2,
            new OctetString(community),
            new ObjectIdentifier("1.3.6.1.6.3.1.1.5.1"),
            time: 0,
            varbinds);
        return msg.ToBytes();
    }

    private SnmpTrapListenerService CreateListener(ITrapChannel? trapChannel = null)
    {
        trapChannel ??= new NoOpTrapChannel();

        return new SnmpTrapListenerService(
            trapChannel,
            _metrics,
            Options.Create(new SnmpListenerOptions
            {
                BindAddress = "0.0.0.0",
                Port = 10162,
                Version = "v2c"
            }),
            NullLogger<SnmpTrapListenerService>.Instance);
    }

    private static UdpReceiveResult MakeResult(byte[] bytes, string fromIp)
        => new UdpReceiveResult(bytes, new IPEndPoint(IPAddress.Parse(fromIp), 12345));

    // -----------------------------------------------------------------------
    // 1. ValidCommunity_WritesVarbindEnvelopesToChannel
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidCommunity_WritesVarbindEnvelopesToChannel()
    {
        var trapChannel = new CapturingTrapChannel();
        var listener = CreateListener(trapChannel);

        var bytes = BuildTrapBytes($"Simetra.{KnownDeviceName}", "1.3.6.1.2.1.2.2.1.10.1");
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        Assert.Single(trapChannel.Written);
        Assert.Equal("1.3.6.1.2.1.2.2.1.10.1", trapChannel.Written[0].Oid);
        Assert.Equal(KnownDeviceName, trapChannel.Written[0].DeviceName);
    }

    // -----------------------------------------------------------------------
    // 2. VarbindEnvelope_HasCorrectFields
    // -----------------------------------------------------------------------

    [Fact]
    public void VarbindEnvelope_HasCorrectFields()
    {
        var trapChannel = new CapturingTrapChannel();
        var listener = CreateListener(trapChannel);

        var bytes = BuildTrapBytes($"Simetra.{KnownDeviceName}", "1.3.6.1.2.1.1.3.0");
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        Assert.Single(trapChannel.Written);
        var envelope = trapChannel.Written[0];

        Assert.Equal("1.3.6.1.2.1.1.3.0", envelope.Oid);
        Assert.Equal(KnownDeviceName, envelope.DeviceName);
        Assert.Equal(IPAddress.Parse(KnownDeviceIp).MapToIPv4(), envelope.AgentIp);
        Assert.Equal(SnmpType.Integer32, envelope.TypeCode);
    }

    // -----------------------------------------------------------------------
    // 3. InvalidCommunity_Public_DropsAndIncrementsAuthFailed
    // -----------------------------------------------------------------------

    [Fact]
    public void InvalidCommunity_Public_DropsAndIncrementsAuthFailed()
    {
        var trapChannel = new CapturingTrapChannel();
        var listener = CreateListener(trapChannel);

        var bytes = BuildTrapBytes("public");
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        var authFailedCount = _measurements.Count(m => m.InstrumentName == "snmp.trap.auth_failed");
        Assert.Equal(1, authFailedCount);
        Assert.Empty(trapChannel.Written);
    }

    // -----------------------------------------------------------------------
    // 4. EmptyCommunity_Drops
    // -----------------------------------------------------------------------

    [Fact]
    public void EmptyCommunity_DropsAndIncrementsAuthFailed()
    {
        var trapChannel = new CapturingTrapChannel();
        var listener = CreateListener(trapChannel);

        var bytes = BuildTrapBytes("");
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        var authFailedCount = _measurements.Count(m => m.InstrumentName == "snmp.trap.auth_failed");
        Assert.Equal(1, authFailedCount);
        Assert.Empty(trapChannel.Written);
    }

    // -----------------------------------------------------------------------
    // 5. CommunityWithNothingAfterDot_Drops
    // -----------------------------------------------------------------------

    [Fact]
    public void CommunityWithNothingAfterDot_DropsAndIncrementsAuthFailed()
    {
        var trapChannel = new CapturingTrapChannel();
        var listener = CreateListener(trapChannel);

        // "Simetra." with no device name after the dot
        var bytes = BuildTrapBytes("Simetra.");
        listener.ProcessDatagram(MakeResult(bytes, KnownDeviceIp));

        var authFailedCount = _measurements.Count(m => m.InstrumentName == "snmp.trap.auth_failed");
        Assert.Equal(1, authFailedCount);
        Assert.Empty(trapChannel.Written);
    }

    // -----------------------------------------------------------------------
    // 6. MalformedPacket_DoesNotThrow_AndDropsQuietly
    // -----------------------------------------------------------------------

    [Fact]
    public void MalformedPacket_DoesNotThrow_AndDropsQuietly()
    {
        var listener = CreateListener();

        // Random garbage bytes that are not valid SNMP
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE };
        var result = MakeResult(garbage, KnownDeviceIp);

        // Should not throw
        var ex = Record.Exception(() => listener.ProcessDatagram(result));
        Assert.Null(ex);

        // No trap counter measurements should be recorded
        Assert.DoesNotContain(_measurements, m =>
            m.InstrumentName is "snmp.trap.auth_failed" or "snmp.trap.unknown_device");
    }

    // -----------------------------------------------------------------------
    // 7. DeviceNameExtractedFromCommunityString
    // -----------------------------------------------------------------------

    [Fact]
    public void DeviceNameExtractedFromCommunityString()
    {
        var trapChannel = new CapturingTrapChannel();
        var listener = CreateListener(trapChannel);

        var bytes = BuildTrapBytes("Simetra.npb-core-01");
        listener.ProcessDatagram(MakeResult(bytes, "192.168.1.1"));

        Assert.Single(trapChannel.Written);
        Assert.Equal("npb-core-01", trapChannel.Written[0].DeviceName);
    }

    // -----------------------------------------------------------------------
    // Stubs
    // -----------------------------------------------------------------------

    /// <summary>Trap channel that captures written envelopes for assertion.</summary>
    private sealed class CapturingTrapChannel : ITrapChannel
    {
        private readonly Channel<VarbindEnvelope> _channel = Channel.CreateUnbounded<VarbindEnvelope>();
        public List<VarbindEnvelope> Written { get; } = new();

        public ChannelWriter<VarbindEnvelope> Writer => new CapturingWriter(Written, _channel.Writer);
        public ChannelReader<VarbindEnvelope> Reader => _channel.Reader;
        public void Complete() => _channel.Writer.TryComplete();
        public Task WaitForDrainAsync(CancellationToken cancellationToken) => _channel.Reader.Completion.WaitAsync(cancellationToken);

        private sealed class CapturingWriter : ChannelWriter<VarbindEnvelope>
        {
            private readonly List<VarbindEnvelope> _list;
            private readonly ChannelWriter<VarbindEnvelope> _inner;

            public CapturingWriter(List<VarbindEnvelope> list, ChannelWriter<VarbindEnvelope> inner)
            {
                _list = list;
                _inner = inner;
            }

            public override bool TryWrite(VarbindEnvelope item)
            {
                _list.Add(item);
                return _inner.TryWrite(item);
            }

            public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
                => _inner.WaitToWriteAsync(cancellationToken);
        }
    }

    /// <summary>Trap channel that silently discards all writes (for tests that only check metrics).</summary>
    private sealed class NoOpTrapChannel : ITrapChannel
    {
        private readonly Channel<VarbindEnvelope> _channel = Channel.CreateUnbounded<VarbindEnvelope>();

        public ChannelWriter<VarbindEnvelope> Writer => new NoOpWriter();
        public ChannelReader<VarbindEnvelope> Reader => _channel.Reader;
        public void Complete() => _channel.Writer.TryComplete();
        public Task WaitForDrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class NoOpWriter : ChannelWriter<VarbindEnvelope>
        {
            public override bool TryWrite(VarbindEnvelope item) => true;
            public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
                => ValueTask.FromResult(true);
        }
    }
}
