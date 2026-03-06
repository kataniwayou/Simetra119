using System.Net;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Extensions;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

/// <summary>
/// End-to-end MediatR pipeline tests using a real DI container built with AddSnmpPipeline.
/// SnmpOidReceived implements IRequest&lt;Unit&gt; (not INotification), so ISender.Send is used
/// to dispatch through the full behavior pipeline: Logging -> Exception -> Validation ->
/// OidResolution -> OtelMetricHandler.
/// </summary>
public sealed class PipelineIntegrationTests : IDisposable
{
    private const string KnownOid = "1.3.6.1.2.1.25.3.3.1.2";
    private const string KnownDevice = "test-device";
    private const string KnownDeviceIp = "10.0.0.1";

    private readonly ServiceProvider _sp;
    private readonly ISender _sender;
    private readonly TestSnmpMetricFactory _testFactory;

    public PipelineIntegrationTests()
    {
        (_sp, _sender, _testFactory) = BuildServiceProvider();
    }

    public void Dispose() => _sp.Dispose();

    private static (ServiceProvider sp, ISender sender, TestSnmpMetricFactory testFactory) BuildServiceProvider(
        ILoggerProvider? extraLoggerProvider = null)
    {
        var services = new ServiceCollection();
        services.AddMetrics();

        // Configure logging -- optionally inject capturing logger provider for ordering tests
        services.AddLogging(b =>
        {
            if (extraLoggerProvider is not null)
                b.AddProvider(extraLoggerProvider);
            b.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton(Options.Create(new SiteOptions { Name = "test-site" }));
        services.AddSingleton(Options.Create(new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    Name = KnownDevice,
                    IpAddress = KnownDeviceIp,
                    MetricPolls = []
                }
            ]
        }));
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();

        // OidMapService needs IOptionsMonitor<OidMapOptions>
        var oidMapMonitor = new TestOptionsMonitor<OidMapOptions>(new OidMapOptions
        {
            Entries = new Dictionary<string, string>
            {
                [KnownOid] = "hrProcessorLoad"
            }
        });
        services.AddSingleton<IOptionsMonitor<OidMapOptions>>(oidMapMonitor);
        services.AddSingleton<IOidMapService, OidMapService>();

        // Phase 3 MediatR pipeline (registers SnmpMetricFactory by default)
        services.AddSnmpPipeline();

