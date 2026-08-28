using Grpc.Core;
using Kevlar.Extensions.Grpc;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class GrpcShieldTests
{
    [Test]
    [Arguments("2500", 2500)]
    [Arguments("0001", 1)]
    public async Task RetryAfter_Uses_Server_Pushback_Delay(string value, int expectedMilliseconds)
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var attemptsStarted = new AsyncCounter("gRPC pushback attempts");
        var shield = GrpcShield.WhenTransient()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            })
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(_ =>
        {
            attemptsStarted.Signal();
            return ++attempts == 1
                ? ValueTask.FromException<int>(CreateException(value))
                : new ValueTask<int>(42);
        }).AsTask();

        await attemptsStarted.WaitForAsync(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(expectedMilliseconds - 1));
        await Assert.That(attempts).IsEqualTo(1);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await attemptsStarted.WaitForAsync(2);

        await Assert.That(await execution).IsEqualTo(42);
    }

    [Test]
    [Arguments("-1")]
    [Arguments("invalid")]
    public async Task RetryAfter_Suppresses_Invalid_Or_Negative_Pushback(string value)
    {
        var attempts = 0;
        var shield = GrpcShield.WhenTransient().Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.DelayGenerator = GrpcShield.RetryAfter;
        });

        var exception = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw CreateException(value);
        })).Throws<RpcException>();

        await Assert.That(exception!.Trailers.GetValue("grpc-retry-pushback-ms")).IsEqualTo(value);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task RetryAfter_Uses_Pushback_From_A_Handled_Inner_Exception()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var attemptsStarted = new AsyncCounter("wrapped gRPC pushback attempts");
        var shield = Shield.WhenInner<RpcException>(GrpcShield.IsTransient)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            })
            .WithTimeProvider(timeProvider);
        var execution = shield.ExecuteAsync<int>(_ =>
        {
            attemptsStarted.Signal();
            return ++attempts == 1
                ? ValueTask.FromException<int>(
                    new InvalidOperationException("wrapper", CreateException("2500")))
                : new ValueTask<int>(42);
        }).AsTask();

        await attemptsStarted.WaitForAsync(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(2499));
        await Assert.That(attempts).IsEqualTo(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await attemptsStarted.WaitForAsync(2);

        await Assert.That(await execution).IsEqualTo(42);
    }

    [Test]
    public async Task RetryAfter_Suppresses_Pushback_From_A_Handled_Aggregate_Branch()
    {
        var attempts = 0;
        var shield = Shield.WhenInner<RpcException>(GrpcShield.IsTransient)
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            });
        var exception = new AggregateException(
            new InvalidOperationException("unrelated"),
            CreateException("-1"));

        _ = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw exception;
        })).Throws<AggregateException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task RetryAfter_Uses_The_Handled_Rpc_Branch_In_An_Aggregate()
    {
        var attempts = 0;
        var shield = Shield.WhenInner<RpcException>(GrpcShield.IsTransient)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            });
        var exception = new AggregateException(
            new RpcException(
                new Status(StatusCode.InvalidArgument, "not transient"),
                new Metadata { { "grpc-retry-pushback-ms", "invalid" } }),
            CreateException(pushback: null));

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? ValueTask.FromException<int>(exception)
                : new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task IsTransient_Accepts_Missing_And_NonNegative_Pushback()
    {
        await Assert.That(GrpcShield.IsTransient(CreateException(pushback: null))).IsTrue();
        await Assert.That(GrpcShield.IsTransient(CreateException("0"))).IsTrue();
        await Assert.That(GrpcShield.IsTransient(CreateException("2147483647"))).IsTrue();
        await Assert.That(GrpcShield.IsTransient(CreateException("-1"))).IsTrue();
        await Assert.That(GrpcShield.IsTransient(CreateException("invalid"))).IsTrue();
        await Assert.That(GrpcShield.IsTransient(
            new RpcException(new Status(StatusCode.InvalidArgument, "not transient")))).IsFalse();
        await Assert.That(GrpcShield.IsTransient((RpcException?)null)).IsFalse();
    }

    [Test]
    public async Task RetryAfter_Suppresses_Duplicate_Pushback_Trailers()
    {
        var attempts = 0;
        var trailers = new Metadata
        {
            { "grpc-retry-pushback-ms", "1" },
            { "grpc-retry-pushback-ms", "2" },
        };
        var exception = new RpcException(new Status(StatusCode.Unavailable, "transient"), trailers);
        var shield = GrpcShield.WhenTransient().Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.DelayGenerator = GrpcShield.RetryAfter;
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw exception;
        })).Throws<RpcException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task RetryAfter_Suppression_Still_Records_The_Failure_In_A_CircuitBreaker()
    {
        var attempts = 0;
        var shield = GrpcShield.WhenTransient()
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            })
            .CircuitBreaker(
                consecutiveFailures: 1,
                breakDuration: TimeSpan.FromMinutes(1));

        _ = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw CreateException("-1");
        })).Throws<RpcException>();
        _ = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            return new ValueTask<int>(42);
        })).Throws<CircuitOpenException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetryAfter_Suppression_Prevents_Outer_Hedge_Attempts(bool completeAsynchronously)
    {
        var attempts = 0;
        var shield = GrpcShield.WhenTransient()
            .Hedge(2, Timeout.InfiniteTimeSpan)
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            });

        await Assert.That(async () => await shield.ExecuteAsync<int>(async _ =>
        {
            attempts++;
            if (completeAsynchronously)
            {
                await Task.Yield();
            }

            throw CreateException("-1");
        })).Throws<RpcException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task RetryAfter_Suppression_Stops_Later_Zero_Delay_Hedges()
    {
        var attempts = 0;
        var releasePrimary = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstHedgeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = GrpcShield.WhenTransient()
            .Hedge(2, TimeSpan.Zero)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            });

        var execution = shield.ExecuteAsync<int>(async _ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                await releasePrimary.Task;
                return 42;
            }

            firstHedgeStarted.TrySetResult();
            throw CreateException("-1");
        }).AsTask();

        await firstHedgeStarted.Task;
        releasePrimary.TrySetResult();

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task RetryAfter_Suppression_Preserves_An_Already_Running_Hedge()
    {
        var attempts = 0;
        var firstAttemptCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = GrpcShield.WhenTransient()
            .Hedge(1, TimeSpan.Zero)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            });

        var result = await shield.ExecuteAsync<int>(async _ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                firstAttemptCompleted.TrySetResult();
                throw CreateException("-1");
            }

            await firstAttemptCompleted.Task;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            return 42;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task RetryAfter_Suppression_Reaches_An_Already_Running_Hedge()
    {
        var attempts = 0;
        var suppressionReachedPendingAttempt = false;
        var secondAttemptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAttemptJudged = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = GrpcShield.WhenTransient()
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = TimeSpan.Zero;
                options.HandlesExceptionContext = _ =>
                {
                    firstAttemptJudged.TrySetResult();
                    return true;
                };
            })
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            });

        await Assert.That(async () => await shield.ExecuteWithContextAsync<int>(async context =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                await secondAttemptStarted.Task;
                throw CreateException("-1");
            }

            if (attempt == 2)
            {
                secondAttemptStarted.TrySetResult();
                await firstAttemptJudged.Task;
                for (var i = 0; i < 100 && !context.Properties.SuppressAdditionalAttempts; i++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1));
                }

                suppressionReachedPendingAttempt = context.Properties.SuppressAdditionalAttempts;
                throw CreateException(null);
            }

            return 42;
        })).Throws<RpcException>();

        await Assert.That(suppressionReachedPendingAttempt).IsTrue();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task RetryAfter_Suppression_Stops_An_Already_Delayed_Hedge_Retry()
    {
        var timeProvider = new ControlledTimeProvider();
        var attempts = 0;
        KevlarContext? delayedContext = null;
        var firstAttemptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = GrpcShield.WhenTransient()
            .Hedge(1, TimeSpan.Zero)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.Constant(TimeSpan.FromMinutes(1));
                options.DelayGenerator = GrpcShield.RetryAfter;
            })
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteWithContextAsync<int>(async context =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                delayedContext = context;
                firstAttemptStarted.TrySetResult();
                throw CreateException(null);
            }

            if (attempt == 2)
            {
                await firstAttemptStarted.Task;
                throw CreateException("-1");
            }

            return 42;
        }).AsTask();

        await timeProvider.WaitForTimersAsync(1);
        for (var i = 0;
             i < 100 && delayedContext?.Properties.SuppressAdditionalAttempts is not true;
             i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1));
        }

        await Assert.That(delayedContext!.Properties.SuppressAdditionalAttempts).IsTrue();
        timeProvider.FireTimer(0);

        _ = await Assert.That(async () => await execution).Throws<RpcException>();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task RetryAfter_Suppression_Reaches_An_Active_Nested_Child()
    {
        var timeProvider = new ControlledTimeProvider();
        var attempts = 0;
        KevlarContext? delayedChild = null;
        var inner = GrpcShield.WhenTransient()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.Constant(TimeSpan.FromMinutes(1));
                options.DelayGenerator = GrpcShield.RetryAfter;
            });
        var outer = GrpcShield.WhenTransient()
            .Hedge(1, TimeSpan.Zero)
            .WithTimeProvider(timeProvider);

        var execution = outer.ExecuteWithContextAsync<int>(parent =>
            inner.ExecuteWithContextAsync(parent, async child =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    delayedChild = child;
                    throw CreateException(null);
                }

                if (attempt == 2)
                {
                    await timeProvider.WaitForTimersAsync(1);
                    throw CreateException("-1");
                }

                return 42;
            })).AsTask();

        await timeProvider.WaitForTimersAsync(1);
        for (var i = 0;
             i < 100 && delayedChild?.Properties.SuppressAdditionalAttempts is not true;
             i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1));
        }

        await Assert.That(delayedChild!.Properties.SuppressAdditionalAttempts).IsTrue();
        timeProvider.FireTimer(0);

        _ = await Assert.That(async () => await execution).Throws<RpcException>();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 0)]
    [Arguments(false, 1)]
    [Arguments(true, 1)]
    public async Task RetryAfter_Inspects_Terminal_Retry_Before_Outer_Hedge(
        bool completeAsynchronously,
        int maxRetries)
    {
        var attempts = 0;
        var shield = GrpcShield.WhenTransient()
            .Hedge(2, Timeout.InfiniteTimeSpan)
            .Retry(options =>
            {
                options.MaxRetries = maxRetries;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            });

        await Assert.That(async () => await shield.ExecuteAsync<int>(async _ =>
        {
            attempts++;
            if (completeAsynchronously)
            {
                await Task.Yield();
            }

            throw CreateException(attempts <= maxRetries ? null : "-1");
        })).Throws<RpcException>();

        await Assert.That(attempts).IsEqualTo(maxRetries + 1);
    }

    [Test]
    public async Task Wrapped_RetryAfter_Inspects_The_Terminal_Retry()
    {
        var attempts = 0;
        var shield = GrpcShield.WhenTransient()
            .Hedge(1, Timeout.InfiniteTimeSpan)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = retry => GrpcShield.RetryAfter(retry);
            });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw CreateException(attempts == 1 ? null : "-1");
        })).Throws<RpcException>();

        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task RetryAfter_Does_Not_Suppress_Outer_Hedge_When_Inner_Retry_Does_Not_Handle()
    {
        var attempts = 0;
        var shield = GrpcShield.WhenTransient()
            .Hedge(1, Timeout.InfiniteTimeSpan)
            .Retry(options =>
            {
                options.MaxRetries = 0;
                options.HandlesException = static _ => false;
                options.DelayGenerator = GrpcShield.RetryAfter;
            });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw CreateException("-1");
        })).Throws<RpcException>();

        await Assert.That(attempts).IsEqualTo(2);
    }

    private static RpcException CreateException(string? pushback)
    {
        var trailers = new Metadata();
        if (pushback is not null)
        {
            trailers.Add("grpc-retry-pushback-ms", pushback);
        }

        return new RpcException(new Status(StatusCode.Unavailable, "transient"), trailers);
    }
}
