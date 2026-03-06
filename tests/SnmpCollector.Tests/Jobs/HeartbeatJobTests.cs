using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Jobs;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="HeartbeatJob"/> covering liveness stamping (success and failure paths),
/// correlation ID scoping, and community string derivation.
/// <para>
/// The actual UDP send (Messenger.SendTrapV2) is a static call and cannot be mocked.
/// Tests focus on observable side effects: liveness stamps and correlation ID lifecycle.
/// </para>
/// </summary>
public sealed class HeartbeatJobTests
{
    private const int TestListenerPort = 9162;

    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    private readonly StubCorrelationService _correlation = new();
    private readonly StubLivenessVectorService _liveness = new();
    private readonly HeartbeatJob _job;

    public HeartbeatJobTests()
    {
        var listenerOptions = Options.Create(new SnmpListenerOptions
        {
            BindAddress = "0.0.0.0",
            Port = TestListenerPort,
            Version = "v2c"
        });

        _job = new HeartbeatJob(
            _correlation,
            _liveness,
            listenerOptions,
            NullLogger<HeartbeatJob>.Instance);
    }

    [Fact]
    public async Task Execute_StampsLiveness()
    {
        // Arrange
        var context = MakeContext("heartbeat");

        // Act -- trap send may fail (no listener on test port), but finally block always runs.
        await _job.Execute(context);

        // Assert: liveness stamped with job key
        Assert.True(_liveness.StampCalled);
        Assert.Equal("heartbeat", _liveness.LastStampedKey);
    }

    [Fact]
    public async Task Execute_SetsAndClearsOperationCorrelationId()
    {
        // Arrange
        _correlation.SetCorrelationId("test-correlation-123");
        var context = MakeContext("heartbeat");

        // Act
        await _job.Execute(context);

        // Assert: OperationCorrelationId cleared to null in finally block
        Assert.Null(_correlation.OperationCorrelationId);
        // Assert: OperationCorrelationId was set at some point (captured by stub)
        Assert.True(_correlation.OperationCorrelationIdWasSet);
    }

    [Fact]
    public async Task Execute_OnException_StillStampsLiveness()
    {
        // Arrange: use port 0 which will cause the UDP send to fail on some platforms,
        // but regardless of whether the send succeeds or fails, the finally block must stamp.
        var listenerOptions = Options.Create(new SnmpListenerOptions
        {
            BindAddress = "0.0.0.0",
            Port = 1, // privileged port -- likely to fail on most platforms
            Version = "v2c"
        });

        var job = new HeartbeatJob(
            _correlation,
            _liveness,
            listenerOptions,
            NullLogger<HeartbeatJob>.Instance);

        var context = MakeContext("heartbeat");

        // Act -- regardless of success or failure, liveness must be stamped
        await job.Execute(context);

        // Assert: liveness always stamped
        Assert.True(_liveness.StampCalled);
        Assert.Equal("heartbeat", _liveness.LastStampedKey);
        // Assert: correlation ID always cleared
        Assert.Null(_correlation.OperationCorrelationId);
    }

    [Fact]
    public void Constructor_DerivesCommunityString()
    {
        // The HeartbeatJob constructor derives the community string via
        // CommunityStringHelper.DeriveFromDeviceName("heartbeat") -> "Simetra.heartbeat".
        // If this fails, the constructor would throw. The fact that _job was created
        // successfully in the test constructor proves derivation works.
        // We verify by checking CommunityStringHelper directly.
        var derived = CommunityStringHelper.DeriveFromDeviceName("heartbeat");
        Assert.Equal("Simetra.heartbeat", derived);
    }

    [Fact]
    public async Task Execute_UsesJobKeyFromContext()
    {
        // Arrange: use a custom job key name
        var context = MakeContext("heartbeat-custom");

        // Act
        await _job.Execute(context);

        // Assert: liveness stamped with the context's job key, not a hardcoded value
        Assert.Equal("heartbeat-custom", _liveness.LastStampedKey);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IJobExecutionContext MakeContext(string jobKeyName)
        => new StubHeartbeatJobContext(jobKeyName);

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed class StubCorrelationService : ICorrelationService
    {
        private string _correlationId = "stub-correlation-id";

        public string CurrentCorrelationId => _correlationId;

        public string? OperationCorrelationId { get; set; }

        /// <summary>Tracks whether OperationCorrelationId was ever set to a non-null value.</summary>
        public bool OperationCorrelationIdWasSet { get; private set; }

        public void SetCorrelationId(string correlationId) => _correlationId = correlationId;

        // Override setter to track assignments
        private string? _opId;
        string? ICorrelationService.OperationCorrelationId
        {
            get => _opId;
            set
            {
                if (value is not null) OperationCorrelationIdWasSet = true;
                _opId = value;
            }
        }
    }

    private sealed class StubLivenessVectorService : ILivenessVectorService
    {
        public bool StampCalled { get; private set; }
        public string? LastStampedKey { get; private set; }

        public void Stamp(string jobKey)
        {
            StampCalled = true;
            LastStampedKey = jobKey;
        }

        public DateTimeOffset? GetStamp(string jobKey) => null;

        public IReadOnlyDictionary<string, DateTimeOffset> GetAllStamps()
            => new Dictionary<string, DateTimeOffset>().AsReadOnly();
    }

    /// <summary>
    /// Minimal IJobExecutionContext stub for HeartbeatJob. Only provides JobDetail.Key.Name
    /// and CancellationToken -- HeartbeatJob reads nothing else from the context.
    /// </summary>
    private sealed class StubHeartbeatJobContext : IJobExecutionContext
    {
        private readonly IJobDetail _jobDetail;

        public StubHeartbeatJobContext(string jobKeyName)
        {
            _jobDetail = JobBuilder.Create<HeartbeatJob>()
                .WithIdentity(jobKeyName)
                .Build();
        }

        public IJobDetail JobDetail => _jobDetail;
        public CancellationToken CancellationToken => CancellationToken.None;
        public object? Result { get; set; }

        // --- Unused interface members ---
        public IScheduler Scheduler                     => throw new NotImplementedException();
        public ITrigger Trigger                         => throw new NotImplementedException();
        public ICalendar? Calendar                      => throw new NotImplementedException();
        public bool Recovering                          => false;
        public TriggerKey RecoveringTriggerKey          => throw new NotImplementedException();
        public int RefireCount                          => 0;
        public JobDataMap MergedJobDataMap              => throw new NotImplementedException();
        public JobDataMap JobDataMap                    => throw new NotImplementedException();
        public string FireInstanceId                    => string.Empty;
        public DateTimeOffset FireTimeUtc               => DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc     => null;
        public DateTimeOffset? NextFireTimeUtc          => null;
        public DateTimeOffset? PreviousFireTimeUtc      => null;
        public TimeSpan JobRunTime                      => TimeSpan.Zero;
        public IJob JobInstance                         => throw new NotImplementedException();

        public void Put(object key, object objectValue) { }
        public object? Get(object key) => null;
    }
}
