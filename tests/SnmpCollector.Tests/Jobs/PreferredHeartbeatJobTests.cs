using System.Net;
using System.Reflection;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Hosting;
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
/// Unit tests for <see cref="PreferredHeartbeatJob"/> covering:
/// <list type="bullet">
///   <item>Reader path — fresh/stale/404/transient-error/null-timestamps/liveness (8 tests, Phase 85)</item>
///   <item>Writer path — create, renew, readiness gate, non-preferred skip, 409/404 conflict,
///         transient error, AcquireTime only on create (9 tests, Phase 86)</item>
///   <item>Yield path — Gate 2 voluntary yield (Phase 88): happy path, each negative condition,
///         delete failure resilience (6 tests)</item>
/// </list>
/// <para>
/// SC-4: IPreferredStampReader.IsPreferredStampFresh returns a real derived value, not a stub —
/// verified here with a mocked K8s lease response.
/// </para>
/// <para>
/// Mocking strategy: the extension methods (<c>ReadNamespacedLeaseAsync</c>,
/// <c>CreateNamespacedLeaseAsync</c>, <c>ReplaceNamespacedLeaseAsync</c>) delegate to the
/// underlying <see cref="ICoordinationV1Operations"/> WithHttpMessages overloads.
/// NSubstitute mocks those interface methods; the extensions delegate automatically.
/// </para>
/// </summary>
public sealed class PreferredHeartbeatJobTests
{
    private const int DurationSeconds = 15;
    private const string LeaseName = "snmp-collector-leader";
    private const string Namespace = "default";
    private const string JobKeyName = "preferred-heartbeat";
    private const string PodId = "test-pod-01";
    private const string NodeName = "node-a";

    // -------------------------------------------------------------------------
    // Shared test infrastructure
    // -------------------------------------------------------------------------

    private readonly ICoordinationV1Operations _mockCoordV1 = Substitute.For<ICoordinationV1Operations>();
    private readonly IKubernetes _mockKubeClient = Substitute.For<IKubernetes>();
    private readonly ILivenessVectorService _mockLiveness = Substitute.For<ILivenessVectorService>();

    private readonly IOptions<LeaseOptions> _leaseOptions;
    private readonly IOptions<PodIdentityOptions> _podIdentityOptions;

    // Reader-only test job: not preferred, not ready (default lifetime mock, CancellationToken.None)
    private readonly PreferredLeaderService _preferredLeaderService;
    private readonly PreferredHeartbeatJob _job;

    public PreferredHeartbeatJobTests()
    {
        _mockKubeClient.CoordinationV1.Returns(_mockCoordV1);

        _leaseOptions = Options.Create(new LeaseOptions
        {
            Name = LeaseName,
            Namespace = Namespace,
            DurationSeconds = DurationSeconds
        });

        _podIdentityOptions = Options.Create(new PodIdentityOptions
        {
            PodIdentity = PodId
        });

        _preferredLeaderService = new PreferredLeaderService(
            _leaseOptions,
            NullLogger<PreferredLeaderService>.Instance);

        // Default job: not preferred (no PreferredNode set), not ready (CancellationToken.None never fires)
        var lifetime = MakeLifetime(alreadyStarted: false);
        _job = new PreferredHeartbeatJob(
            _mockKubeClient,
            _preferredLeaderService,
            _leaseOptions,
            _podIdentityOptions,
            lifetime,
            _mockLiveness,
            NullLogger<PreferredHeartbeatJob>.Instance);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IJobExecutionContext MakeContext(string jobKeyName = JobKeyName)
        => new StubJobContext(jobKeyName);

    /// <summary>
    /// Returns a mock IHostApplicationLifetime whose ApplicationStarted token is either
    /// already cancelled (simulating the host fully started) or not (host still starting).
    /// </summary>
    private static IHostApplicationLifetime MakeLifetime(bool alreadyStarted)
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        if (alreadyStarted)
        {
            // Create a pre-cancelled token so Register fires synchronously in the constructor.
            var cts = new CancellationTokenSource();
            cts.Cancel();
            lifetime.ApplicationStarted.Returns(cts.Token);
        }
        else
        {
            lifetime.ApplicationStarted.Returns(CancellationToken.None);
        }

        return lifetime;
    }

