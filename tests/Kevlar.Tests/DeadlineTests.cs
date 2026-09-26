using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

[NotInParallel]
public class DeadlineTests
{
    [Test]
    public async Task Retry_Returns_Last_Failure_When_Next_Delay_Cannot_Fit()
    {
        var time = new FakeTimeProvider();
        var attempts = 0;
        var failure = new IOException("last real failure");
        var shield = Shield.Timeout(TimeSpan.FromSeconds(1)).Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.Constant(TimeSpan.FromMilliseconds(600), jitter: Jitter.None);
            options.RespectDeadline = true;
        }).WithTimeProvider(time);
        var task = shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(failure);
        }).AsTask();

        time.Advance(TimeSpan.FromMilliseconds(600));
        var outcome = await task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(outcome.Exception).IsSameReferenceAs(failure);
    }

    [Test]
    public async Task Default_Still_Waits_Until_Outer_Timeout_Cancels_Backoff()
    {
        var time = new FakeTimeProvider();
        var attempts = new AsyncCounter("deadline default attempts");
        var task = Shield.Timeout(TimeSpan.FromSeconds(1))
            .Retry(3, Backoff.Constant(TimeSpan.FromMilliseconds(600), jitter: Jitter.None))
            .WithTimeProvider(time)
            .ExecuteOutcomeAsync<int>(_ =>
            {
                attempts.Signal();
                return ValueTask.FromException<int>(new IOException());
            }).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(600));
        await attempts.WaitForAsync(2);
        time.Advance(TimeSpan.FromMilliseconds(400));
        var outcome = await task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(attempts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_Retry_Keeps_Skipped_Result_Undisposed_And_Does_Not_Notify()
    {
        var value = new DisposableResult();
        var notifications = 0;
        var result = await Shield.For<DisposableResult>()
            .Timeout(TimeSpan.FromSeconds(1)).Retry(options =>
            {
                options.HandlesResult = static _ => true;
                options.RespectDeadline = true;
                options.Backoff = Backoff.Constant(TimeSpan.FromSeconds(1), jitter: Jitter.None);
                options.OnRetry = _ => { notifications++; return default; };
            }).WithTimeProvider(new FakeTimeProvider())
            .ExecuteAsync(_ => new ValueTask<DisposableResult>(value));
        await Assert.That(result).IsSameReferenceAs(value);
        await Assert.That(value.Disposed).IsFalse();
        await Assert.That(notifications).IsEqualTo(0);
        value.Dispose();
    }

    [Test]
    public async Task Retry_Rechecks_Budget_After_Async_Notification()
    {
        var time = new FakeTimeProvider();
        var attempts = 0;
        var value = new DisposableResult();
        var result = await Shield.For<DisposableResult>().Timeout(TimeSpan.FromSeconds(1))
            .Retry(options =>
            {
                options.RespectDeadline = true;
                options.HandlesResult = static _ => true;
                options.Backoff = Backoff.Constant(TimeSpan.FromMilliseconds(600), jitter: Jitter.None);
                options.OnRetry = async _ =>
                {
                    await Task.Yield();
                    time.Advance(TimeSpan.FromMilliseconds(500));
                };
            }).WithTimeProvider(time).ExecuteAsync(_ =>
            {
                attempts++;
                return new ValueTask<DisposableResult>(value);
            });
        await Assert.That(result).IsSameReferenceAs(value);
        await Assert.That(value.Disposed).IsFalse();
        await Assert.That(attempts).IsEqualTo(1);
        value.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hedge_Does_Not_Schedule_Delay_Beyond_Deadline(bool typed)
    {
        var clock = new FakeTimeProvider();
        var time = new TimerCountingProvider(clock);
        var calls = 0;
        var callbacks = 0;
        var completion = new TaskCompletionSource<int>();
        ValueTask<int> Operation(CancellationToken _)
        {
            calls++;
            return new ValueTask<int>(completion.Task);
        }
        var shield = Shield.Timeout(TimeSpan.FromSeconds(1)).WithTimeProvider(time);
        var task = typed
            ? shield.For<int>().Hedge(options =>
            {
                options.RespectDeadline = true;
                options.Delay = TimeSpan.FromSeconds(2);
                options.OnHedge = _ => { callbacks++; return default; };
            }).ExecuteAsync(Operation).AsTask()
            : shield.Hedge(options =>
            {
                options.RespectDeadline = true;
                options.Delay = TimeSpan.FromSeconds(2);
                options.OnHedge = _ => { callbacks++; return default; };
            }).ExecuteAsync(Operation).AsTask();
        clock.Advance(TimeSpan.FromMilliseconds(900));
        completion.SetResult(42);
        await Assert.That(await task.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(42);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(callbacks).IsEqualTo(0);
        await Assert.That(time.TimerCount).IsEqualTo(1);
    }

    [Test]
    public async Task Nested_Timeouts_And_Explicit_Children_Use_Minimum_Deadline()
    {
        var time = new FakeTimeProvider();
        var expected = time.GetUtcNow().AddSeconds(2);
        DateTimeOffset? outerDeadline = null;
        DateTimeOffset? longerDeadline = null;
        DateTimeOffset? shorterDeadline = null;
        DateTimeOffset? restoredDeadline = null;
        await Shield.Timeout(TimeSpan.FromSeconds(2)).WithTimeProvider(time)
            .ExecuteWithContextAsync(async parent =>
            {
                outerDeadline = parent.Deadline;
                await Shield.Timeout(TimeSpan.FromSeconds(5)).ExecuteWithContextAsync(parent, child =>
                {
                    longerDeadline = child.Deadline;
                    return ValueTask.CompletedTask;
                });
                await Shield.Timeout(TimeSpan.FromSeconds(1)).ExecuteWithContextAsync(parent, child =>
                {
                    shorterDeadline = child.Deadline;
                    return ValueTask.CompletedTask;
                });
                restoredDeadline = parent.Deadline;
            });
        await Assert.That(outerDeadline).IsEqualTo(expected);
        await Assert.That(longerDeadline).IsEqualTo(expected);
        await Assert.That(shorterDeadline).IsEqualTo(expected.AddSeconds(-1));
        await Assert.That(restoredDeadline).IsEqualTo(expected);
        var unbounded = await Shield.Empty.ExecuteWithContextAsync(context => new ValueTask<DateTimeOffset?>(context.Deadline));
        await Assert.That(unbounded).IsNull();
    }

    [Test]
    public async Task No_Deadline_Preserves_Retry_Allowance_And_Describe_Shows_Opt_In()
    {
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.RespectDeadline = true;
        });
        _ = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(new IOException());
        });
        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(shield.ToString()).Contains("deadline-aware");
        await Assert.That(Shield.Hedge(options => options.RespectDeadline = true).ToString()).Contains("deadline-aware");
        await Assert.That(new RetryOptions().RespectDeadline).IsFalse();
        await Assert.That(new RetryOptions<int>().RespectDeadline).IsFalse();
        await Assert.That(new HedgeOptions().RespectDeadline).IsFalse();
        await Assert.That(new HedgeOptions<int>().RespectDeadline).IsFalse();
    }

    [Test]
    public async Task Inner_Timeout_Restores_Outer_Deadline_Before_Retry_Callback()
    {
        var time = new FakeTimeProvider();
        DateTimeOffset? callbackDeadline = null;
        DateTimeOffset? operationDeadline = null;
        var task = Shield.Timeout(TimeSpan.FromSeconds(2)).Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry =>
            {
                callbackDeadline = retry.Context.Deadline;
                retry.SuppressAdditionalAttempts();
                return default;
            };
        }).Timeout(TimeSpan.FromSeconds(1)).WithTimeProvider(time)
            .ExecuteWithContextAsync<int>(context =>
            {
                operationDeadline = context.Deadline;
                return ValueTask.FromException<int>(new IOException());
            }).AsTask();
        await Assert.That(async () => await task).Throws<IOException>();
        await Assert.That(operationDeadline).IsEqualTo(time.GetUtcNow().AddSeconds(1));
        await Assert.That(callbackDeadline).IsEqualTo(time.GetUtcNow().AddSeconds(2));
    }

    [Test]
    public async Task Definition_Binds_Both_Deadline_Options()
    {
        var retry = new Kevlar.Extensions.DependencyInjection.ShieldDefinition
        {
            Retry = new() { RespectDeadline = true },
        };
        var hedge = new Kevlar.Extensions.DependencyInjection.ShieldDefinition
        {
            Hedge = new() { RespectDeadline = true },
        };
        await Assert.That(retry.Build().ToString()).Contains("deadline-aware");
        await Assert.That(hedge.Build().ToString()).Contains("deadline-aware");
    }

    [Test]
    public async Task Generated_Retry_Delay_Uses_Final_Capped_Value()
    {
        var time = new FakeTimeProvider();
        var start = time.GetUtcNow();
        var attempts = 0;
        var task = Shield.Timeout(TimeSpan.FromSeconds(1)).Retry(options =>
        {
            options.RespectDeadline = true;
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.MaxDelay = TimeSpan.FromMilliseconds(600);
            options.DelayGenerator = async _ =>
            {
                await Task.Yield();
                return TimeSpan.FromSeconds(10);
            };
            options.OnRetry = _ => { time.Advance(TimeSpan.FromMilliseconds(400)); return default; };
        }).WithTimeProvider(time).ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(new IOException());
        }).AsTask();
        // The capped 600 ms delay fits before the hook, then equals the remaining budget.
        var outcome = await task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(outcome.Exception).IsTypeOf<IOException>();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(time.GetUtcNow()).IsEqualTo(start.AddMilliseconds(400));
    }

    [Test]
    public async Task Caller_Cancellation_Wins_Over_Deadline_Skip()
    {
        using var cancellation = new CancellationTokenSource();
        var outcome = await Shield.Timeout(TimeSpan.FromSeconds(1)).Retry(options =>
        {
            options.RespectDeadline = true;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ =>
            {
                cancellation.Cancel();
                return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(2));
            };
        }).WithTimeProvider(new FakeTimeProvider())
            .ExecuteOutcomeAsync<int>(_ => ValueTask.FromException<int>(new IOException()), cancellation.Token);
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
    }

    [Test]
    public async Task Hedge_Forks_Inherit_Deadline_And_Keep_Existing_Contenders()
    {
        var time = new FakeTimeProvider();
        var expected = time.GetUtcNow().AddSeconds(1);
        var deadlines = new List<DateTimeOffset?>();
        var primary = new TaskCompletionSource<int>();
        var result = await Shield.Timeout(TimeSpan.FromSeconds(1)).Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.RespectDeadline = true;
        }).WithTimeProvider(time).ExecuteWithContextAsync(context =>
        {
            deadlines.Add(context.Deadline);
            if (context.AttemptNumber == 0)
            {
                return new ValueTask<int>(primary.Task);
            }
            primary.SetResult(42);
            return ValueTask.FromException<int>(new IOException());
        });
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(deadlines).IsEquivalentTo(new DateTimeOffset?[] { expected, expected });
    }

    [Test]
    public async Task Deadline_Remains_Stable_When_Wall_Clock_Moves()
    {
        var clock = new FakeTimeProvider();
        var time = new AdjustableUtcProvider(clock);
        var expected = clock.GetUtcNow().AddSeconds(2);
        await Shield.Timeout(TimeSpan.FromSeconds(2)).WithTimeProvider(time)
            .ExecuteWithContextAsync(async context =>
            {
                time.UtcOffset = TimeSpan.FromHours(1);
                await Assert.That(context.Deadline).IsEqualTo(expected);
                await Shield.Timeout(TimeSpan.FromSeconds(1)).ExecuteWithContextAsync(context, async child =>
                {
                    // A forward wall-clock change must not extend the enclosing UTC deadline.
                    await Assert.That(child.Deadline).IsEqualTo(expected);
                });
                time.UtcOffset = TimeSpan.FromHours(-1);
                await Assert.That(context.Deadline).IsEqualTo(expected);
            });
    }

    [Test]
    public async Task Deadline_Clamps_At_Maximum_Utc_Value()
    {
        var clock = new FakeTimeProvider();
        var time = new AdjustableUtcProvider(clock)
        {
            UtcOffset = DateTimeOffset.MaxValue.AddSeconds(-1) - clock.GetUtcNow(),
        };
        var deadline = await Shield.Timeout(TimeSpan.FromSeconds(2)).WithTimeProvider(time)
            .ExecuteWithContextAsync(context => new ValueTask<DateTimeOffset?>(context.Deadline));
        await Assert.That(deadline).IsEqualTo(DateTimeOffset.MaxValue);
    }

    private sealed class AdjustableUtcProvider(FakeTimeProvider clock) : TimeProvider
    {
        public TimeSpan UtcOffset { get; set; }
        public int UtcReads { get; private set; }
        public override long TimestampFrequency => clock.TimestampFrequency;
        public override long GetTimestamp() => clock.GetTimestamp();
        public override DateTimeOffset GetUtcNow()
        {
            UtcReads++;
            return clock.GetUtcNow() + UtcOffset;
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            clock.CreateTimer(callback, state, dueTime, period);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Token_Only_Default_Pipeline_Does_Not_Read_Utc_Clock(bool synchronous)
    {
        var time = new AdjustableUtcProvider(new FakeTimeProvider());
        var shield = Shield.Timeout(TimeSpan.FromSeconds(2)).Retry(1, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(1)).WithTimeProvider(time);
        var result = synchronous
            ? shield.Execute(static _ => 42)
            : await shield.ExecuteAsync(static _ => new ValueTask<int>(42));
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(time.UtcReads).IsEqualTo(0);
    }

    [Test]
    public async Task Context_Aware_Handling_Receives_Deadline_With_Option_Off()
    {
        var time = new FakeTimeProvider();
        DateTimeOffset? observed = null;
        var result = await Shield.Timeout(TimeSpan.FromSeconds(1)).Retry(options =>
        {
            options.MaxRetries = 1;
            options.HandlesExceptionContext = handling =>
            {
                observed = handling.Context.Deadline;
                return false;
            };
        }).WithTimeProvider(time).ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        await Assert.That(result.Exception).IsTypeOf<IOException>();
        await Assert.That(observed).IsEqualTo(time.GetUtcNow().AddSeconds(1));
    }

    [Test]
    public async Task Custom_Strategy_Receives_Deadline_In_Token_Only_Execution()
    {
        var time = new FakeTimeProvider();
        var observer = new DeadlineObserver();
        await Shield.Timeout(TimeSpan.FromSeconds(1)).Use(observer).WithTimeProvider(time)
            .ExecuteAsync(static _ => ValueTask.CompletedTask);
        await Assert.That(observer.Deadline).IsEqualTo(time.GetUtcNow().AddSeconds(1));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Global_Diagnostics_Observe_Deadline_In_Token_Only_Execution(bool enrichment)
    {
        var time = new FakeTimeProvider();
        var observer = new DiagnosticDeadlineObserver();
        using var meter = new System.Diagnostics.Metrics.MeterListener();
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Kevlar" && instrument.Name == "kevlar.retries")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meter.SetMeasurementEventCallback<long>(static (_, _, _, _) => { });
        meter.Start();
        using var subscription = enrichment
            ? KevlarDiagnostics.AddMetricEnricher(observer)
            : KevlarDiagnostics.Listen(observer);
        var attempts = 0;
        var result = await Shield.Timeout(TimeSpan.FromSeconds(1)).Retry(1, Backoff.None)
            .WithTimeProvider(time).ExecuteAsync<int>(_ => ++attempts == 1
                ? ValueTask.FromException<int>(new IOException())
                : new ValueTask<int>(42));
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observer.Deadline).IsEqualTo(time.GetUtcNow().AddSeconds(1));
    }

    private sealed class DiagnosticDeadlineObserver : KevlarMetricEnricher, IKevlarTelemetryListener
    {
        public DateTimeOffset? Deadline { get; private set; }

        public void OnEvent(in KevlarTelemetryEvent telemetryEvent)
        {
            if (telemetryEvent.Context.Deadline is { } deadline)
            {
                Deadline = deadline;
            }
        }

        public override void Enrich(in KevlarMetricEnrichmentContext context)
        {
            if (context.Context?.Deadline is { } deadline)
            {
                Deadline = deadline;
            }
        }
    }

    private sealed class DeadlineObserver : Strategy
    {
        public DateTimeOffset? Deadline { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
        {
            Deadline = context.Deadline;
            return next.InvokeAsync(context);
        }
    }

    private sealed class TimerCountingProvider(FakeTimeProvider clock) : TimeProvider
    {
        public int TimerCount { get; private set; }
        public override long TimestampFrequency => clock.TimestampFrequency;
        public override long GetTimestamp() => clock.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerCount++;
            return clock.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class DisposableResult : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
