using System.Diagnostics;

namespace Kevlar.Extensions.Tracing.Tests;

[NotInParallel]
public class TracingTests
{
    [Test]
    public async Task Retry_After_Await_Enriches_Existing_Activity_Without_Child_Spans()
    {
        using var subscription = KevlarTracing.Listen();
        using var source = new ActivitySource("tracing-tests");
        var started = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => started++,
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("application")!;
        var attempts = 0;
        var result = await Shield.Retry(1, Backoff.None).WithName("catalog").ExecuteAsync(async _ =>
        {
            await Task.Yield();
            if (++attempts == 1)
            {
                throw new InvalidOperationException("secret");
            }
            return 42;
        });

        var retry = activity.Events.Single(item => Tag(item, "kevlar.event.name") as string == "retry");
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(started).IsEqualTo(1);
        await Assert.That(ReferenceEquals(Activity.Current, activity)).IsTrue();
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That(activity.Duration).IsEqualTo(TimeSpan.Zero);
        await Assert.That(retry.Name).IsEqualTo("kevlar.strategy");
        await Assert.That(Tag(retry, "kevlar.shield.name")).IsEqualTo("catalog");
        await Assert.That(Tag(retry, "kevlar.strategy.name")).IsEqualTo("Retry");
        await Assert.That(Tag(retry, "kevlar.attempt.number")).IsEqualTo(1);
        await Assert.That(Tag(retry, "exception.type")).IsEqualTo(typeof(InvalidOperationException).FullName);
        await Assert.That(Tag(retry, "exception.message")).IsNull();
        await Assert.That(Tag(retry, "exception.stacktrace")).IsNull();
    }

    [Test]
    public async Task Parallel_Hedges_Keep_Their_Application_Activity()
    {
        using var subscription = KevlarTracing.Listen();
        var operations = Enumerable.Range(0, 12).Select(async index =>
        {
            using var activity = StartRecorded($"operation-{index}");
            var attempts = 0;
            var shield = Shield.Hedge(1, delay: TimeSpan.Zero);
            var result = await shield.ExecuteWithContextAsync(async context =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                context.RecordEvent($"operation-{index}");
                if (attempt == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                }
                return index;
            });
            // Hedge loser cleanup may complete after the winner is returned. Wait for its telemetry callback.
            await WaitUntilAsync(() => activity.Events.Count(item => Tag(item, "kevlar.event.name") as string == "hedge_attempt") == 2);
            var custom = activity.Events.Where(item => (Tag(item, "kevlar.event.name") as string)?.StartsWith("operation-", StringComparison.Ordinal) == true).ToArray();
            await Assert.That(result).IsEqualTo(index);
            await Assert.That(custom.Length).IsEqualTo(2);
            await Assert.That(custom.All(item => Equals(Tag(item, "kevlar.event.name"), $"operation-{index}"))).IsTrue();
            await Assert.That(activity.Events.Count(item => Equals(Tag(item, "kevlar.hedge.winner"), true))).IsEqualTo(1);
            await Assert.That(activity.Events.Count(item => Equals(Tag(item, "kevlar.hedge.cancelled"), true))).IsEqualTo(1);
        });
        await Task.WhenAll(operations);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task Unsampled_Or_Propagation_Only_Activities_Are_Ignored(bool recorded, bool allData)
    {
        using var subscription = KevlarTracing.Listen();
        using var activity = new Activity("ignored").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = recorded ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
        activity.IsAllDataRequested = allData;
        await EmitAsync();
        await Assert.That(activity.Events.Any()).IsFalse();
    }

    [Test]
    public async Task No_Current_Activity_Does_Not_Create_One()
    {
        using var subscription = KevlarTracing.Listen();
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            await EmitAsync();
            await Assert.That(Activity.Current).IsNull();
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Test]
    public async Task No_Subscription_Does_Not_Enrich()
    {
        using var activity = StartRecorded("disabled");
        await EmitAsync();
        await Assert.That(activity.Events.Any()).IsFalse();
    }