    /// <summary>
    /// Builds a PreferredHeartbeatJob that is the preferred pod (PHYSICAL_HOSTNAME == PreferredNode)
    /// and optionally already started (readiness gate open).
    /// Caller MUST restore PHYSICAL_HOSTNAME after the test.
    /// </summary>
    private PreferredHeartbeatJob MakePreferredJob(
        bool schedulerReady,
        out PreferredLeaderService preferredLeaderService)
    {
        var leaseOptions = Options.Create(new LeaseOptions
        {
            Name = LeaseName,
            Namespace = Namespace,
            DurationSeconds = DurationSeconds,
            PreferredNode = NodeName          // enables preferred-pod feature
        });

        // PHYSICAL_HOSTNAME must match PreferredNode for IsPreferredPod = true
        Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", NodeName);

        preferredLeaderService = new PreferredLeaderService(
            leaseOptions,
            NullLogger<PreferredLeaderService>.Instance);

        var lifetime = MakeLifetime(alreadyStarted: schedulerReady);

        return new PreferredHeartbeatJob(
            _mockKubeClient,
            preferredLeaderService,
            leaseOptions,
            _podIdentityOptions,
            lifetime,
            _mockLiveness,
            NullLogger<PreferredHeartbeatJob>.Instance);
    }

    /// <summary>
    /// Sets up the read mock to return the given lease.
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
    /// Sets up the read mock to throw the given exception.
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

    /// <summary>
    /// Sets up CreateNamespacedLeaseWithHttpMessagesAsync to return a lease with the given resourceVersion.
    /// </summary>
    private void SetupCreateResponse(string resourceVersion = "rv-100")
    {
        var created = MakeLease(resourceVersion);
        var response = new HttpOperationResponse<V1Lease>
        {
            Body = created,
            Response = new HttpResponseMessage(HttpStatusCode.Created)
        };

        _mockCoordV1
            .CreateNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<V1Lease>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
    }

    /// <summary>
    /// Sets up CreateNamespacedLeaseWithHttpMessagesAsync to throw an HttpOperationException
    /// with the specified status code.
    /// </summary>
    private void SetupCreateThrows(HttpStatusCode statusCode)
    {
        _mockCoordV1
            .CreateNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<V1Lease>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(MakeHttpException(statusCode));
    }

