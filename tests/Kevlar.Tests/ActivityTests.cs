using System.Collections.Concurrent;
using System.Diagnostics;

namespace Kevlar.Tests;

[NotInParallel]
public class ActivityTests
{
    [Test]
    [Arguments("async")]
    [Arguments("async-state")]
    [Arguments("async-outcome")]
    [Arguments("async-context")]
    [Arguments("sync")]
    [Arguments("sync-state")]
    [Arguments("sync-outcome")]
    [Arguments("sync-context")]
    [Arguments("typed-async")]
    [Arguments("typed-sync")]
    public async Task Public_Execution_Forms_Create_One_Execution_Span(string form)
    {
        using var capture = new Capture();
        using var parent = new Activity("application").SetIdFormat(ActivityIdFormat.W3C).Start();
        var shield = Shield.Empty.WithName("catalog");
        var result = form switch
        {
            "async" => await shield.ExecuteAsync(static _ => new ValueTask<int>(42)),
            "async-state" => await shield.ExecuteAsync(42, static (state, _) => new ValueTask<int>(state)),
            "async-outcome" => (await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42))).Result,
            "async-context" => await shield.ExecuteWithContextAsync(static _ => new ValueTask<int>(42)),
            "sync" => shield.Execute(static _ => 42),
            "sync-state" => shield.Execute(42, static (state, _) => state),
            "sync-outcome" => shield.ExecuteOutcome(static _ => 42).Result,
            "sync-context" => shield.ExecuteWithContext(static _ => 42),
            "typed-async" => await Shield<int>.Empty.WithName("catalog").ExecuteAsync(static _ => new ValueTask<int>(42)),
            "typed-sync" => Shield<int>.Empty.WithName("catalog").Execute(static _ => 42),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        await Assert.That(result).IsEqualTo(42);
        var span = capture.Stopped.Single();
        await Assert.That(span.OperationName).IsEqualTo("kevlar.execute");
        await Assert.That(span.Kind).IsEqualTo(ActivityKind.Internal);
        await Assert.That(span.ParentSpanId).IsEqualTo(parent.SpanId);
        await Assert.That(span.GetTagItem("kevlar.shield.name")).IsEqualTo("catalog");
        await Assert.That(span.GetTagItem("kevlar.execution.outcome")).IsEqualTo("success");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
    }

    [Test]
    public async Task Pending_Executions_Restore_Caller_Activity_And_Are_Siblings()
    {
        using var capture = new Capture();
        using var parent = new Activity("application").SetIdFormat(ActivityIdFormat.W3C).Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Shield.Empty.ExecuteAsync(async _ => { await release.Task; return Activity.Current!.SpanId; });
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
        var second = Shield.Empty.ExecuteAsync(async _ => { await release.Task; return Activity.Current!.SpanId; });
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
        release.SetResult();
        var firstId = await first;
        var secondId = await second;
        await Assert.That(firstId).IsNotEqualTo(secondId);
        await Assert.That(capture.Stopped.Count).IsEqualTo(2);
        await Assert.That(capture.Stopped.All(span => span.ParentSpanId == parent.SpanId)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Nested_Context_Executions_Have_Parent_Child_Spans(bool synchronous)
    {
        using var capture = new Capture();
        var outer = Shield.Empty.WithName("outer");
        var inner = Shield.Empty.WithName("inner");
        if (synchronous)
        {
            _ = outer.ExecuteWithContext(context => inner.ExecuteWithContext(context, static _ => 42));
        }
        else
        {
            _ = await outer.ExecuteWithContextAsync(context => inner.ExecuteWithContextAsync(context, static _ => new ValueTask<int>(42)));
        }
        var outerSpan = capture.Stopped.Single(span => Equals(span.GetTagItem("kevlar.shield.name"), "outer"));
        var innerSpan = capture.Stopped.Single(span => Equals(span.GetTagItem("kevlar.shield.name"), "inner"));
        await Assert.That(innerSpan.ParentSpanId).IsEqualTo(outerSpan.SpanId);
    }

    [Test]
    public async Task Async_Retry_Creates_Attempt_Children_And_Records_Recovery()
    {
        using var capture = new Capture();
        var calls = 0;
        var result = await Shield.Retry(1, Backoff.None).WithName("retry").ExecuteAsync(async _ =>
        {
            await Task.Yield();
            if (++calls == 1)
            {
                throw new InvalidOperationException("private-message");
            }
            return 42;
        });
        await Assert.That(result).IsEqualTo(42);
        var execution = capture.Stopped.Single(span => span.OperationName == "kevlar.execute");
        var attempts = capture.Stopped.Where(span => span.OperationName == "kevlar.attempt")
            .OrderBy(span => (int)span.GetTagItem("kevlar.attempt.number")!).ToArray();
        await Assert.That(attempts.Length).IsEqualTo(2);
        await Assert.That(attempts.All(span => span.ParentSpanId == execution.SpanId)).IsTrue();
        await Assert.That(attempts[0].GetTagItem("kevlar.attempt.number")).IsEqualTo(0);
        await Assert.That(attempts[1].GetTagItem("kevlar.attempt.number")).IsEqualTo(1);
        await Assert.That(attempts[0].GetTagItem("kevlar.strategy.name")).IsEqualTo("Retry");
        await Assert.That(attempts[0].Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(attempts[1].Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That(execution.Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That(execution.Events.Any(item => item.Name == "kevlar.retry")).IsTrue();
        await Assert.That(attempts[0].GetTagItem("exception.type")).IsEqualTo(typeof(InvalidOperationException).FullName);
        await Assert.That(capture.Stopped.SelectMany(span => span.TagObjects).Any(tag => Equals(tag.Value, "private-message"))).IsFalse();
    }

    [Test]
    public async Task Concurrent_Hedge_Attempts_Are_Siblings_And_Loser_Cancellation_Is_Recorded()
    {
        using var capture = new Capture();
        var calls = 0;
        var result = await Shield.Hedge(1, delay: TimeSpan.Zero).ExecuteAsync(async token =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return 42;
        });
        await WaitUntilAsync(() => capture.Stopped.Count == 3);
        await Assert.That(result).IsEqualTo(42);
        var execution = capture.Stopped.Single(span => span.OperationName == "kevlar.execute");
        var attempts = capture.Stopped.Where(span => span.OperationName == "kevlar.attempt").ToArray();
        await Assert.That(attempts.Length).IsEqualTo(2);
        await Assert.That(attempts.All(span => span.ParentSpanId == execution.SpanId)).IsTrue();
        await Assert.That(attempts.Count(span => Equals(span.GetTagItem("kevlar.attempt.outcome"), "cancelled"))).IsEqualTo(1);
        await Assert.That(attempts.Count(span => Equals(span.GetTagItem("kevlar.attempt.outcome"), "success"))).IsEqualTo(1);
    }

    [Test]
    public async Task Generated_Hedge_Action_Is_Inside_An_Attempt_Span()
    {
        using var capture = new Capture();
        Activity? generatedActivity = null;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = _ => _ =>
            {
                generatedActivity = Activity.Current;
                return new ValueTask<int>(42);
            };
        });
        var result = await shield.ExecuteAsync(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        });
        await WaitUntilAsync(() => capture.Stopped.Count == 3);
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(generatedActivity!.OperationName).IsEqualTo("kevlar.attempt");
        await Assert.That(generatedActivity.GetTagItem("kevlar.attempt.number")).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Failed_And_PreCancelled_Outcomes_Are_Recorded(bool cancelled)
    {
        using var capture = new Capture();
        using var cancellation = new CancellationTokenSource();
        if (cancelled)
        {
            cancellation.Cancel();
        }
        var failure = new InvalidOperationException("private-message");
        var outcome = await Shield.Empty.ExecuteOutcomeAsync<int>(
            _ => ValueTask.FromException<int>(failure), cancellation.Token);
        var span = capture.Stopped.Single();
        await Assert.That(outcome.IsSuccess).IsFalse();
        await Assert.That(span.GetTagItem("kevlar.execution.outcome")).IsEqualTo(cancelled ? "cancelled" : "failure");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(span.GetTagItem("exception.message")).IsNull();
    }

    [Test]
    public async Task Circuit_And_Timeout_Events_Are_Recorded_On_Execution()
    {
        using var capture = new Capture();
        var breaker = Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: TimeSpan.FromMinutes(1));
        _ = await breaker.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new InvalidOperationException()));
        _ = await breaker.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
        _ = await Shield.Timeout(TimeSpan.FromMilliseconds(10)).ExecuteOutcomeAsync(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 42;
        });
        var events = capture.Stopped.SelectMany(span => span.Events).ToArray();
        await Assert.That(events.Any(item => item.Name == "kevlar.circuit_opened")).IsTrue();
        await Assert.That(events.Any(item => item.Name == "kevlar.rejection")).IsTrue();
        await Assert.That(events.Any(item => item.Name == "kevlar.timeout")).IsTrue();
    }

    [Test]
    [Arguments(ActivitySamplingResult.None)]
    [Arguments(ActivitySamplingResult.PropagationData)]
    [Arguments(ActivitySamplingResult.AllData)]
    public async Task Sampling_Decisions_Are_Respected(ActivitySamplingResult sampling)
    {
        using var capture = new Capture(sampling);
        _ = await Shield.Retry(1, Backoff.None).ExecuteAsync(static _ => new ValueTask<int>(42));
        if (sampling == ActivitySamplingResult.None)
        {
            await Assert.That(capture.Stopped.Count).IsEqualTo(0);
        }
        else
        {
            await Assert.That(capture.Stopped.Count).IsEqualTo(2);
            await Assert.That(capture.Stopped.Any(span => span.TagObjects.Any())).IsEqualTo(sampling == ActivitySamplingResult.AllData);
        }
    }

    [Test]
    public async Task Without_Listener_Delegate_Keeps_Application_Activity()
    {
        using var parent = new Activity("application").Start();
        Activity? observed = null;
        _ = await Shield.Retry(1, Backoff.None).ExecuteAsync(_ =>
        {
            observed = Activity.Current;
            return new ValueTask<int>(42);
        });
        await Assert.That(ReferenceEquals(observed, parent)).IsTrue();
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
    }

    [Test]
    [Arguments("sample")]
    [Arguments("start")]
    [Arguments("stop")]
    public async Task Throwing_Activity_Listeners_Do_Not_Change_Result_Or_Caller_Activity(string phase)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Kevlar",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => phase == "sample"
                ? throw new InvalidOperationException("sample failure") : ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { if (phase == "start") throw new InvalidOperationException("start failure"); },
            ActivityStopped = _ => { if (phase == "stop") throw new InvalidOperationException("stop failure"); },
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("application").SetIdFormat(ActivityIdFormat.W3C).Start();
        var result = await Shield.Retry(1, Backoff.None).ExecuteAsync(static _ => new ValueTask<int>(42));
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
    }

    [Test]
    public async Task Result_Fallback_Records_Event_Without_Exposing_Result()
    {
        using var capture = new Capture();
        var shield = Shield.For<int>().WhenResultEquals(-1).FallbackTo(42);
        var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(-1));
        await Assert.That(result).IsEqualTo(42);
        var execution = capture.Stopped.Single();
        await Assert.That(execution.GetTagItem("kevlar.execution.outcome")).IsEqualTo("success");
        var item = execution.Events.Single(item => item.Name == "kevlar.fallback");
        await Assert.That(item.Tags.Any(tag => tag.Key.Contains("result", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Events_Inside_An_Application_Child_Enrich_The_Kevlar_Ancestor()
    {
        using var capture = new Capture();
        using var source = new ActivitySource("application-child");
        using var childListener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(childListener);
        await Shield.Empty.ExecuteWithContextAsync(context =>
        {
            using var child = source.StartActivity("child")!;
            context.RecordEvent("custom");
            return ValueTask.CompletedTask;
        });
        await Assert.That(capture.Stopped.Single().Events.Any(item => item.Name == "kevlar.custom")).IsTrue();
    }

    [Test]
    public async Task Context_Initializer_Exception_Closes_The_Execution_Span()
    {
        using var capture = new Capture();
        var failure = new InvalidOperationException("initializer");
        try
        {
            await Shield.Empty.ExecuteWithContextAsync(
                42, (_, _) => throw failure, static (state, _) => new ValueTask<int>(state));
            throw new InvalidOperationException("Expected initializer failure.");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure))
        {
        }
        var execution = capture.Stopped.Single();
        await Assert.That(execution.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(execution.GetTagItem("kevlar.execution.outcome")).IsEqualTo("failure");
    }

    [Test]
    public async Task Names_Are_Bounded_Without_Splitting_Surrogate_Pairs()
    {
        using var capture = new Capture();
        var name = new string('x', 255) + "\U0001F600";
        _ = Shield.Empty.WithName(name).Execute(static _ => 42);
        await Assert.That(capture.Stopped.Single().GetTagItem("kevlar.shield.name"))
            .IsEqualTo(new string('x', 255));
    }

    [Test]
    public async Task Synchronous_Failure_Preserves_Exception_And_Restores_Parent()
    {
        using var capture = new Capture();
        using var parent = new Activity("application").Start();
        var failure = new InvalidOperationException("private-message");
        try
        {
            Shield.Retry(1, Backoff.None).Execute<int>(_ => throw failure);
            throw new InvalidOperationException("Expected protected failure.");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure))
        {
        }
        await Assert.That(capture.Stopped.Count).IsEqualTo(3);
        await Assert.That(capture.Stopped.All(span => span.Status == ActivityStatusCode.Error)).IsTrue();
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
    }

    [Test]
    public async Task Disposing_The_Last_Listener_Restores_Untraced_Execution()
    {
        using (var capture = new Capture())
        {
            _ = Shield.Empty.Execute(static _ => 42);
            await Assert.That(capture.Stopped.Count).IsEqualTo(1);
        }
        using var parent = new Activity("application").Start();
        var observed = Shield.Empty.Execute(static _ => Activity.Current);
        await Assert.That(ReferenceEquals(observed, parent)).IsTrue();
    }

    [Test]
    public async Task Attempt_Telemetry_Remains_Available_Without_Duplicate_Span_Events()
    {
        using var capture = new Capture();
        var listener = new AttemptListener();
        using var subscription = KevlarDiagnostics.Listen(listener);
        _ = await Shield.Retry(1, Backoff.None).ExecuteAsync(static _ => new ValueTask<int>(42));
        await Assert.That(listener.Count).IsEqualTo(1);
        await Assert.That(capture.Stopped.Count(span => span.OperationName == "kevlar.attempt")).IsEqualTo(1);
        await Assert.That(capture.Stopped.SelectMany(span => span.Events)
            .Any(item => item.Name == "kevlar.execution_attempt")).IsFalse();
    }

    private sealed class AttemptListener : IKevlarTelemetryListener
    {
        public int Count { get; private set; }

        public void OnEvent(in KevlarTelemetryEvent item)
        {
            if (item.EventName == "execution_attempt")
            {
                Count++;
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class Capture : IDisposable
    {
        private readonly ActivityListener _listener;
        public ConcurrentQueue<Activity> Stopped { get; } = new();

        public Capture(ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Kevlar",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
                ActivityStopped = Stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }
}