    [Test]
    public async Task Stopped_Activity_Is_Ignored()
    {
        using var subscription = KevlarTracing.Listen();
        using var activity = StartRecorded("stopped");
        activity.Stop();
        var previous = Activity.Current;
        try
        {
            Activity.Current = activity;
            await EmitAsync();
            await Assert.That(activity.Events.Any()).IsFalse();
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Test]
    public async Task Suppressed_ExecutionContext_Is_Not_Reconstructed()
    {
        using var subscription = KevlarTracing.Listen();
        using var activity = StartRecorded("parent");
        Task pending;
        using (ExecutionContext.SuppressFlow())
        {
            pending = Task.Run(async () => await EmitAsync());
        }
        await pending;
        await Assert.That(activity.Events.Any()).IsFalse();
    }

    [Test]
    public async Task Disposing_One_Registration_Leaves_The_Other_Active()
    {
        using var first = KevlarTracing.Listen();
        using var second = KevlarTracing.Listen();
        using var activity = StartRecorded("duplicates");
        await EmitAsync();
        await Assert.That(CustomEvents(activity).Count()).IsEqualTo(2);
        first.Dispose();
        first.Dispose();
        await EmitAsync();
        await Assert.That(CustomEvents(activity).Count()).IsEqualTo(3);
        second.Dispose();
        await EmitAsync();
        await Assert.That(CustomEvents(activity).Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Options_Are_Snapshotted_And_Strings_Are_Bounded()
    {
        var options = new KevlarTracingOptions { IncludeExceptionDetails = true, IncludeOperationKey = true, MaximumTagValueLength = 8 };
        using var subscription = KevlarTracing.Listen(options);
        options.IncludeExceptionDetails = false;
        options.IncludeOperationKey = false;
        options.MaximumTagValueLength = 1;
        using var activity = StartRecorded("snapshot");
        var failure = new DetailException();
        await Shield.Empty.WithName("catalog-long-name").ExecuteWithContextAsync(context =>
        {
            context.Properties.Set(KevlarKeys.OperationKey, "logical-operation");
            context.RecordEvent("custom-event-long", exception: failure);
            return ValueTask.CompletedTask;
        });
        var item = activity.Events.First();
        await Assert.That(Tag(item, "kevlar.shield.name")).IsEqualTo("catalog-");
        await Assert.That(Tag(item, "kevlar.event.name")).IsEqualTo("custom-e");
        await Assert.That(Tag(item, "kevlar.operation.key")).IsEqualTo("logical-");
        await Assert.That(Tag(item, "exception.message")).IsEqualTo("secret-m");
        await Assert.That(Tag(item, "exception.stacktrace")).IsEqualTo("secret-s");
        await Assert.That(item.Tags.All(pair => pair.Value is not string value || value.Length <= 8)).IsTrue();
    }

    [Test]
    public async Task Truncation_Does_Not_Split_Surrogate_Pairs()
    {
        using var subscription = KevlarTracing.Listen(new KevlarTracingOptions { MaximumTagValueLength = 2 });
        using var activity = StartRecorded("unicode");
        await Shield.Empty.WithName("a😀b").ExecuteWithContextAsync(context =>
        {
            context.RecordEvent("custom");
            return ValueTask.CompletedTask;
        });
        await Assert.That(Tag(activity.Events.First(), "kevlar.shield.name")).IsEqualTo("a");
    }

    [Test]
    public async Task Default_Events_Contain_No_Results_Properties_Or_Exception_Objects()
    {
        using var subscription = KevlarTracing.Listen();
        using var activity = StartRecorded("privacy");
        var shield = Shield.For<string>().WhenResultEquals("secret-result").Retry(1, Backoff.None);
        await shield.ExecuteWithContextAsync(context =>
        {
            context.Properties.Set(KevlarKeys.OperationKey, "secret-operation");
            context.Properties.Set(new KevlarKey<object>("secret-key"), new object());
            return new ValueTask<string>("secret-result");
        });
        var tags = activity.Events.SelectMany(item => item.Tags).ToArray();
        await Assert.That(tags.Length > 0).IsTrue();
        await Assert.That(tags.All(pair => pair.Value is string or int or double or bool)).IsTrue();
        await Assert.That(tags.Any(pair => pair.Value is string value && value.Contains("secret", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Listener_Failure_Does_Not_Replace_Outcome()
    {
        using var subscription = KevlarTracing.Listen(new KevlarTracingOptions { IncludeExceptionDetails = true });
        using var activity = StartRecorded("broken-details");
        var failure = new ThrowingMessageException();
        var outcome = await Shield.Retry(1, Backoff.None).ExecuteOutcomeAsync<int>(_ => ValueTask.FromException<int>(failure));
        await Assert.That(ReferenceEquals(outcome.Exception, failure)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Invalid_Tag_Length_Is_Rejected(int length)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        {
            using var subscription = KevlarTracing.Listen(new KevlarTracingOptions { MaximumTagValueLength = length });
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Concurrent_Dispose_Is_Idempotent()
    {
        using var subscription = KevlarTracing.Listen();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(subscription.Dispose)));
        using var activity = StartRecorded("disposed");
        await EmitAsync();
        await Assert.That(activity.Events.Any()).IsFalse();
    }

    [Test]
    public async Task Circuit_Transitions_And_Rejections_Keep_Retry_Hints()
    {
        using var subscription = KevlarTracing.Listen();
        using var activity = StartRecorded("circuit");
        var shield = Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: TimeSpan.FromMinutes(1));
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new InvalidOperationException()));
        _ = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));

        var opened = activity.Events.Single(item => Equals(Tag(item, "kevlar.event.name"), "circuit_opened"));
        await Assert.That(Tag(opened, "kevlar.circuit.from")).IsEqualTo("Closed");
        await Assert.That(Tag(opened, "kevlar.circuit.to")).IsEqualTo("Open");
        await Assert.That(Tag(opened, "kevlar.delay.seconds")).IsEqualTo(60d);
        var rejection = activity.Events.Single(item => Equals(Tag(item, "kevlar.event.name"), "rejection"));
        await Assert.That(Tag(rejection, "kevlar.rejection.kind")).IsEqualTo("circuit_open");
        await Assert.That((double)Tag(rejection, "kevlar.retry_after.seconds")!).IsGreaterThan(0);
        await Assert.That((bool)Tag(rejection, "kevlar.outcome.success")!).IsFalse();
    }

    [Test]
    public async Task Timeout_Enriches_Parent_After_Async_Cancellation()
    {
        using var subscription = KevlarTracing.Listen();
        using var activity = StartRecorded("timeout");
        var outcome = await Shield.Timeout(TimeSpan.FromMilliseconds(10)).ExecuteOutcomeAsync(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 42;
        });
        await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
        var timeout = activity.Events.Single(item => Equals(Tag(item, "kevlar.event.name"), "timeout"));
        await Assert.That(Tag(timeout, "kevlar.duration.seconds")).IsEqualTo(0.01d);
        await Assert.That(Tag(timeout, "exception.type")).IsEqualTo(typeof(TimeoutExceededException).FullName);
    }

    [Test]
    public async Task Callback_Error_Enrichment_Does_Not_Change_Result()
    {
        using var subscription = KevlarTracing.Listen();
        using var activity = StartRecorded("callback");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => throw new InvalidOperationException("callback failure");
        });
        var attempts = 0;
        var result = await shield.ExecuteAsync(_ => ++attempts == 1
            ? ValueTask.FromException<int>(new InvalidOperationException())
            : new ValueTask<int>(42));
        await Assert.That(result).IsEqualTo(42);
        var callback = activity.Events.Single(item => Equals(Tag(item, "kevlar.event.name"), "callback_error"));
        await Assert.That(Tag(callback, "kevlar.callback.kind")).IsNotNull();
        await Assert.That(Tag(callback, "kevlar.callback.source")).IsNotNull();
    }