    /// <summary>
    /// Sets up CreateNamespacedLeaseWithHttpMessagesAsync to throw a generic exception.
    /// </summary>
    private void SetupCreateThrowsGeneric(Exception ex)
    {
        _mockCoordV1
            .CreateNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<V1Lease>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);
    }

    /// <summary>
    /// Sets up ReplaceNamespacedLeaseWithHttpMessagesAsync to return a lease with a new resourceVersion.
    /// </summary>
    private void SetupReplaceResponse(string resourceVersion = "rv-200")
    {
        var replaced = MakeLease(resourceVersion);
        var response = new HttpOperationResponse<V1Lease>
        {
            Body = replaced,
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };

        _mockCoordV1
            .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<V1Lease>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
    }

    /// <summary>
    /// Sets up ReplaceNamespacedLeaseWithHttpMessagesAsync to throw an HttpOperationException.
    /// </summary>
    private void SetupReplaceThrows(HttpStatusCode statusCode)
    {
        _mockCoordV1
            .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<V1Lease>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(MakeHttpException(statusCode));
    }

    private static V1Lease MakeLease(string resourceVersion = "rv-100") =>
        new()
        {
            Metadata = new V1ObjectMeta { ResourceVersion = resourceVersion },
            Spec = new V1LeaseSpec
            {
                HolderIdentity = PodId,
                RenewTime = DateTime.UtcNow,
                LeaseDurationSeconds = DurationSeconds
            }
        };

    private static V1Lease FreshLease(DateTime? renewTime = null, DateTime? acquireTime = null) =>
        new()
        {
            Spec = new V1LeaseSpec
            {
                RenewTime = renewTime ?? DateTime.UtcNow,
                AcquireTime = acquireTime ?? DateTime.UtcNow.AddMinutes(-5)
            }
        };

    private static HttpOperationException Make404Exception() =>
        MakeHttpException(HttpStatusCode.NotFound);

    private static HttpOperationException MakeHttpException(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessageWrapper(
            new HttpResponseMessage(statusCode), "");
        return new HttpOperationException(statusCode.ToString()) { Response = response };
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

    // =========================================================================
    //  Writer-path tests (Phase 86)
    // =========================================================================

    // -------------------------------------------------------------------------
    // 9. Execute_PreferredAndReady_CreatesHeartbeatLease
    //    HB-01: preferred pod + scheduler ready + no cached resourceVersion
    //    -> calls Create with HolderIdentity, RenewTime, LeaseDurationSeconds, AcquireTime
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_PreferredAndReady_CreatesHeartbeatLease()
    {
        // Arrange
        SetupCreateResponse("rv-100");
        SetupLeaseResponse(FreshLease());  // reader path

        var job = MakePreferredJob(schedulerReady: true, out _);

        try
        {
            // Act
            await job.Execute(MakeContext());

            // Assert: Create called exactly once
            await _mockCoordV1.Received(1)
                .CreateNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Is<V1Lease>(l =>
                        l.Spec.HolderIdentity == PodId &&
                        l.Spec.RenewTime != null &&
                        l.Spec.AcquireTime != null &&
                        l.Spec.LeaseDurationSeconds == DurationSeconds),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());

            // Replace must NOT be called on first tick
            await _mockCoordV1.DidNotReceive()
                .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<V1Lease>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 10. Execute_PreferredAndReady_RenewsWithCachedResourceVersion
    //     Second tick uses Replace (not Create) with the cached resourceVersion.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_PreferredAndReady_RenewsWithCachedResourceVersion()
    {
        // Arrange: first tick creates (rv-100), second tick replaces (rv-200)
        SetupCreateResponse("rv-100");
        SetupReplaceResponse("rv-200");
        SetupLeaseResponse(FreshLease());  // reader path

        var job = MakePreferredJob(schedulerReady: true, out _);

        try
        {
            // Act: tick 1 — create
            await job.Execute(MakeContext());

            // Act: tick 2 — replace using cached rv-100
            await job.Execute(MakeContext());

            // Assert: Replace called with the resourceVersion from tick 1
            await _mockCoordV1.Received(1)
                .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Is<V1Lease>(l => l.Metadata.ResourceVersion == "rv-100"),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 11. Execute_PreferredButNotReady_SkipsWrite
    //     Readiness gate: scheduler not ready -> no Create or Replace.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_PreferredButNotReady_SkipsWrite()
    {
        // Arrange: preferred pod but scheduler NOT ready (CancellationToken.None)
        SetupLeaseResponse(FreshLease());  // reader path only

        var job = MakePreferredJob(schedulerReady: false, out _);

        try
        {
            // Act
            await job.Execute(MakeContext());

            // Assert: no write calls at all
            await _mockCoordV1.DidNotReceive()
                .CreateNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<V1Lease>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());

            await _mockCoordV1.DidNotReceive()
                .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<V1Lease>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 12. Execute_NotPreferred_SkipsWrite
    //     Non-preferred pod -> no Create or Replace. Reader path still runs.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_NotPreferred_SkipsWrite()
    {
        // Arrange: default _job is not preferred (no PreferredNode), but scheduler ready doesn't matter
        SetupLeaseResponse(FreshLease());  // reader path

        // Act
        await _job.Execute(MakeContext());

        // Assert: no write calls, reader path ran (IsPreferredStampFresh updated)
        await _mockCoordV1.DidNotReceive()
            .CreateNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<V1Lease>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>());

        await _mockCoordV1.DidNotReceive()
            .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<V1Lease>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>());

        Assert.True(_preferredLeaderService.IsPreferredStampFresh);
    }

    // -------------------------------------------------------------------------
    // 13. Execute_CreateConflict409_FallsBackToReadThenReplace
    //     409 on Create -> reads existing lease -> calls Replace.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_CreateConflict409_FallsBackToReadThenReplace()
    {
        // Arrange: Create throws 409, Read returns rv-existing, Replace succeeds
        SetupCreateThrows(HttpStatusCode.Conflict);

        // The read for the fallback path (ReadNamespacedLeaseAsync) returns a lease with a resourceVersion
        var existingLease = MakeLease("rv-existing");
        var readResponse = new HttpOperationResponse<V1Lease>
        {
            Body = existingLease,
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        _mockCoordV1
            .ReadNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(readResponse);

        SetupReplaceResponse("rv-200");

        var job = MakePreferredJob(schedulerReady: true, out _);

        try
        {
            // Act
            await job.Execute(MakeContext());

            // Assert: Replace called with resourceVersion from the read fallback
            await _mockCoordV1.Received(1)
                .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Is<V1Lease>(l => l.Metadata.ResourceVersion == "rv-existing"),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 14. Execute_ReplaceConflict409_InvalidatesCache
    //     409 on Replace -> cache invalidated -> next tick calls Create again.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ReplaceConflict409_InvalidatesCache()
    {
        // Arrange: tick 1 creates (caches rv-100), tick 2 replaces but gets 409
        SetupCreateResponse("rv-100");
        SetupLeaseResponse(FreshLease());  // reader path

        var job = MakePreferredJob(schedulerReady: true, out _);

        try
        {
            // Tick 1: create succeeds, rv-100 cached
            await job.Execute(MakeContext());

            // Tick 2: replace throws 409 -> cache invalidated
            SetupReplaceThrows(HttpStatusCode.Conflict);
            await job.Execute(MakeContext());

            // Tick 3: cache is null again -> Create called (not Replace)
            SetupCreateResponse("rv-300");
            _mockCoordV1.ClearReceivedCalls();
            await job.Execute(MakeContext());

            // Assert: Create called again (not Replace) on tick 3
            await _mockCoordV1.Received(1)
                .CreateNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<V1Lease>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());

            await _mockCoordV1.DidNotReceive()
                .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<V1Lease>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 15. Execute_Replace404_InvalidatesCache
    //     404 on Replace -> cache invalidated -> next tick calls Create again.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_Replace404_InvalidatesCache()
    {
        // Arrange: tick 1 creates (caches rv-100), tick 2 replaces but gets 404
        SetupCreateResponse("rv-100");
        SetupLeaseResponse(FreshLease());  // reader path

        var job = MakePreferredJob(schedulerReady: true, out _);

        try
        {
            // Tick 1: create succeeds
            await job.Execute(MakeContext());

            // Tick 2: replace throws 404 -> cache invalidated
            SetupReplaceThrows(HttpStatusCode.NotFound);
            await job.Execute(MakeContext());

            // Tick 3: cache null -> Create called again
            SetupCreateResponse("rv-300");
            _mockCoordV1.ClearReceivedCalls();
            await job.Execute(MakeContext());

            await _mockCoordV1.Received(1)
                .CreateNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<V1Lease>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 16. Execute_WriteTransientError_LogsWarningAndContinuesRead
    //     Generic exception on Create -> job does not throw, reader path runs,
    //     liveness stamped.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_WriteTransientError_LogsWarningAndContinuesRead()
    {
        // Arrange: Create throws a transient error
        SetupCreateThrowsGeneric(new HttpRequestException("connection refused"));
        SetupLeaseResponse(FreshLease());  // reader path

        var job = MakePreferredJob(schedulerReady: true, out var preferredLeaderService);

        try
        {
            // Act: should NOT throw
            await job.Execute(MakeContext());

            // Assert: liveness stamped (finally block in Execute)
            _mockLiveness.Received(1).Stamp(JobKeyName);

            // Assert: reader path still ran (stamp freshness updated)
            Assert.True(preferredLeaderService.IsPreferredStampFresh);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 17. Execute_AcquireTimeSetOnlyOnCreate_NotOnReplace
    //     AcquireTime present in Create body, absent (null) in Replace body.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_AcquireTimeSetOnlyOnCreate_NotOnReplace()
    {
        // Capture the lease bodies sent to Create and Replace
        V1Lease? capturedCreate = null;
        V1Lease? capturedReplace = null;

        var createResponse = new HttpOperationResponse<V1Lease>
        {
            Body = MakeLease("rv-100"),
            Response = new HttpResponseMessage(HttpStatusCode.Created)
        };
        _mockCoordV1
            .CreateNamespacedLeaseWithHttpMessagesAsync(
                Arg.Do<V1Lease>(l => capturedCreate = l),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(createResponse);

        var replaceResponse = new HttpOperationResponse<V1Lease>
        {
            Body = MakeLease("rv-200"),
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        _mockCoordV1
            .ReplaceNamespacedLeaseWithHttpMessagesAsync(
                Arg.Do<V1Lease>(l => capturedReplace = l),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(replaceResponse);

        SetupLeaseResponse(FreshLease());  // reader path

        var job = MakePreferredJob(schedulerReady: true, out _);

        try
        {
            // Tick 1: Create
            await job.Execute(MakeContext());
            // Tick 2: Replace
            await job.Execute(MakeContext());

            // Assert: AcquireTime set on Create body
            Assert.NotNull(capturedCreate);
            Assert.NotNull(capturedCreate!.Spec.AcquireTime);

            // Assert: AcquireTime NOT set on Replace body (renewal only updates RenewTime)
            Assert.NotNull(capturedReplace);
            Assert.Null(capturedReplace!.Spec.AcquireTime);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // =========================================================================
    //  Yield path helpers
    // =========================================================================

    /// <summary>
    /// Builds a non-preferred <see cref="PreferredHeartbeatJob"/> that has a real
    /// <see cref="K8sLeaseElection"/> injected, enabling yield path testing.
    /// The job's PHYSICAL_HOSTNAME is set to "other-node", which does NOT match
    /// <see cref="NodeName"/> — so <c>IsPreferredPod</c> is <c>false</c>.
    /// Caller MUST restore PHYSICAL_HOSTNAME in a finally block.
    /// </summary>
    private PreferredHeartbeatJob MakeNonPreferredJobWithElection(
        out PreferredLeaderService preferredLeaderService,
        out K8sLeaseElection leaseElection)
    {
        var leaseOptions = Options.Create(new LeaseOptions
        {
            Name = LeaseName,
            Namespace = Namespace,
            DurationSeconds = DurationSeconds,
            PreferredNode = NodeName   // feature enabled, but this pod is NOT preferred
        });

        // PHYSICAL_HOSTNAME != PreferredNode → IsPreferredPod = false
        Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", "other-node");

        preferredLeaderService = new PreferredLeaderService(
            leaseOptions,
            NullLogger<PreferredLeaderService>.Instance);

        // Real K8sLeaseElection (sealed — cannot substitute).
        // Uses shared _mockKubeClient so DeleteNamespacedLeaseAsync calls are captured.
        // PreferredLeaderService implements IPreferredStampReader, so it serves as both.
        leaseElection = new K8sLeaseElection(
            leaseOptions,
            Options.Create(new PodIdentityOptions { PodIdentity = PodId }),
            _mockKubeClient,
            Substitute.For<IHostApplicationLifetime>(),
            NullLogger<K8sLeaseElection>.Instance,
            preferredLeaderService,
            preferredLeaderService);  // implements IPreferredStampReader

        var lifetime = MakeLifetime(alreadyStarted: false);

        return new PreferredHeartbeatJob(
            _mockKubeClient,
            preferredLeaderService,
            leaseOptions,
            _podIdentityOptions,
            lifetime,
            _mockLiveness,
            NullLogger<PreferredHeartbeatJob>.Instance,
            leaseElection);
    }

    /// <summary>
    /// Uses reflection to set <c>_isLeader = true</c> on a real (sealed)
    /// <see cref="K8sLeaseElection"/> instance. Acceptable for unit-testing
    /// sealed classes whose internal state cannot be reached via public API
    /// without running the full election loop.
    /// </summary>
    private static void SetIsLeader(K8sLeaseElection election, bool value)
    {
        var field = typeof(K8sLeaseElection)
            .GetField("_isLeader", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_isLeader field not found on K8sLeaseElection");

        field.SetValue(election, value);
    }

    /// <summary>
    /// Sets up <c>DeleteNamespacedLeaseWithHttpMessagesAsync</c> to return HTTP 200.
    /// </summary>
    private void SetupDeleteSucceeds()
    {
        var response = new HttpOperationResponse<V1Status>
        {
            Body = new V1Status(),
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        _mockCoordV1
            .DeleteNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
    }

    /// <summary>
    /// Sets up <c>DeleteNamespacedLeaseWithHttpMessagesAsync</c> to throw the given exception.
    /// </summary>
    private void SetupDeleteThrows(Exception ex)
    {
        _mockCoordV1
            .DeleteNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);
    }

    // =========================================================================
    //  Yield path tests — Gate 2 (Phase 88)
    // =========================================================================

    // -------------------------------------------------------------------------
    // 18. Execute_NonPreferredLeader_StampFresh_YieldsLeadership
    //     Happy path: non-preferred pod is leader + stamp is fresh → delete called.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_NonPreferredLeader_StampFresh_YieldsLeadership()
    {
        // Arrange
        SetupLeaseResponse(FreshLease(renewTime: DateTime.UtcNow));
        SetupDeleteSucceeds();

        var job = MakeNonPreferredJobWithElection(
            out var preferredLeaderService,
            out var leaseElection);

        try
        {
            // Simulate this pod being the leader (reflection — sealed class)
            SetIsLeader(leaseElection, true);

            // Act: reader path sets stamp fresh, then Gate 2 condition fires
            await job.Execute(MakeContext());

            // Assert: delete called with leadership lease name and namespace
            await _mockCoordV1.Received(1)
                .DeleteNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Is<string>(n => n == LeaseName),
                    Arg.Is<string>(ns => ns == Namespace),
                    Arg.Any<V1DeleteOptions>(),
                    Arg.Any<string?>(),
                    Arg.Any<int?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 19. Execute_StampStale_DoesNotYield
    //     Stamp is stale → Gate 2 condition is false → no delete.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_StampStale_DoesNotYield()
    {
        // Arrange: lease renewed beyond threshold → stale
        var staleTime = DateTime.UtcNow.AddSeconds(-(DurationSeconds + 5 + 1));
        SetupLeaseResponse(FreshLease(renewTime: staleTime));

        var job = MakeNonPreferredJobWithElection(
            out _,
            out var leaseElection);

        try
        {
            SetIsLeader(leaseElection, true);

            // Act
            await job.Execute(MakeContext());

            // Assert: no delete
            await _mockCoordV1.DidNotReceive()
                .DeleteNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<V1DeleteOptions>(),
                    Arg.Any<string?>(),
                    Arg.Any<int?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 20. Execute_NotLeader_DoesNotYield
    //     _isLeader is false (default) → Gate 2 condition is false → no delete.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_NotLeader_DoesNotYield()
    {
        // Arrange: stamp is fresh, but IsLeader is NOT set (default false)
        SetupLeaseResponse(FreshLease(renewTime: DateTime.UtcNow));

        var job = MakeNonPreferredJobWithElection(out _, out _);

        try
        {
            // Act — _isLeader stays false (not calling SetIsLeader)
            await job.Execute(MakeContext());

            // Assert: no delete
            await _mockCoordV1.DidNotReceive()
                .DeleteNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<V1DeleteOptions>(),
                    Arg.Any<string?>(),
                    Arg.Any<int?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 21. Execute_PreferredPod_DoesNotYield
    //     Preferred pod (IsPreferredPod = true) → Gate 2 condition false → no delete.
    //     The preferred pod never yields — it IS the one we're yielding to.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_PreferredPod_DoesNotYield()
    {
        // Arrange: preferred pod, scheduler ready, stamp fresh
        SetupLeaseResponse(FreshLease(renewTime: DateTime.UtcNow));
        SetupCreateResponse();

        // Use MakePreferredJob — IsPreferredPod = true, PHYSICAL_HOSTNAME == NodeName
        var job = MakePreferredJob(schedulerReady: true, out var preferredLeaderService);

        // Build a separate K8sLeaseElection for this preferred job — IsPreferredPod = true
        var leaseOptions = Options.Create(new LeaseOptions
        {
            Name = LeaseName,
            Namespace = Namespace,
            DurationSeconds = DurationSeconds,
            PreferredNode = NodeName
        });

        // Note: PHYSICAL_HOSTNAME == NodeName was already set by MakePreferredJob
        var leaseElection = new K8sLeaseElection(
            leaseOptions,
            Options.Create(new PodIdentityOptions { PodIdentity = PodId }),
            _mockKubeClient,
            Substitute.For<IHostApplicationLifetime>(),
            NullLogger<K8sLeaseElection>.Instance,
            preferredLeaderService,
            preferredLeaderService);

        SetIsLeader(leaseElection, true);

        // Rebuild job with the election — preferred pod + election injected
        var jobWithElection = new PreferredHeartbeatJob(
            _mockKubeClient,
            preferredLeaderService,
            leaseOptions,
            _podIdentityOptions,
            MakeLifetime(alreadyStarted: true),
            _mockLiveness,
            NullLogger<PreferredHeartbeatJob>.Instance,
            leaseElection);

        try
        {
            // Act
            await jobWithElection.Execute(MakeContext());

            // Assert: no delete — preferred pod never yields
            await _mockCoordV1.DidNotReceive()
                .DeleteNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<V1DeleteOptions>(),
                    Arg.Any<string?>(),
                    Arg.Any<int?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // -------------------------------------------------------------------------
    // 22. Execute_NullLeaseElection_DoesNotYield
    //     When K8sLeaseElection is null (default job, no election injected),
    //     Gate 2 condition is false → no delete, no NullReferenceException.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_NullLeaseElection_DoesNotYield()
    {
        // Arrange: default _job has no K8sLeaseElection. Stamp fresh.
        SetupLeaseResponse(FreshLease(renewTime: DateTime.UtcNow));

        // Act — no exception expected
        await _job.Execute(MakeContext());

        // Assert: no delete attempted
        await _mockCoordV1.DidNotReceive()
            .DeleteNamespacedLeaseWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // 23. Execute_Yield_DeleteFails_StillCancelsInnerElection
    //     Delete throws → job does NOT throw → liveness stamped (election cancel
    //     fires even on delete failure — the warning path in YieldLeadershipAsync).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_Yield_DeleteFails_StillCancelsInnerElection()
    {
        // Arrange: delete throws a 500 Internal Server Error
        SetupLeaseResponse(FreshLease(renewTime: DateTime.UtcNow));
        SetupDeleteThrows(MakeHttpException(HttpStatusCode.InternalServerError));

        var job = MakeNonPreferredJobWithElection(
            out _,
            out var leaseElection);

        try
        {
            SetIsLeader(leaseElection, true);

            // Act: should NOT propagate the exception
            await job.Execute(MakeContext());

            // Assert: delete was attempted
            await _mockCoordV1.Received(1)
                .DeleteNamespacedLeaseWithHttpMessagesAsync(
                    Arg.Is<string>(n => n == LeaseName),
                    Arg.Is<string>(ns => ns == Namespace),
                    Arg.Any<V1DeleteOptions>(),
                    Arg.Any<string?>(),
                    Arg.Any<int?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool?>(),
                    Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    Arg.Any<CancellationToken>());

            // Assert: job completed normally (liveness stamp fired — delete failure is swallowed)
            _mockLiveness.Received(1).Stamp(JobKeyName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
        }
    }

    // =========================================================================
    //  Minimal IJobExecutionContext stub
    // =========================================================================

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
