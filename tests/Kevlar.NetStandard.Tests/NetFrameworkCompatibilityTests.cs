using System.Net;
using System.Net.Http;
using Kevlar.Extensions.Http;

namespace Kevlar.NetStandard.Tests;

internal static class NetFrameworkCompatibilityTests
{
    public static async Task Main()
    {
        await RetryHandlesExceptionsAndResults();
        await TimeoutUsesBclTimeProvider();
        await CircuitBreakerTransitions();
        await RateLimitReportsRetryAfter();
        await PartitionCapacityEvicts();
        await OutcomeAndSyncExecutionWork();
        SynchronousExecutionAvoidsSynchronizationContextPosts();
        await HttpRetryPreservesProperties();
        Console.WriteLine("Kevlar net48 compatibility tests passed.");
    }

    private static async Task RetryHandlesExceptionsAndResults()
    {
        var exceptionAttempts = 0;
        var exceptionResult = await Shield.Retry(1, Backoff.None).ExecuteAsync<int>(_ =>
            ++exceptionAttempts == 1
                ? throw new InvalidOperationException("retry")
                : new ValueTask<int>(42));
        Equal(42, exceptionResult, "exception retry result");
        Equal(2, exceptionAttempts, "exception retry attempts");

        var resultAttempts = 0;
        var result = await Shield.For<int>()
            .WhenResult(static value => value < 0)
            .Retry(1, Backoff.None)
            .ExecuteAsync(_ => new ValueTask<int>(++resultAttempts == 1 ? -1 : 42));
        Equal(42, result, "result retry result");
        Equal(2, resultAttempts, "result retry attempts");
    }

    private static async Task TimeoutUsesBclTimeProvider()
    {
        var timeProvider = new ManualTimeProvider();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = Shield.Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(timeProvider)
            .ExecuteOutcomeAsync<int>(async cancellationToken =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 42;
            }).AsTask();
        await started.Task;
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var outcome = await execution;
        True(outcome.Exception is TimeoutExceededException, "timeout outcome");
    }

    private static async Task CircuitBreakerTransitions()
    {
        var timeProvider = new ManualTimeProvider();
        var shield = Shield.CircuitBreaker(
            consecutiveFailures: 1,
            breakDuration: TimeSpan.FromSeconds(1)).WithTimeProvider(timeProvider);

        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("trip"));
        var rejected = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));
        True(rejected.Exception is CircuitOpenException, "open circuit rejection");

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Equal(42, await shield.ExecuteAsync(_ => new ValueTask<int>(42)), "half-open recovery");
        Equal(43, await shield.ExecuteAsync(_ => new ValueTask<int>(43)), "closed circuit execution");
    }

    private static async Task RateLimitReportsRetryAfter()
    {
        var timeProvider = new ManualTimeProvider();
        var shield = Shield.RateLimit(1, TimeSpan.FromSeconds(10)).WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(_ => new ValueTask<int>(42));
        var rejected = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));
        var exception = rejected.Exception as RateLimitExceededException;
        True(exception?.RetryAfter > TimeSpan.Zero, "rate-limit retry-after");
    }

    private static Task PartitionCapacityEvicts()
    {
        var partitions = new PartitionedShield<string, int>(
            static _ => Shield.For<int>().FallbackTo(42),
            new PartitionedShieldOptions { MaxPartitions = 1 });
        _ = partitions.GetShield("first");
        _ = partitions.GetShield("second");

        Equal(1, partitions.Count, "partition count");
        Equal(1L, partitions.CapacityEvictionCount, "partition eviction count");
        return Task.CompletedTask;
    }

    private static async Task OutcomeAndSyncExecutionWork()
    {
        var outcome = await Shield.Empty.ExecuteOutcomeAsync<int>(
            _ => throw new InvalidOperationException("outcome"));
        True(outcome.Exception is InvalidOperationException, "async outcome");
        Equal(42, Shield.Empty.Execute(static _ => 42), "sync execution");
    }

    private static void SynchronousExecutionAvoidsSynchronizationContextPosts()
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new ThrowingSynchronizationContext());

            var attempts = 0;
            var retry = Shield.Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = static _ => default;
            });
            Equal(42, retry.Execute(_ => ++attempts == 1
                ? throw new InvalidOperationException("retry")
                : 42), "sync retry under synchronization context");

            var queued = Shield.RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromMilliseconds(10);
                options.QueueLimit = 1;
            });
            _ = queued.Execute(static _ => 1);
            Equal(2, queued.Execute(static _ => 2), "sync queued rate limit");

            var timeout = Shield.Timeout(options =>
                options.TimeoutGenerator = static _ => new(TimeSpan.FromSeconds(1)));
            Equal(3, timeout.Execute(static _ => 3), "sync timeout generator");

            var breaker = Shield.CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDurationGenerator = static _ => new(TimeSpan.FromSeconds(1));
            });
            Equal(4, breaker.Execute(static _ => 4), "sync break-duration generator");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static async Task HttpRetryPreservesProperties()
    {
        var marker = new object();
        var handler = new PropertyRecordingHandler(marker);
        using var invoker = new HttpMessageInvoker(new ShieldDelegatingHandler(
            HttpShield.WhenTransient().Retry(1, Backoff.None))
        {
            InnerHandler = handler,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
#pragma warning disable CS0618
        request.Properties["marker"] = marker;
#pragma warning restore CS0618

        using var response = await invoker.SendAsync(request, CancellationToken.None);
        Equal(HttpStatusCode.OK, response.StatusCode, "HTTP retry response");
        Equal(2, handler.Attempts, "HTTP retry attempts");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
        }
    }

    private static void True(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"{name} failed.");
        }
    }

    private sealed class PropertyRecordingHandler(object marker) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
#pragma warning disable CS0618
            True(request.Properties.TryGetValue("marker", out var value)
                && ReferenceEquals(value, marker), "HTTP request property replay");
#pragma warning restore CS0618
            return Task.FromResult(new HttpResponseMessage(
                Attempts == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        }
    }

    private sealed class ThrowingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object state) =>
            throw new InvalidOperationException("Synchronous execution posted a continuation.");

        public override void Send(SendOrPostCallback callback, object state) =>
            throw new InvalidOperationException("Synchronous execution sent a continuation.");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new();
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, _timestamp + dueTime.Ticks);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            _timestamp += elapsed.Ticks;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireIfDue(_timestamp);
            }
        }

        private sealed class ManualTimer(
            TimerCallback callback,
            object? state,
            long dueTimestamp) : ITimer
        {
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }

            public void FireIfDue(long timestamp)
            {
                if (!_disposed && timestamp >= dueTimestamp)
                {
                    callback(state);
                }
            }
        }
    }
}
