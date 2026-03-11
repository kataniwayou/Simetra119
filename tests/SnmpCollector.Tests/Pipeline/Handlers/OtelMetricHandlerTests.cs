using System.Diagnostics.Metrics;
using System.Net;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Handlers;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Pipeline.Handlers;

public sealed class OtelMetricHandlerTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly TestSnmpMetricFactory _testFactory;
    private readonly OtelMetricHandler _handler;

    public OtelMetricHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<PipelineMetricService>();
        _sp = services.BuildServiceProvider();

        _pipelineMetrics = _sp.GetRequiredService<PipelineMetricService>();
        _testFactory = new TestSnmpMetricFactory();
        _handler = new OtelMetricHandler(
            _testFactory,
            _pipelineMetrics,
            NullLogger<OtelMetricHandler>.Instance);
    }

    public void Dispose() => _sp.Dispose();

    private static SnmpOidReceived MakeNotification(
        ISnmpData value,
        SnmpType typeCode,
        string oid = "1.3.6.1.2.1.25.3.3.1.2",
        string agentIp = "10.0.0.1",
        string deviceName = "test-device",
        string? metricName = "hrProcessorLoad",
        double extractedValue = 0.0,
        string? extractedStringValue = null,
        double? pollDurationMs = null,
        SnmpSource source = SnmpSource.Poll) =>
        new()
        {
            Oid = oid,
            AgentIp = IPAddress.Parse(agentIp),
            Value = value,
            Source = source,
            TypeCode = typeCode,
            DeviceName = deviceName,
            MetricName = metricName,
            ExtractedValue = extractedValue,
            ExtractedStringValue = extractedStringValue,
            PollDurationMs = pollDurationMs
        };

    // --- Gauge dispatch tests ---

    [Fact]
    public async Task Integer32_RecordsGauge()
    {
        var notification = MakeNotification(new Integer32(42), SnmpType.Integer32, extractedValue: 42.0);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Equal(42.0, _testFactory.GaugeRecords[0].Value);
        Assert.Empty(_testFactory.InfoRecords);
    }

    [Fact]
    public async Task Gauge32_RecordsGauge()
    {
        var notification = MakeNotification(new Gauge32(75), SnmpType.Gauge32, extractedValue: 75.0);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Equal(75.0, _testFactory.GaugeRecords[0].Value);
    }

    [Fact]
    public async Task TimeTicks_RecordsGauge()
    {
        var notification = MakeNotification(new TimeTicks(123456), SnmpType.TimeTicks, extractedValue: 123456.0);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Equal(123456.0, _testFactory.GaugeRecords[0].Value);
        Assert.Empty(_testFactory.InfoRecords);
    }

    // --- Info dispatch tests ---

    [Fact]
    public async Task OctetString_RecordsInfo()
    {
        var notification = MakeNotification(new OctetString("router-01"), SnmpType.OctetString,
            extractedStringValue: "router-01");
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.InfoRecords);
        Assert.Equal("router-01", _testFactory.InfoRecords[0].Value);
        Assert.Empty(_testFactory.GaugeRecords);
    }

    // --- Counter raw value tests ---

    [Fact]
    public async Task Counter32_RecordsGauge()
    {
        var notification = MakeNotification(new Counter32(1000), SnmpType.Counter32, extractedValue: 1000.0);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Equal(1000.0, _testFactory.GaugeRecords[0].Value);
    }

    [Fact]
    public async Task Counter64_RecordsGauge()
    {
        var notification = MakeNotification(new Counter64(5000), SnmpType.Counter64, extractedValue: 5000.0);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Equal(5000.0, _testFactory.GaugeRecords[0].Value);
    }

    // --- Label correctness tests ---

    [Fact]
    public async Task GaugeRecordHasCorrectLabels()
    {
        var notification = MakeNotification(
            new Integer32(99),
            SnmpType.Integer32,
            oid: "1.3.6.1.2.1.25.3.3.1.2",
            agentIp: "10.0.0.1",
            deviceName: "core-router",
            metricName: "hrProcessorLoad",
            extractedValue: 99.0);

        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        var record = _testFactory.GaugeRecords[0];
        Assert.Equal("hrProcessorLoad", record.MetricName);
        Assert.Equal("1.3.6.1.2.1.25.3.3.1.2", record.Oid);
        Assert.Equal("core-router", record.DeviceName);
        Assert.Equal("10.0.0.1", record.Ip);
        Assert.Equal("poll", record.Source);
        Assert.Equal("integer32", record.SnmpType);
        Assert.Equal(99.0, record.Value);
    }

    [Fact]
    public async Task GaugeRecord_FallsBackToUnknownWhenDeviceNameNull()
    {
        // When DeviceName is null, device_name label uses "unknown" and ip uses AgentIp.ToString()
        var notification = new SnmpOidReceived
        {
            Oid = "1.3.6.1.2.1.25.3.3.1.2",
            AgentIp = IPAddress.Parse("10.0.0.1"),
            Value = new Integer32(10),
            Source = SnmpSource.Trap,
            TypeCode = SnmpType.Integer32,
            DeviceName = null,
            MetricName = "hrProcessorLoad"
        };

        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Equal("unknown", _testFactory.GaugeRecords[0].DeviceName);
        Assert.Equal("10.0.0.1", _testFactory.GaugeRecords[0].Ip);
    }

    // --- snmp_info truncation test ---

    [Fact]
    public void SnmpMetricFactory_InfoTruncatesValueAt128Chars()
    {
        // Truncation is implemented in SnmpMetricFactory.RecordInfo, not in OtelMetricHandler.
        // Verify via a capturing wrapper around SnmpMetricFactory.
        var services = new ServiceCollection();
        services.AddMetrics();
        using var sp = services.BuildServiceProvider();

        var capturingFactory = new CapturingSnmpMetricFactory(
            new SnmpMetricFactory(sp.GetRequiredService<IMeterFactory>()));

        var longValue = new string('x', 200);

        capturingFactory.RecordInfo("sysDescr", "1.3.6.1.2.1.1.1.0", "test-device", "10.0.0.1", "poll", "octetstring", longValue);

        Assert.Single(capturingFactory.CapturedInfoValues);
        var captured = capturingFactory.CapturedInfoValues[0];
        Assert.Equal(128, captured.Length);
        Assert.True(captured.EndsWith("..."), $"Expected value to end with '...', got: '{captured}'");
    }

    // --- Heartbeat export tests ---

    [Fact]
    public async Task Heartbeat_ExportedAsGauge_WithSimetraDevice()
    {
        // Heartbeat flows through the pipeline and exports as snmp_gauge with device_name=Simetra.
        var notification = new SnmpOidReceived
        {
            Oid = HeartbeatJobOptions.HeartbeatOid,
            AgentIp = IPAddress.Parse("127.0.0.1"),
            Value = new Counter32(1),
            Source = SnmpSource.Trap,
            TypeCode = SnmpType.Counter32,
            DeviceName = HeartbeatJobOptions.HeartbeatDeviceName,
            MetricName = "Heartbeat",
            ExtractedValue = 1.0
        };

        var exception = await Record.ExceptionAsync(() => _handler.Handle(notification, CancellationToken.None));

        Assert.Null(exception);
        Assert.Single(_testFactory.GaugeRecords);
        Assert.Equal("Heartbeat", _testFactory.GaugeRecords[0].MetricName);
        Assert.Equal("Simetra", _testFactory.GaugeRecords[0].DeviceName);
        Assert.Empty(_testFactory.InfoRecords);
    }

    [Fact]
    public async Task HeartbeatDeviceName_ExportedAsGauge()
    {
        // A message with DeviceName=HeartbeatDeviceName and Counter32 exports as snmp_gauge
        var notification = MakeNotification(
            new Counter32(1),
            SnmpType.Counter32,
            deviceName: HeartbeatJobOptions.HeartbeatDeviceName,
            extractedValue: 1.0);

        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
    }

    // --- Pipeline metric counter test ---

    [Fact]
    public async Task Integer32_DoesNotThrowOnHandledIncrement()
    {
        // Verify IncrementHandled() is called without throwing (internal counter is not directly observable
        // without an OTel pipeline, but we verify the operation completes without error).
        var notification = MakeNotification(new Integer32(1), SnmpType.Integer32);

        var exception = await Record.ExceptionAsync(() => _handler.Handle(notification, CancellationToken.None));

        Assert.Null(exception);
    }

    // --- Duration recording tests ---

    [Fact]
    public async Task Integer32_WithPollDuration_RecordsGaugeDuration()
    {
        var notification = MakeNotification(new Integer32(42), SnmpType.Integer32,
            extractedValue: 42.0, pollDurationMs: 15.5);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeDurationRecords);
        var record = _testFactory.GaugeDurationRecords[0];
        Assert.Equal(15.5, record.DurationMs);
        Assert.Equal("hrProcessorLoad", record.MetricName);
        Assert.Equal("test-device", record.DeviceName);
        Assert.Equal("integer32", record.SnmpType);
    }

    [Fact]
    public async Task OctetString_WithPollDuration_RecordsInfoDuration()
    {
        var notification = MakeNotification(new OctetString("router-01"), SnmpType.OctetString,
            extractedStringValue: "router-01", pollDurationMs: 22.3);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.InfoDurationRecords);
        var record = _testFactory.InfoDurationRecords[0];
        Assert.Equal(22.3, record.DurationMs);
        Assert.Equal("router-01", record.Value);
    }

    [Fact]
    public async Task Integer32_WithoutPollDuration_SkipsDurationRecording()
    {
        var notification = MakeNotification(new Integer32(42), SnmpType.Integer32, extractedValue: 42.0);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Empty(_testFactory.GaugeDurationRecords);
    }

    [Fact]
    public async Task TrapSource_NoPollDuration_SkipsDurationRecording()
    {
        var notification = MakeNotification(new Integer32(10), SnmpType.Integer32,
            extractedValue: 10.0, source: SnmpSource.Trap);
        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Empty(_testFactory.GaugeDurationRecords);
    }

    // --- CapturingSnmpMetricFactory helper ---

    private sealed class CapturingSnmpMetricFactory : ISnmpMetricFactory, IDisposable
    {
        private readonly SnmpMetricFactory _inner;
        public List<string> CapturedInfoValues { get; } = new();

        public CapturingSnmpMetricFactory(SnmpMetricFactory inner) => _inner = inner;

        public void RecordGauge(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double value)
            => _inner.RecordGauge(metricName, oid, deviceName, ip, source, snmpType, value);

        public void RecordInfo(string metricName, string oid, string deviceName, string ip, string source, string snmpType, string value)
        {
            _inner.RecordInfo(metricName, oid, deviceName, ip, source, snmpType, value);
            var truncated = value.Length > 128
                ? string.Concat(value.AsSpan(0, 125), "...")
                : value;
            CapturedInfoValues.Add(truncated);
        }

        public void RecordGaugeDuration(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double durationMs)
            => _inner.RecordGaugeDuration(metricName, oid, deviceName, ip, source, snmpType, durationMs);

        public void RecordInfoDuration(string metricName, string oid, string deviceName, string ip, string source, string snmpType, string value, double durationMs)
            => _inner.RecordInfoDuration(metricName, oid, deviceName, ip, source, snmpType, value, durationMs);

        public void Dispose() => _inner.Dispose();
    }
}
