using System.Net;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Jobs;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using Xunit;

namespace SnmpCollector.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="PreferredHeartbeatJob"/> covering all lease-read scenarios:
/// fresh lease, stale lease, exact threshold, 404, transient errors, null timestamps,
/// acquireTime fallback, and liveness stamping.
/// <para>
/// SC-4: IPreferredStampReader.IsPreferredStampFresh returns a real derived value, not a stub —
/// verified here with a mocked K8s lease response.
/// </para>
/// <para>
/// Mocking strategy: <see cref="ICoordinationV1Operations.ReadNamespacedLeaseWithHttpMessagesAsync"/>
/// is the underlying interface method called by the <c>ReadNamespacedLeaseAsync</c> extension method.
/// NSubstitute mocks the interface method; the extension delegates to it automatically.
/// </para>
/// </summary>
public sealed class PreferredHeartbeatJobTests
{
    private const int DurationSeconds = 15;
    private const string LeaseName = "snmp-collector-leader";
    private const string Namespace = "default";
    private const string JobKeyName = "preferred-heartbeat";

    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    private readonly ICoordinationV1Operations _mockCoordV1 = Substitute.For<ICoordinationV1Operations>();
    private readonly IKubernetes _mockKubeClient = Substitute.For<IKubernetes>();
    private readonly ILivenessVectorService _mockLiveness = Substitute.For<ILivenessVectorService>();
    private readonly PreferredLeaderService _preferredLeaderService;
    private readonly PreferredHeartbeatJob _job;

    public PreferredHeartbeatJobTests()
    {
        _mockKubeClient.CoordinationV1.Returns(_mockCoordV1);

        var leaseOptions = Options.Create(new LeaseOptions
        {
            Name = LeaseName,
            Namespace = Namespace,
            DurationSeconds = DurationSeconds
        });

        _preferredLeaderService = new PreferredLeaderService(
            leaseOptions,
            NullLogger<PreferredLeaderService>.Instance);

        _job = new PreferredHeartbeatJob(
            _mockKubeClient,
            _preferredLeaderService,
            leaseOptions,
            _mockLiveness,
            NullLogger<PreferredHeartbeatJob>.Instance);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IJobExecutionContext MakeContext(string jobKeyName = JobKeyName)
        => new StubJobContext(jobKeyName);

    /// <summary>
    /// Sets up the mock to return the given lease via the underlying WithHttpMessages method.
    /// The ReadNamespacedLeaseAsync extension delegates to ReadNamespacedLeaseWithHttpMessagesAsync.
    /// </summary>
    private void SetupLeaseResponse(V1Lease lease)
    {
        var response = new HttpOperationResponse<V1Lease>
        {
            Body = lease,
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };

        _mockCoordV1
            .ReadNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
    }

    /// <summary>
    /// Sets up the mock to throw the given exception.
    /// </summary>
    private void SetupLeaseThrows(Exception ex)
    {
        _mockCoordV1
            .ReadNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);
    }

    private static V1Lease FreshLease(DateTime? renewTime = null, DateTime? acquireTime = null) =>
        new()
        {
            Spec = new V1LeaseSpec
            {
                RenewTime = renewTime ?? DateTime.UtcNow,
                AcquireTime = acquireTime ?? DateTime.UtcNow.AddMinutes(-5)
            }
        };

    private static HttpOperationException Make404Exception()
    {
        var response = new HttpResponseMessageWrapper(
            new HttpResponseMessage(HttpStatusCode.NotFound), "");
        return new HttpOperationException("Not Found") { Response = response };
    }

    // -------------------------------------------------------------------------
    // 1. Execute_WithFreshLease_SetsStampFreshTrue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithFreshLease_SetsStampFreshTrue()
    {
        // Arrange: lease renewed just now — well within DurationSeconds+5s threshold
        SetupLeaseResponse(FreshLease(renewTime: DateTime.UtcNow));
        var context = MakeContext();

        // Act
        await _job.Execute(context);

        // Assert: SC-4 — real derived value, not a stub
        Assert.True(_preferredLeaderService.IsPreferredStampFresh);
        _mockLiveness.Received(1).Stamp(JobKeyName);
    }

    // -------------------------------------------------------------------------
    // 2. Execute_WithStaleLease_SetsStampFreshFalse
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithStaleLease_SetsStampFreshFalse()
    {
        // Arrange: lease renewed DurationSeconds+5+1 seconds ago — beyond threshold
        var staleTime = DateTime.UtcNow.AddSeconds(-(DurationSeconds + 5 + 1));
        SetupLeaseResponse(FreshLease(renewTime: staleTime));
        var context = MakeContext();

        // Act
        await _job.Execute(context);

        // Assert
        Assert.False(_preferredLeaderService.IsPreferredStampFresh);
    }

    // -------------------------------------------------------------------------
    // 3. Execute_WithLeaseJustInsideThreshold_SetsStampFreshTrue
    //    Uses threshold - 1s to avoid wall-clock race: UtcNow advances a few ms
    //    between stamp computation and job execution, so the stamp must be safely
    //    inside the threshold window (age <= DurationSeconds+5) to pass reliably.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithLeaseJustInsideThreshold_SetsStampFreshTrue()
    {
        // Arrange: 1 second inside the threshold — age <= DurationSeconds+5 = true
        var justInsideThreshold = DateTime.UtcNow.AddSeconds(-(DurationSeconds + 5 - 1));
        SetupLeaseResponse(FreshLease(renewTime: justInsideThreshold));
        var context = MakeContext();

        // Act
        await _job.Execute(context);

        // Assert: age is within threshold, stamp is fresh
        Assert.True(_preferredLeaderService.IsPreferredStampFresh);
    }