    [Test]
    public async Task Built_In_Source_And_Adapter_Enrich_The_Current_Execution_Span()
    {
        using var subscription = KevlarTracing.Listen();
        Activity? execution = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == KevlarDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => execution = activity,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = StartRecorded("application");
        await EmitAsync();
        await Assert.That(execution!.OperationName).IsEqualTo("kevlar.execute");
        await Assert.That(execution.ParentSpanId).IsEqualTo(parent.SpanId);
        await Assert.That(execution.Events.Any(item => item.Name == "kevlar.custom")).IsTrue();
        await Assert.That(CustomEvents(execution).Single().Name).IsEqualTo("kevlar.strategy");
        await Assert.That(parent.Events.Any()).IsFalse();
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
    }

    private static Activity StartRecorded(string name)
    {
        var activity = new Activity(name).SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        return activity;
    }

    private static object? Tag(ActivityEvent item, string name) => item.Tags.FirstOrDefault(pair => pair.Key == name).Value;
    private static IEnumerable<ActivityEvent> CustomEvents(Activity activity) => activity.Events.Where(item => Equals(Tag(item, "kevlar.event.name"), "custom"));
    private static ValueTask EmitAsync() => Shield.Empty.ExecuteWithContextAsync(static context =>
    {
        context.RecordEvent("custom");
        return ValueTask.CompletedTask;
    });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class DetailException : Exception
    {
        public override string Message => "secret-message";
        public override string StackTrace => "secret-stacktrace";
    }

    private sealed class ThrowingMessageException : Exception
    {
        public override string Message => throw new InvalidOperationException("formatter failed");
    }
}

