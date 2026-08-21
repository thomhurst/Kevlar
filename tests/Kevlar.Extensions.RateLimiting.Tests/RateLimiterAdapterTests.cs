using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using Kevlar.Extensions.RateLimiting;
using Kevlar.Testing;

namespace Kevlar.Extensions.RateLimiting.Tests;

public class RateLimiterAdapterTests
{
    private static readonly KevlarKey<string> TenantKey = new("tenant");

    [Test]
    public async Task Fixed_Window_Preserves_Retry_After_And_Hook_Order()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false,
        });
        using var listener = new RejectionListener("fixed-window");
        var order = new List<string>();
        RateLimiterRejectedEvent observed = default;
        var shield = Shield.Empty
            .RateLimit(limiter, options =>
            {
                options.OnRejected = rejection =>
                {
                    observed = rejection;
                    order.Add(listener.Count == 1 ? "metric-sync" : "sync-before-metric");
                };
                options.OnRejectedAsync = async rejection =>
                {
                    await Task.Yield();
                    await Assert.That(ReferenceEquals(rejection.Context, observed.Context)).IsTrue();
                    order.Add("async");
                };
            })
            .WithName("fixed-window");

        await shield.ExecuteAsync(static _ => new ValueTask<int>(1));
        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(2));

        var exception = outcome.Exception as RateLimitExceededException;
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.RetryAfter).IsNotNull();
        await Assert.That(exception.RetryAfter > TimeSpan.Zero).IsTrue();
        await Assert.That(observed.RetryAfter).IsEqualTo(exception.RetryAfter);
        await Assert.That(observed.Metadata.ContainsKey(MetadataName.RetryAfter.Name)).IsTrue();
        await Assert.That(observed.PermitCount).IsEqualTo(1);
        await Assert.That(observed.StrategyIndex).IsEqualTo(0);
        await Assert.That(order.SequenceEqual(["metric-sync", "async"])).IsTrue();
    }

    [Test]
    public async Task Sliding_Window_And_Chained_Limiters_Reject()
    {
        using var sliding = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 2,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false,
        });
        await AssertRejectsSecondExecution(sliding);

        using var first = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false,
        });
        using var second = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false,
        });
        using var chained = RateLimiter.CreateChained(first, second);
        await AssertRejectsSecondExecution(chained);
    }

    [Test]
    public async Task Concurrency_Limiter_Queues_Releases_And_Cancels()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 1,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejected = 0;
        var shield = Shield.Empty.RateLimit(limiter, options =>
            options.OnRejected = _ => rejected++);
        var occupying = shield.ExecuteAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await entered.Task;

        using var cancellation = new CancellationTokenSource();
        var queued = shield.ExecuteAsync(static _ => new ValueTask<int>(2), cancellation.Token).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();
        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        await Assert.That(rejected).IsEqualTo(0);

        var replacement = shield.ExecuteAsync(static _ => new ValueTask<int>(3)).AsTask();
        release.SetResult();
        await Assert.That(await occupying).IsEqualTo(1);
        await Assert.That(await replacement.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(3);
    }

    [Test]
    public async Task Partitioned_Limiter_Isolates_Context_Partitions_And_Composes_With_Partitioned_Shields()
    {
        using var limiter = CreateTenantConcurrencyLimiter(queueLimit: 0);
        var shields = new PartitionedShield<string>(_ => Shield.Empty.RateLimit(limiter));
        var shield = shields.GetShield("pipeline");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupying = ExecuteForTenantAsync(shield, "alpha", async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await entered.Task;

        await Assert.That(async () => await ExecuteForTenantAsync(
            shield,
            "alpha",
            static _ => new ValueTask<int>(2))).Throws<RateLimitExceededException>();
        await Assert.That(await ExecuteForTenantAsync(
            shield,
            "beta",
            static _ => new ValueTask<int>(3))).IsEqualTo(3);

        release.SetResult();
        await Assert.That(await occupying).IsEqualTo(1);
        await Assert.That(shields.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Partitioned_Limiter_Queue_Cancellation_Does_Not_Consume_Capacity()
    {
        using var limiter = CreateTenantConcurrencyLimiter(queueLimit: 1);
        var shield = Shield.Empty.RateLimit(limiter);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupying = ExecuteForTenantAsync(shield, "alpha", async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await entered.Task;

        using var cancellation = new CancellationTokenSource();
        var queued = ExecuteForTenantAsync(
            shield,
            "alpha",
            static _ => new ValueTask<int>(2),
            cancellation.Token).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();
        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();

        var replacement = ExecuteForTenantAsync(
            shield,
            "alpha",
            static _ => new ValueTask<int>(3)).AsTask();
        release.SetResult();
        await Assert.That(await occupying).IsEqualTo(1);
        await Assert.That(await replacement.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(3);
    }

    [Test]
    public async Task Partitioned_Limiter_Preserves_Context_Metadata_And_Lease_Ownership()
    {
        var retryAfter = TimeSpan.FromSeconds(5);
        var lease = new TrackingLease(
            isAcquired: false,
            new Dictionary<string, object?>
            {
                [MetadataName.RetryAfter.Name] = retryAfter,
                ["partition"] = "alpha",
            });
        string? observedTenant = null;
        CancellationToken observedCancellation = default;
        using var limiter = new StubPartitionedLimiter((context, cancellationToken) =>
        {
            observedTenant = context.Properties.GetOrDefault(TenantKey, string.Empty);
            observedCancellation = cancellationToken;
            return new ValueTask<RateLimitLease>(lease);
        });
        RateLimiterRejectedEvent observedRejection = default;
        var shield = Shield.Empty.RateLimit(
            limiter,
            options =>
            {
                options.PermitCount = 2;
                options.OnRejected = rejection => observedRejection = rejection;
            });
        using var cancellation = new CancellationTokenSource();

        var exception = await Assert.That(async () => await ExecuteForTenantAsync(
            shield,
            "alpha",
            static _ => new ValueTask<int>(42),
            cancellation.Token)).Throws<RateLimitExceededException>();

        await Assert.That(exception!.RetryAfter).IsEqualTo(retryAfter);
        await Assert.That(observedTenant).IsEqualTo("alpha");
        await Assert.That(observedCancellation).IsEqualTo(cancellation.Token);
        await Assert.That(observedRejection.Metadata["partition"]).IsEqualTo("alpha");
        await Assert.That(observedRejection.PermitCount).IsEqualTo(2);
        await Assert.That(lease.DisposeCount).IsEqualTo(1);
        await Assert.That(lease.MetadataReadAfterDispose).IsFalse();
    }

    [Test]
    public async Task Delegate_Acquisition_Receives_Context_And_Disposes_Lease_Once()
    {
        var lease = new TrackingLease(isAcquired: true);
        int observedPermitCount = 0;
        string? observedName = null;
        var shield = Shield.Empty
            .RateLimit((permitCount, context) =>
            {
                observedPermitCount = permitCount;
                observedName = context.ShieldName;
                return new ValueTask<RateLimitLease>(lease);
            }, options => options.PermitCount = 3)
            .WithName("delegate-source");

        await Assert.That(await shield.ExecuteAsync(static _ => new ValueTask<int>(42))).IsEqualTo(42);
        await Assert.That(observedPermitCount).IsEqualTo(3);
        await Assert.That(observedName).IsEqualTo("delegate-source");
        await Assert.That(lease.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Custom_Limiter_Lease_Is_Held_Through_Async_Execution()
    {
        var lease = new TrackingLease(isAcquired: true);
        using var limiter = new StubLimiter(_ => new ValueTask<RateLimitLease>(lease));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield<int>.Empty.RateLimit(limiter);

        var execution = shield.ExecuteAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 42;
        }).AsTask();
        await entered.Task;
        await Assert.That(lease.DisposeCount).IsEqualTo(0);

        release.SetResult();
        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(lease.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Rejected_Custom_Lease_Snapshots_Metadata_Before_Disposal()
    {
        var retryAfter = TimeSpan.FromSeconds(7);
        var lease = new TrackingLease(
            isAcquired: false,
            new Dictionary<string, object?>
            {
                [MetadataName.RetryAfter.Name] = retryAfter,
                ["tenant"] = "alpha",
            });
        RateLimiterRejectedEvent observed = default;
        using var limiter = new StubLimiter(_ => new ValueTask<RateLimitLease>(lease));
        var shield = Shield.Empty.RateLimit(limiter, options =>
            options.OnRejected = rejection => observed = rejection);

        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));

        await Assert.That(outcome.Exception).IsTypeOf<RateLimitExceededException>();
        await Assert.That(((RateLimitExceededException)outcome.Exception!).RetryAfter).IsEqualTo(retryAfter);
        await Assert.That(observed.Metadata["tenant"]).IsEqualTo("alpha");
        await Assert.That(lease.DisposeCount).IsEqualTo(1);
        await Assert.That(lease.MetadataReadAfterDispose).IsFalse();
    }

    [Test]
    public async Task Callback_Failure_Replaces_Rejection_And_Skips_Later_Hook()
    {
        var callbackFailure = new InvalidOperationException("callback failed");
        var asyncCalls = 0;
        var shield = Shield.Empty.RateLimit(
            static (_, _) => new ValueTask<RateLimitLease>(new TrackingLease(false)),
            options =>
            {
                options.OnRejected = _ => throw callbackFailure;
                options.OnRejectedAsync = _ =>
                {
                    asyncCalls++;
                    return ValueTask.CompletedTask;
                };
            });

        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));

        await Assert.That(ReferenceEquals(outcome.Exception, callbackFailure)).IsTrue();
        await Assert.That(asyncCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Asynchronous_Callback_Failure_Replaces_Rejection()
    {
        var callbackFailure = new InvalidOperationException("async callback failed");
        var shield = Shield.Empty.RateLimit(
            static (_, _) => new ValueTask<RateLimitLease>(new TrackingLease(false)),
            options => options.OnRejectedAsync = async _ =>
            {
                await Task.Yield();
                throw callbackFailure;
            });

        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));

        await Assert.That(ReferenceEquals(outcome.Exception, callbackFailure)).IsTrue();
    }

    [Test]
    public async Task Completed_Asynchronous_Callback_Preserves_Rejection()
    {
        var shield = Shield.Empty.RateLimit(
            static (_, _) => new ValueTask<RateLimitLease>(new TrackingLease(false)),
            options => options.OnRejectedAsync = static _ => ValueTask.CompletedTask);

        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));

        await Assert.That(outcome.Exception).IsTypeOf<RateLimitExceededException>();
    }

    [Test]
    public async Task Cancellation_Wins_Acquisition_Race_And_Disposes_Returned_Lease()
    {
        var acquisition = new TaskCompletionSource<RateLimitLease>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new TrackingLease(isAcquired: true);
        var invoked = false;
        var rejections = 0;
        var shield = Shield.Empty.RateLimit(
            (_, _) => new ValueTask<RateLimitLease>(acquisition.Task),
            options => options.OnRejected = _ => rejections++);
        using var cancellation = new CancellationTokenSource();

        var execution = shield.ExecuteAsync(_ =>
        {
            invoked = true;
            return new ValueTask<int>(42);
        }, cancellation.Token).AsTask();
        cancellation.Cancel();
        acquisition.SetResult(lease);

        var exception = await Assert.That(async () => await execution)
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(invoked).IsFalse();
        await Assert.That(rejections).IsEqualTo(0);
        await Assert.That(lease.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Concurrent_Async_Executions_Dispose_Every_Lease_Once()
    {
        const int executionCount = 64;
        var leases = new List<TrackingLease>();
        var gate = new object();
        var shield = Shield.Empty.RateLimit((_, _) =>
        {
            var lease = new TrackingLease(isAcquired: true);
            lock (gate)
            {
                leases.Add(lease);
            }

            return new ValueTask<RateLimitLease>(lease);
        });

        var executions = Enumerable.Range(0, executionCount)
            .Select(index => shield.ExecuteAsync(async _ =>
            {
                await Task.Yield();
                return index;
            }).AsTask());

        await Task.WhenAll(executions);
        await Assert.That(leases.Count).IsEqualTo(executionCount);
        await Assert.That(leases.All(static lease => lease.DisposeCount == 1)).IsTrue();
    }

    [Test]
    public async Task Description_Identifies_Source_Without_Exposing_Limiter()
    {
        using var limiter = new StubLimiter(static _ =>
            new ValueTask<RateLimitLease>(new TrackingLease(true)));
        var frameworkShield = Shield.Empty.RateLimit(limiter, options => options.PermitCount = 2);
        var delegateShield = Shield<int>.Empty.RateLimit(static (_, _) =>
            new ValueTask<RateLimitLease>(new TrackingLease(true)));
        using var partitionedLimiter = new StubPartitionedLimiter(static (_, _) =>
            new ValueTask<RateLimitLease>(new TrackingLease(true)));
        var partitionedShield = Shield<int>.Empty.RateLimit(partitionedLimiter);

        var frameworkDescriptor = frameworkShield.GetDescriptor()
            .AssertContainsSingle<CustomStrategyDescriptor>();
        var delegateDescriptor = delegateShield.GetDescriptor()
            .AssertContainsSingle<CustomStrategyDescriptor>();
        var partitionedDescriptor = partitionedShield.GetDescriptor()
            .AssertContainsSingle<CustomStrategyDescriptor>();

        await Assert.That(frameworkDescriptor.Description).IsEqualTo(
            "RateLimitAdapter(framework, permits 2)");
        await Assert.That(delegateDescriptor.Description).IsEqualTo(
            "RateLimitAdapter(delegate, permits 1)");
        await Assert.That(partitionedDescriptor.Description).IsEqualTo(
            "RateLimitAdapter(partitioned-framework, permits 1)");
        await Assert.That(frameworkDescriptor.Description.Contains(limiter.GetType().FullName!)).IsFalse();
        await Assert.That(partitionedDescriptor.Description.Contains(
            partitionedLimiter.GetType().FullName!)).IsFalse();
    }

    [Test]
    public async Task Acquisition_And_Lease_Failures_Are_Outcomes_And_Dispose_Once()
    {
        var synchronousFailure = new InvalidOperationException("sync acquire");
        var synchronous = Shield.Empty.RateLimit((_, _) => throw synchronousFailure);
        var synchronousOutcome = await synchronous.ExecuteOutcomeAsync(
            static _ => new ValueTask<int>(42));
        await Assert.That(ReferenceEquals(synchronousOutcome.Exception, synchronousFailure)).IsTrue();

        var asynchronousFailure = new InvalidOperationException("async acquire");
        var asynchronous = Shield.Empty.RateLimit((_, _) =>
            ValueTask.FromException<RateLimitLease>(asynchronousFailure));
        var asynchronousOutcome = await asynchronous.ExecuteOutcomeAsync(
            static _ => new ValueTask<int>(42));
        await Assert.That(ReferenceEquals(asynchronousOutcome.Exception, asynchronousFailure)).IsTrue();

        var stateFailure = new InvalidOperationException("lease state");
        var stateLease = new TrackingLease(true) { IsAcquiredFailure = stateFailure };
        var stateShield = Shield.Empty.RateLimit((_, _) => new ValueTask<RateLimitLease>(stateLease));
        var stateOutcome = await stateShield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
        await Assert.That(ReferenceEquals(stateOutcome.Exception, stateFailure)).IsTrue();
        await Assert.That(stateLease.DisposeCount).IsEqualTo(1);

        var metadataFailure = new InvalidOperationException("lease metadata");
        var metadataLease = new TrackingLease(false) { MetadataFailure = metadataFailure };
        var metadataShield = Shield.Empty.RateLimit((_, _) =>
            new ValueTask<RateLimitLease>(metadataLease));
        var metadataOutcome = await metadataShield.ExecuteOutcomeAsync(
            static _ => new ValueTask<int>(42));
        await Assert.That(ReferenceEquals(metadataOutcome.Exception, metadataFailure)).IsTrue();
        await Assert.That(metadataLease.DisposeCount).IsEqualTo(1);

        var disposalFailure = new InvalidOperationException("lease disposal");
        var acquiredLease = new TrackingLease(true) { DisposalFailure = disposalFailure };
        var acquiredShield = Shield.Empty.RateLimit((_, _) =>
            new ValueTask<RateLimitLease>(acquiredLease));
        var acquiredOutcome = await acquiredShield.ExecuteOutcomeAsync(
            static _ => new ValueTask<int>(42));
        await Assert.That(ReferenceEquals(acquiredOutcome.Exception, disposalFailure)).IsTrue();
        await Assert.That(acquiredLease.DisposeCount).IsEqualTo(1);

        var rejectedLease = new TrackingLease(false) { DisposalFailure = disposalFailure };
        var rejectedShield = Shield.Empty.RateLimit((_, _) =>
            new ValueTask<RateLimitLease>(rejectedLease));
        var rejectedOutcome = await rejectedShield.ExecuteOutcomeAsync(
            static _ => new ValueTask<int>(42));
        await Assert.That(ReferenceEquals(rejectedOutcome.Exception, disposalFailure)).IsTrue();
        await Assert.That(rejectedLease.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Disposal_Failure_Is_Aggregated_With_An_Earlier_Lease_Failure()
    {
        var stateFailure = new InvalidOperationException("lease state");
        var disposalFailure = new InvalidOperationException("lease disposal");
        var lease = new TrackingLease(true)
        {
            IsAcquiredFailure = stateFailure,
            DisposalFailure = disposalFailure,
        };
        var shield = Shield.Empty.RateLimit((_, _) => new ValueTask<RateLimitLease>(lease));

        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));

        var aggregate = outcome.Exception as AggregateException;
        await Assert.That(aggregate).IsNotNull();
        await Assert.That(aggregate!.InnerExceptions.Contains(stateFailure)).IsTrue();
        await Assert.That(aggregate.InnerExceptions.Contains(disposalFailure)).IsTrue();
        await Assert.That(lease.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Options_And_Duplicate_Strategy_Contracts_Are_Validated()
    {
        using var limiter = new StubLimiter(static _ =>
            new ValueTask<RateLimitLease>(new TrackingLease(true)));

        await Assert.That(() => Shield.Empty.RateLimit(
            limiter,
            options => options.PermitCount = 0)).Throws<ArgumentOutOfRangeException>();

        var shield = Shield.Empty.RateLimit(limiter);
        await Assert.That(shield.InvokesContinuationAtMostOnce).IsTrue();
        await Assert.That(() => Shield.Compose(shield, shield)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Null_Public_Arguments_Are_Rejected()
    {
        using var limiter = new StubLimiter(static _ =>
            new ValueTask<RateLimitLease>(new TrackingLease(true)));
        RateLimitLeaseAcquirer acquire = static (_, _) =>
            new ValueTask<RateLimitLease>(new TrackingLease(true));
        using var partitionedLimiter = new StubPartitionedLimiter(static (_, _) =>
            new ValueTask<RateLimitLease>(new TrackingLease(true)));

        await Assert.That(() => ShieldRateLimiterExtensions.RateLimit(null!, limiter))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ShieldRateLimiterExtensions.RateLimit(null!, acquire))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ShieldRateLimiterExtensions.RateLimit<int>(null!, limiter))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ShieldRateLimiterExtensions.RateLimit<int>(null!, acquire))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ShieldRateLimiterExtensions.RateLimit(null!, partitionedLimiter))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ShieldRateLimiterExtensions.RateLimit<int>(null!, partitionedLimiter))
            .Throws<ArgumentNullException>();
        await Assert.That(() => Shield.Empty.RateLimit((RateLimiter)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => Shield.Empty.RateLimit((RateLimitLeaseAcquirer)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => Shield.Empty.RateLimit((PartitionedRateLimiter<KevlarContext>)null!))
            .Throws<ArgumentNullException>();
    }

    private static PartitionedRateLimiter<KevlarContext> CreateTenantConcurrencyLimiter(
        int queueLimit) =>
        PartitionedRateLimiter.Create<KevlarContext, string>(context =>
            RateLimitPartition.GetConcurrencyLimiter(
                context.Properties.GetOrDefault(TenantKey, "default"),
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = 1,
                    QueueLimit = queueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }));

    private static ValueTask<T> ExecuteForTenantAsync<T>(
        Shield shield,
        string tenant,
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken = default) =>
        shield.ExecuteWithContextAsync(
            (tenant, action),
            static (state, properties) => properties.Set(TenantKey, state.tenant),
            static (state, context) => state.action(context.CancellationToken),
            cancellationToken);

    private static async Task AssertRejectsSecondExecution(RateLimiter limiter)
    {
        var shield = Shield.Empty.RateLimit(limiter);
        await shield.ExecuteAsync(static _ => new ValueTask<int>(1));
        await Assert.That(async () => await shield.ExecuteAsync(static _ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();
    }

    private sealed class StubLimiter(
        Func<CancellationToken, ValueTask<RateLimitLease>> acquire) : RateLimiter
    {
        public override TimeSpan? IdleDuration => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
            acquire(default).GetAwaiter().GetResult();

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            int permitCount,
            CancellationToken cancellationToken) => acquire(cancellationToken);

        public override RateLimiterStatistics? GetStatistics() => null;
    }

    private sealed class StubPartitionedLimiter(
        Func<KevlarContext, CancellationToken, ValueTask<RateLimitLease>> acquire)
        : PartitionedRateLimiter<KevlarContext>
    {
        protected override RateLimitLease AttemptAcquireCore(
            KevlarContext resource,
            int permitCount) =>
            acquire(resource, default).GetAwaiter().GetResult();

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            KevlarContext resource,
            int permitCount,
            CancellationToken cancellationToken) => acquire(resource, cancellationToken);

        public override RateLimiterStatistics? GetStatistics(KevlarContext resource) => null;
    }

    private sealed class TrackingLease : RateLimitLease
    {
        private readonly bool _isAcquired;
        private readonly IReadOnlyDictionary<string, object?> _metadata;
        private int _disposeCount;

        public TrackingLease(
            bool isAcquired,
            IReadOnlyDictionary<string, object?>? metadata = null)
        {
            _isAcquired = isAcquired;
            _metadata = metadata ?? new Dictionary<string, object?>();
        }

        public override bool IsAcquired => IsAcquiredFailure is null
            ? _isAcquired
            : throw IsAcquiredFailure;

        public override IEnumerable<string> MetadataNames => _metadata.Keys;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Exception? IsAcquiredFailure { get; init; }

        public Exception? MetadataFailure { get; init; }

        public Exception? DisposalFailure { get; init; }

        public bool MetadataReadAfterDispose { get; private set; }

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (MetadataFailure is not null)
            {
                throw MetadataFailure;
            }

            MetadataReadAfterDispose |= DisposeCount > 0;
            return _metadata.TryGetValue(metadataName, out metadata);
        }

        protected override void Dispose(bool disposing)
        {
            Interlocked.Increment(ref _disposeCount);
            if (DisposalFailure is not null)
            {
                throw DisposalFailure;
            }
        }
    }

    private sealed class RejectionListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly string _shieldName;
        private int _count;

        public RejectionListener(string shieldName)
        {
            _shieldName = shieldName;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName
                    && instrument.Name == "kevlar.rejections")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
            {
                string? name = null;
                string? kind = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "kevlar.shield.name")
                    {
                        name = tag.Value as string;
                    }
                    else if (tag.Key == "kevlar.rejection.type")
                    {
                        kind = tag.Value as string;
                    }
                }

                if (name == _shieldName && kind == "rate_limit")
                {
                    Interlocked.Add(ref _count, (int)measurement);
                }
            });
            _listener.Start();
        }

        public int Count => Volatile.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