    // -------------------------------------------------------------------------
    // 4. Execute_With404_SetsStampFreshFalse
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_With404_SetsStampFreshFalse()
    {
        // Arrange: Kubernetes returns 404 — lease not yet created or already expired
        SetupLeaseThrows(Make404Exception());
        var context = MakeContext();

        // Act
        await _job.Execute(context);

        // Assert: 404 = stale, not an error
        Assert.False(_preferredLeaderService.IsPreferredStampFresh);
        // Liveness still stamped (finally block runs)
        _mockLiveness.Received(1).Stamp(JobKeyName);
    }

    // -------------------------------------------------------------------------
    // 5. Execute_WithTransientError_KeepsLastValue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithTransientError_KeepsLastValue()
    {
        // Step 1: establish IsPreferredStampFresh = true with a fresh lease
        SetupLeaseResponse(FreshLease(renewTime: DateTime.UtcNow));
        await _job.Execute(MakeContext());
        Assert.True(_preferredLeaderService.IsPreferredStampFresh);

        // Step 2: simulate transient K8s error — should NOT flip to false
        SetupLeaseThrows(new HttpRequestException("connection refused"));
        await _job.Execute(MakeContext());

        // Assert: last known value preserved
        Assert.True(_preferredLeaderService.IsPreferredStampFresh);
    }

    // -------------------------------------------------------------------------
    // 6. Execute_WithNullRenewTimeAndNullAcquireTime_SetsStampFreshFalse
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithNullRenewTimeAndNullAcquireTime_SetsStampFreshFalse()
    {
        // Arrange: both timestamps are null — cannot determine freshness
        var lease = new V1Lease
        {
            Spec = new V1LeaseSpec
            {
                RenewTime = null,
                AcquireTime = null
            }
        };
        SetupLeaseResponse(lease);
        var context = MakeContext();

        // Act
        await _job.Execute(context);

        // Assert
        Assert.False(_preferredLeaderService.IsPreferredStampFresh);
    }

    // -------------------------------------------------------------------------
    // 7. Execute_WithNullRenewTime_FallsBackToAcquireTime
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithNullRenewTime_FallsBackToAcquireTime()
    {
        // Arrange: RenewTime is null but AcquireTime is fresh — fallback should yield true
        var lease = new V1Lease
        {
            Spec = new V1LeaseSpec
            {
                RenewTime = null,
                AcquireTime = DateTime.UtcNow  // fresh
            }
        };
        SetupLeaseResponse(lease);
        var context = MakeContext();

        // Act
        await _job.Execute(context);

        // Assert: RenewTime ?? AcquireTime = AcquireTime (fresh) => true
        Assert.True(_preferredLeaderService.IsPreferredStampFresh);
    }

    // -------------------------------------------------------------------------
    // 8. Execute_AlwaysStampsLiveness_EvenOnError
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_AlwaysStampsLiveness_EvenOnError()
    {
        // Arrange: generic exception thrown by lease read
        SetupLeaseThrows(new InvalidOperationException("unexpected failure"));
        var context = MakeContext();

        // Act
        await _job.Execute(context);

        // Assert: liveness.Stamp called exactly once regardless of error (finally block)
        _mockLiveness.Received(1).Stamp(JobKeyName);
    }

    // -------------------------------------------------------------------------
    // Minimal IJobExecutionContext stub
    // -------------------------------------------------------------------------

    private sealed class StubJobContext : IJobExecutionContext
    {
        private readonly IJobDetail _jobDetail;

        public StubJobContext(string jobKeyName)
        {
            _jobDetail = JobBuilder.Create<PreferredHeartbeatJob>()
                .WithIdentity(jobKeyName)
                .Build();
        }

        public IJobDetail JobDetail => _jobDetail;
        public CancellationToken CancellationToken => CancellationToken.None;
        public object? Result { get; set; }

        // --- Unused interface members ---
        public IScheduler Scheduler                  => throw new NotImplementedException();
        public ITrigger Trigger                      => throw new NotImplementedException();
        public ICalendar? Calendar                   => throw new NotImplementedException();
        public bool Recovering                       => false;
        public TriggerKey RecoveringTriggerKey       => throw new NotImplementedException();
        public int RefireCount                       => 0;
        public JobDataMap MergedJobDataMap           => throw new NotImplementedException();
        public JobDataMap JobDataMap                 => throw new NotImplementedException();
        public string FireInstanceId                 => string.Empty;
        public DateTimeOffset FireTimeUtc            => DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc  => null;
        public DateTimeOffset? NextFireTimeUtc       => null;
        public DateTimeOffset? PreviousFireTimeUtc   => null;
        public TimeSpan JobRunTime                   => TimeSpan.Zero;
        public IJob JobInstance                      => throw new NotImplementedException();

        public void Put(object key, object objectValue) { }
        public object? Get(object key) => null;
    }
}