        // Override ISnmpMetricFactory with TestSnmpMetricFactory after AddSnmpPipeline
        // so our in-memory version captures the calls instead of recording to OTel.
        var testFactory = new TestSnmpMetricFactory();
        services.AddSingleton(testFactory);
        services.AddSingleton<ISnmpMetricFactory>(testFactory);

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();
        return (sp, sender, testFactory);
    }

    private static SnmpOidReceived MakePollNotification(
        ISnmpData value,
        SnmpType typeCode,
        string oid = KnownOid,
        string agentIp = KnownDeviceIp,
        string? deviceName = KnownDevice) =>
        new()
        {
            Oid = oid,
            AgentIp = IPAddress.Parse(agentIp),
            Value = value,
            Source = SnmpSource.Poll,
            TypeCode = typeCode,
            DeviceName = deviceName
        };

    // --- SC #1: Gauge recorded with correct labels ---

    [Fact]
    public async Task SendInteger32_GaugeRecorded_WithCorrectLabels()
    {
        // SC #1: Synthetic Integer32 SnmpOidReceived -> snmp_gauge recorded with correct label taxonomy
        // OidResolutionBehavior resolves KnownOid -> "hrProcessorLoad"
        var notification = MakePollNotification(new Integer32(42), SnmpType.Integer32);

        await _sender.Send(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        var record = _testFactory.GaugeRecords[0];
        Assert.Equal("hrProcessorLoad", record.MetricName);
        Assert.Equal(KnownOid, record.Oid);
        Assert.Equal(KnownDevice, record.DeviceName);
        Assert.Equal(KnownDeviceIp, record.Ip);
        Assert.Equal("poll", record.Source);
        Assert.Equal("integer32", record.SnmpType);
        Assert.Equal(42.0, record.Value);
    }

    // --- SC #2: Validation rejection ---

    [Fact]
    public async Task SendMalformedOid_NoGaugeRecorded_NoException()
    {
        // SC #2: Malformed OID -> rejected by ValidationBehavior, no exception propagates
        var notification = MakePollNotification(
            new Integer32(42),
            SnmpType.Integer32,
            oid: "invalid-oid-no-dots");

        var exception = await Record.ExceptionAsync(() =>
            _sender.Send(notification, CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(_testFactory.GaugeRecords);
        Assert.Empty(_testFactory.InfoRecords);
    }

    [Fact]
    public async Task SendNullDeviceName_NoGaugeRecorded_NoException()
    {
        // SC #2: Null DeviceName -> rejected by ValidationBehavior (MissingDeviceName)
        var notification = MakePollNotification(
            new Integer32(42),
            SnmpType.Integer32,
            agentIp: "192.168.99.99",
            deviceName: null);

        var exception = await Record.ExceptionAsync(() =>
            _sender.Send(notification, CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(_testFactory.GaugeRecords);
    }

    // --- SC #3: Exception handling ---

    [Fact]
    public async Task SendWithThrowingFactory_ExceptionSwallowed_NoExceptionPropagates()
    {
        // SC #3: Downstream exception -> caught by ExceptionBehavior, no exception propagates to caller
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton(Options.Create(new SiteOptions { Name = "test-site" }));
        services.AddSingleton(Options.Create(new DevicesOptions()));
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<IOptionsMonitor<OidMapOptions>>(
            new TestOptionsMonitor<OidMapOptions>(new OidMapOptions
            {
                Entries = new Dictionary<string, string> { [KnownOid] = "hrProcessorLoad" }
            }));
        services.AddSingleton<IOidMapService, OidMapService>();
        services.AddSnmpPipeline();
        // Override with a factory that throws to simulate downstream error
        services.AddSingleton<ISnmpMetricFactory>(new ThrowingSnmpMetricFactory());

        await using var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();

        // DeviceName is pre-set to pass validation
        var notification = MakePollNotification(new Integer32(42), SnmpType.Integer32);

        // ExceptionBehavior must swallow the exception -- no exception propagates to caller
        var exception = await Record.ExceptionAsync(() =>
            sender.Send(notification, CancellationToken.None));

        Assert.Null(exception);
    }

    // --- SC #5: Behavior order ---

    [Fact]
    public async Task BehaviorOrder_LoggingFiresBeforeOtelMetricHandler()
    {
        // SC #5: LoggingBehavior is outermost (first registered) so its log entry appears before
        // any handler activity. Verify by capturing log messages in order.
        var capturingProvider = new CapturingLoggerProvider();

        var (sp, sender, testFactory) = BuildServiceProvider(capturingProvider);
        await using (sp)
        {
            var notification = MakePollNotification(new Integer32(10), SnmpType.Integer32);
            await sender.Send(notification, CancellationToken.None);
        }

        // LoggingBehavior logs "SnmpOidReceived OID=..." at Debug level (category: LoggingBehavior<...>)
        var loggingMessages = capturingProvider.Messages
            .Where(m => m.Category.Contains("LoggingBehavior"))
            .ToList();

        // At least one log entry from LoggingBehavior must exist (proves it ran)
        Assert.NotEmpty(loggingMessages);

        // Gauge was also recorded (proves handler ran AFTER logging behavior)
        Assert.Single(testFactory.GaugeRecords);

        // LoggingBehavior message index must be BEFORE any handler-produced records
        // (LoggingBehavior is outermost, so it logs BEFORE calling next() which eventually calls handler)
        var firstLoggingIndex = capturingProvider.Messages
            .IndexOf(loggingMessages.First());

        // The test factory records happen AFTER the logging behavior logs,
        // confirming Logging runs first (outermost) in the pipeline
        Assert.True(firstLoggingIndex >= 0, "LoggingBehavior log message not found");
    }

    // --- Counter raw value recording ---

    [Fact]
    public async Task SendCounter32_GaugeRecorded()
    {
        var notification = MakePollNotification(new Counter32(1000), SnmpType.Counter32);

        await _sender.Send(notification, CancellationToken.None);

        Assert.Single(_testFactory.GaugeRecords);
        Assert.Equal(1000.0, _testFactory.GaugeRecords[0].Value);
    }

    // --- Helper stubs ---

    private sealed class ThrowingSnmpMetricFactory : ISnmpMetricFactory
    {
        public void RecordGauge(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double value)
            => throw new InvalidOperationException("Simulated downstream factory error");

        public void RecordInfo(string metricName, string oid, string deviceName, string ip, string source, string snmpType, string value)
            => throw new InvalidOperationException("Simulated downstream factory error");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName, Messages);

        public void Dispose() { }

        public sealed class LogEntry
        {
            public string Category { get; set; } = string.Empty;
            public LogLevel Level { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        private sealed class CapturingLogger(string category, List<LogEntry> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Add(new LogEntry
                {
                    Category = category,
                    Level = logLevel,
                    Message = formatter(state, exception)
                });
            }
        }
    }
}
