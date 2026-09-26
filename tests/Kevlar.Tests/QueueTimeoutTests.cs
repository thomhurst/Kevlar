using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

[NotInParallel]
public class QueueTimeoutTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Expiry_Rejects_And_Refunds_The_Reservation(bool rateLimit)
    {
        var time = new FakeTimeProvider();
        var reasons = new List<string?>();
        var shield = Create(rateLimit, time, TimeSpan.FromMilliseconds(250), reasons.Add);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = shield.ExecuteAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        try
        {
            var invoked = false;
            var queued = shield.ExecuteOutcomeAsync(_ =>
            {
                invoked = true;
                return new ValueTask<int>(2);
            }).AsTask();
            time.Advance(TimeSpan.FromMilliseconds(250));
            var outcome = await queued.WaitAsync(TestHelpers.DefaultTimeout);
            await Assert.That(outcome.Exception?.GetType()).IsEqualTo(rateLimit
                ? typeof(RateLimitExceededException) : typeof(ConcurrencyLimitExceededException));
            await Assert.That(invoked).IsFalse();
            await Assert.That(reasons.Single()).IsEqualTo("queue_timeout");
            await Assert.That(Queued(shield)).IsEqualTo(0);

            release.TrySetResult(1);
            await active.WaitAsync(TestHelpers.DefaultTimeout);
            time.Advance(TimeSpan.FromMilliseconds(750));
            var replacement = await shield.ExecuteAsync(_ => new ValueTask<int>(3));
            await Assert.That(replacement).IsEqualTo(3);
        }
        finally
        {
            release.TrySetResult(1);
            await active.WaitAsync(TestHelpers.DefaultTimeout);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Admission_Stops_The_Queue_Timer_And_Preserves_The_Caller_Token(bool rateLimit)
    {
        var time = new FakeTimeProvider();
        using var caller = new CancellationTokenSource();
        var shield = Create(rateLimit, time, TimeSpan.FromSeconds(2));
        var releaseFirst = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = shield.ExecuteAsync(_ => new ValueTask<int>(releaseFirst.Task)).AsTask();
        var second = shield.ExecuteAsync(token =>
        {
            entered.TrySetResult(token);
            return new ValueTask<int>(releaseSecond.Task);
        }, caller.Token).AsTask();
        try
        {
            releaseFirst.TrySetResult(1);
            time.Advance(TimeSpan.FromSeconds(1));
            var token = await entered.Task.WaitAsync(TestHelpers.DefaultTimeout);
            time.Advance(TimeSpan.FromSeconds(10));
            await Assert.That(token).IsEqualTo(caller.Token);
            await Assert.That(token.IsCancellationRequested).IsFalse();
            await Assert.That(second.IsCompleted).IsFalse();
            releaseSecond.TrySetResult(2);
            await Assert.That(await second.WaitAsync(TestHelpers.DefaultTimeout)).IsEqualTo(2);
        }
        finally
        {
            releaseFirst.TrySetResult(1);
            releaseSecond.TrySetResult(2);
            await Task.WhenAll(first, second).WaitAsync(TestHelpers.DefaultTimeout);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Caller_Cancellation_Wins_Over_Queue_Expiry(bool rateLimit)
    {
        var time = new ControlledTimeProvider();
        using var caller = new CancellationTokenSource();
        var reasons = new List<string?>();
        var shield = Create(rateLimit, time, TimeSpan.FromSeconds(1), reasons.Add);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = shield.ExecuteAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        try
        {
            var queued = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(2), caller.Token).AsTask();
            var dispatchedTimer = time.QueueTimerCallback(0);
            caller.Cancel();
            time.Advance(TimeSpan.FromSeconds(1));
            var outcome = await queued.WaitAsync(TestHelpers.DefaultTimeout);
            // A callback dispatched before disposal may arrive after the waiter has drained.
            dispatchedTimer.Fire();
            await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
            await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken).IsEqualTo(caller.Token);
            await Assert.That(reasons.Count).IsEqualTo(0);
            await Assert.That(Queued(shield)).IsEqualTo(0);
        }
        finally
        {
            release.TrySetResult(1);
            await active.WaitAsync(TestHelpers.DefaultTimeout);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Synchronous_Queue_Wait_Expires_On_The_Configured_TimeProvider(bool rateLimit)
    {
        var time = new ControlledTimeProvider();
        var shield = Create(rateLimit, time, TimeSpan.FromSeconds(1));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = shield.ExecuteAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        var queued = Task.Factory.StartNew(() => shield.ExecuteOutcome(_ => 2),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await time.WaitForTimersAsync(rateLimit ? 2 : 1);
            time.Advance(TimeSpan.FromSeconds(1));
            time.FireTimer(0);
            var outcome = await queued.WaitAsync(TestHelpers.DefaultTimeout);
            await Assert.That(outcome.Exception?.GetType()).IsEqualTo(rateLimit
                ? typeof(RateLimitExceededException) : typeof(ConcurrencyLimitExceededException));
            await Assert.That(Queued(shield)).IsEqualTo(0);
        }
        finally
        {
            release.TrySetResult(1);
            await active.WaitAsync(TestHelpers.DefaultTimeout);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Queue_Expiry_Reports_Reason_In_Telemetry_And_Metrics(bool rateLimit)
    {
        var time = new FakeTimeProvider();
        var telemetry = new Listener();
        using var subscription = KevlarDiagnostics.Listen(telemetry);
        using var meter = new MeterListener();
        var rejectionReasons = new ConcurrentQueue<string?>();
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == KevlarDiagnostics.MeterName && instrument.Name == "kevlar.rejections")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meter.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "kevlar.rejection.reason")
                {
                    rejectionReasons.Enqueue(tag.Value as string);
                }
            }
        });
        meter.Start();
        var shield = Create(rateLimit, time, TimeSpan.FromMilliseconds(250)).WithName("queue-timeout-telemetry");
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = shield.ExecuteAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        try
        {
            var queued = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(2)).AsTask();
            time.Advance(TimeSpan.FromMilliseconds(250));
            await queued.WaitAsync(TestHelpers.DefaultTimeout);
            await Assert.That(telemetry.Reasons.Single()).IsEqualTo("queue_timeout");
            await Assert.That(rejectionReasons.Single()).IsEqualTo("queue_timeout");
        }
        finally
        {
            release.TrySetResult(1);
            await active.WaitAsync(TestHelpers.DefaultTimeout);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Elapsed_Deadline_Rejects_Even_When_Timeout_Callback_Has_Not_Run(bool rateLimit)
    {
        var time = new ControlledTimeProvider();
        var shield = Create(rateLimit, time, TimeSpan.FromMilliseconds(250));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = shield.ExecuteAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        var queued = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(2)).AsTask();
        time.Advance(TimeSpan.FromSeconds(1));
        release.TrySetResult(1);
        if (rateLimit)
        {
            // Wake the permit delay without dispatching the queue-timeout callback.
            time.FireTimer(1);
        }
        var outcome = await queued.WaitAsync(TestHelpers.DefaultTimeout);
        await active.WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(outcome.Exception is ExecutionRejectedException).IsTrue();
        await Assert.That(Queued(shield)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Uncontended_Admission_Does_Not_Create_A_Queue_Timer(bool rateLimit)
    {
        var time = new ControlledTimeProvider();
        var shield = Create(rateLimit, time, TimeSpan.FromSeconds(1));
        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(42))).IsEqualTo(42);
        await Assert.That(time.TimerCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Timer_Creation_Failure_Does_Not_Leak_Queue_Capacity(bool rateLimit)
    {
        var time = new FailingTimerProvider();
        var shield = Create(rateLimit, time, TimeSpan.FromSeconds(1));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = shield.ExecuteAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        try
        {
            var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(2));
            await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(Queued(shield)).IsEqualTo(0);
            release.TrySetResult(1);
            await active.WaitAsync(TestHelpers.DefaultTimeout);
            time.Advance(TimeSpan.FromSeconds(1));
            await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(3))).IsEqualTo(3);
        }
        finally
        {
            release.TrySetResult(1);
            await active.WaitAsync(TestHelpers.DefaultTimeout);
        }
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-1d)]
    [Arguments(4294967295d)]
    public async Task Invalid_Queue_Timeout_Is_Rejected(double milliseconds)
    {
        var timeout = TimeSpan.FromMilliseconds(milliseconds);
        await Assert.That(() => Shield.ConcurrencyLimit(options => options.QueueTimeout = timeout))
            .Throws<KevlarConfigurationException>();
        await Assert.That(() => Shield.RateLimit(options => options.QueueTimeout = timeout))
            .Throws<KevlarConfigurationException>();
    }

    [Test]
    public async Task Configuration_And_Descriptors_Preserve_Queue_Timeout()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConcurrencyLimit:QueueLimit"] = "2",
            ["ConcurrencyLimit:QueueTimeout"] = "00:00:00.250",
            ["RateLimit:QueueLimit"] = "3",
            ["RateLimit:QueueTimeout"] = "00:00:00.500",
        }).Build();
        var services = new ServiceCollection();
        services.AddShield("queue", configuration);
        services.AddShield<int>("typed-queue", configuration);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();
        foreach (var descriptor in new[] { registry.GetShield("queue").GetDescriptor(), registry.GetShield<int>("typed-queue").GetDescriptor() })
        {
            var concurrency = descriptor.AssertContainsSingle<ConcurrencyLimitStrategyDescriptor>();
            var rate = descriptor.AssertContainsSingle<RateLimitStrategyDescriptor>();
            await Assert.That(concurrency.QueueTimeout).IsEqualTo(TimeSpan.FromMilliseconds(250));
            await Assert.That(rate.QueueTimeout).IsEqualTo(TimeSpan.FromMilliseconds(500));
            await Assert.That(concurrency.Description).Contains("queue 2/250ms");
            await Assert.That(rate.Description).Contains("queue 3/500ms");
        }
        await Assert.That(Shield.ConcurrencyLimit(1, queueLimit: 2).ToString()).IsEqualTo("ConcurrencyLimit(1, queue 2)");
    }

    private static Shield Create(bool rateLimit, TimeProvider time, TimeSpan? timeout, Action<string?>? onRejected = null)
    {
        var shield = rateLimit
            ? Shield.RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(1);
                options.QueueLimit = 2;
                options.QueueTimeout = timeout;
                options.OnRejected = rejection => { onRejected?.Invoke(rejection.Reason); return default; };
            })
            : Shield.ConcurrencyLimit(options =>
            {
                options.MaxConcurrency = 1;
                options.QueueLimit = 2;
                options.QueueTimeout = timeout;
                options.OnRejected = rejection => { onRejected?.Invoke(rejection.Reason); return default; };
            });
        return shield.WithTimeProvider(time);
    }

    private static int Queued(Shield shield) => shield.GetStateSnapshot().Strategies.Single() switch
    {
        ConcurrencyLimitStateSnapshot concurrency => concurrency.QueuedExecutions,
        RateLimitStateSnapshot rate => rate.QueuedExecutions,
        _ => throw new InvalidOperationException(),
    };

    private sealed class Listener : IKevlarTelemetryListener
    {
        public ConcurrentQueue<string?> Reasons { get; } = new();
        public void OnEvent(in KevlarTelemetryEvent telemetryEvent)
        {
            if (telemetryEvent.ShieldName == "queue-timeout-telemetry" && telemetryEvent.EventName == "rejection")
            {
                Reasons.Enqueue(telemetryEvent.RejectionReason);
            }
        }
    }

    private sealed class FailingTimerProvider : FakeTimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new InvalidOperationException("Timer creation failed.");
    }
}
