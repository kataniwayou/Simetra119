using System.Diagnostics.Metrics;
using System.Net;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Pipeline.Behaviors;

/// <summary>
/// Unit tests for <see cref="TimingBehavior{TRequest,TResponse}"/> verifying that
/// snmp.pipeline.duration is recorded for SnmpOidReceived messages with correct tags,
/// and not recorded for non-SnmpOidReceived requests.
///
/// Placed in NonParallelMeterTests collection because MeterListener is a global listener;
/// parallel test classes using the same meter name cause cross-test measurement contamination.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class TimingBehaviorTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;
    private readonly MeterListener _meterListener;
    private readonly List<(string InstrumentName, double Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public TimingBehaviorTests()
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
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
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

    // -------------------------------------------------------------------------
    // Test 1: SnmpOidReceived records pipeline duration with device_name tag
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_SnmpOidReceived_RecordsPipelineDuration()
    {
        // Arrange
        var behavior = new TimingBehavior<SnmpOidReceived, Unit>(_metrics);
        var request = new SnmpOidReceived
        {
            Oid = "1.3.6.1.2.1.1.1.0",
            AgentIp = IPAddress.Loopback,
            DeviceName = "test-device",
            Value = new Integer32(42),
            Source = SnmpSource.Poll,
            TypeCode = SnmpType.Integer32
        };

        // Act
        await behavior.Handle(
            request,
            async ct =>
            {
                await Task.Delay(5, ct);
                return Unit.Value;
            },
            CancellationToken.None);

        // Assert
        _meterListener.RecordObservableInstruments();
        var duration = _measurements
            .Where(m => m.InstrumentName == "snmp.pipeline.duration")
            .ToList();

        Assert.Single(duration);
        Assert.True(duration[0].Value > 0, "Duration should be greater than 0");
        var deviceTag = duration[0].Tags.FirstOrDefault(t => t.Key == "device_name");
        Assert.Equal("test-device", deviceTag.Value);
    }

    // -------------------------------------------------------------------------
    // Test 2: Non-SnmpOidReceived request does not record duration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_NonSnmpOidReceived_DoesNotRecordDuration()
    {
        // Arrange
        var behavior = new TimingBehavior<DummyRequest, Unit>(_metrics);
        var request = new DummyRequest();

        // Act
        await behavior.Handle(
            request,
            ct => Task.FromResult(Unit.Value),
            CancellationToken.None);

        // Assert
        _meterListener.RecordObservableInstruments();
        var duration = _measurements
            .Where(m => m.InstrumentName == "snmp.pipeline.duration")
            .ToList();

        Assert.Empty(duration);
    }

    // -------------------------------------------------------------------------
    // Test 3: Null DeviceName records with "unknown"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_SnmpOidReceived_NullDeviceName_RecordsWithUnknown()
    {
        // Arrange
        var behavior = new TimingBehavior<SnmpOidReceived, Unit>(_metrics);
        var request = new SnmpOidReceived
        {
            Oid = "1.3.6.1.2.1.1.1.0",
            AgentIp = IPAddress.Loopback,
            DeviceName = null,
            Value = new Integer32(42),
            Source = SnmpSource.Trap,
            TypeCode = SnmpType.Integer32
        };

        // Act
        await behavior.Handle(
            request,
            ct => Task.FromResult(Unit.Value),
            CancellationToken.None);

        // Assert
        _meterListener.RecordObservableInstruments();
        var duration = _measurements
            .Where(m => m.InstrumentName == "snmp.pipeline.duration")
            .ToList();

        Assert.Single(duration);
        var deviceTag = duration[0].Tags.FirstOrDefault(t => t.Key == "device_name");
        Assert.Equal("unknown", deviceTag.Value);
    }

    // -------------------------------------------------------------------------
    // Dummy request for non-SnmpOidReceived test
    // -------------------------------------------------------------------------

    private sealed class DummyRequest : IRequest<Unit> { }
}
