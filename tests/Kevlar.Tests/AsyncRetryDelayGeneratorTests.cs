namespace Kevlar.Tests;

public class AsyncRetryDelayGeneratorTests
{
    [Test]
    public async Task Synchronously_Completed_Generator_Overrides_Delay_Before_Hooks()
    {
        var order = new List<string>();
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.Constant(TimeSpan.FromHours(1));
            options.DelayGenerator = retry =>
            {
                order.Add($"generator:{retry.RetryNumber}:{retry.Delay.TotalHours}");
                return new ValueTask<TimeSpan?>(TimeSpan.Zero);
            };
            options.OnRetry = retry =>
            {
                order.Add($"hook:{retry.RetryNumber}:{retry.Delay.TotalSeconds}");
                return default;
            };
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            order.Add($"attempt:{++attempts}");
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException("retry"))
                : new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(order).IsEquivalentTo(
            ["attempt:1", "generator:1:1", "hook:1:0", "attempt:2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Typed_Truly_Asynchronous_Generator_Is_Awaited_And_Preserves_Outcome()
    {
        var gate = new AsyncGate("retry delay generator");
        var order = new List<string>();
        var seenResult = 0;
        var attempts = 0;
        var shield = Shield.For<int>()
            .WhenResultEquals(-1)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = async retry =>
                {
                    order.Add("generator-start");
                    seenResult = retry.Outcome.Result;
                    retry.Context.Properties.Set(ContextKey, "alive");
                    await gate.EnterAsync();
                    order.Add(retry.Context.Properties.TryGet(ContextKey, out var value) ? value : "missing");
                    return TimeSpan.Zero;
                };
                options.OnRetry = _ =>
                {
                    order.Add("hook");
                    return default;
                };
            });

        var execution = shield.ExecuteAsync(_ =>
        {
            order.Add($"attempt-{++attempts}");
            return new ValueTask<int>(attempts == 1 ? -1 : 42);
        }).AsTask();

        await gate.WaitForEntryAsync();
        await Assert.That(order).IsEquivalentTo(
            ["attempt-1", "generator-start"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        gate.Release();

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(seenResult).IsEqualTo(-1);
        await Assert.That(order).IsEquivalentTo(
            ["attempt-1", "generator-start", "alive", "hook", "attempt-2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Generator_Result_Is_Clamped_To_MaxDelay_Before_Hooks()
    {
        var seenByGenerator = TimeSpan.MinValue;
        var seenByHook = TimeSpan.Zero;
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.Constant(TimeSpan.FromSeconds(9));
            options.MaxDelay = TimeSpan.FromSeconds(5);
            options.DelayGenerator = retry =>
            {
                seenByGenerator = retry.Delay;
                return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(8));
            };
            options.OnRetry = retry =>
            {
                seenByHook = retry.Delay;
                return default;
            };
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(seenByGenerator).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(seenByHook).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Invalid_Generator_Result_Keeps_The_Previous_Delay()
    {
        var generated = new Queue<TimeSpan?>([null, TimeSpan.FromSeconds(-1)]);
        var seen = new List<TimeSpan>();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ => new ValueTask<TimeSpan?>(generated.Dequeue());
            options.OnRetry = retry =>
            {
                seen.Add(retry.Delay);
                return default;
            };
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(seen).IsEquivalentTo([TimeSpan.Zero, TimeSpan.Zero]);
    }

    [Test]
    public async Task Generator_Failure_Surfaces_Exact_Exception_And_Skips_Hooks()
    {
        var generatorFailure = new FormatException("generator failed");
        var hooks = 0;
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ => ValueTask.FromException<TimeSpan?>(generatorFailure);
            options.OnRetry = _ =>
            {
                hooks++;
                return default;
            };
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(ReferenceEquals(outcome.Exception, generatorFailure)).IsTrue();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(hooks).IsEqualTo(0);
    }

    [Test]
    public async Task Caller_Cancellation_During_Generator_Runs_Hooks_Then_Stops_Retrying()
    {
        using var cancellation = new CancellationTokenSource();
        var gate = new AsyncGate("retry delay cancellation");
        var hooks = 0;
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.DelayGenerator = async retry =>
            {
                await Assert.That(retry.Context.CancellationToken).IsEqualTo(cancellation.Token);
                await gate.EnterAsync();
                return TimeSpan.Zero;
            };
            options.OnRetry = _ =>
            {
                hooks++;
                return default;
            };
        });

        var execution = shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        }, cancellation.Token).AsTask();

        await gate.WaitForEntryAsync();
        cancellation.Cancel();
        gate.Release();
        var outcome = await execution;

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(hooks).IsEqualTo(1);
    }

    [Test]
    public async Task Concurrent_Executions_Receive_Independent_Typed_Events()
    {
        const int executionCount = 32;
        var events = new List<(int Attempt, int Result)>();
        var shield = Shield.For<int>()
            .WhenResultEquals(-1)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = retry =>
                {
                    lock (events)
                    {
                        events.Add((retry.RetryNumber, retry.Outcome.Result));
                    }

                    return new ValueTask<TimeSpan?>(TimeSpan.Zero);
                };
            });

        var executions = Enumerable.Range(0, executionCount)
            .Select(_ =>
            {
                var attempt = 0;
                return shield.ExecuteAsync(_ => new ValueTask<int>(attempt++ == 0 ? -1 : 42)).AsTask();
            });

        await Task.WhenAll(executions);

        await Assert.That(events.Count).IsEqualTo(executionCount);
        await Assert.That(events.All(retry => retry == (1, -1))).IsTrue();
    }

    [Test]
    public async Task Generator_Can_Reenter_The_Same_Shield()
    {
        Shield? shield = null;
        var attempts = 0;
        var nestedResult = 0;
        shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = async _ =>
            {
                nestedResult = await shield!.ExecuteAsync(static _ => new ValueTask<int>(42));
                return TimeSpan.Zero;
            };
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(7);
        });

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(nestedResult).IsEqualTo(42);
    }

    [Test]
    public async Task Truly_Async_Generator_Is_Rejected_For_Synchronous_Execution()
    {
        var attempts = 0;
        // Released only after the assertions so the generator is guaranteed to still be pending
        // when the synchronous guard inspects it.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = async retry =>
            {
                await gate.Task;
                return TimeSpan.Zero;
            };
        });

        var exception = await Assert.That(() => shield.Execute<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<NotSupportedException>();

        await Assert.That(exception!.Message).Contains("RetryOptions.DelayGenerator");
        await Assert.That(exception!.Message).Contains("Use ExecuteAsync");
        // The guard fires when the generator yields, so the first attempt ran and no retry followed.
        await Assert.That(attempts).IsEqualTo(1);
        gate.SetResult();
    }

    [Test]
    public async Task Synchronously_Completed_Generator_Is_Accepted_For_Synchronous_Execution()
    {
        var attempts = 0;
        var generated = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.Constant(TimeSpan.FromHours(1));
            options.DelayGenerator = _ =>
            {
                generated++;
                return new(TimeSpan.Zero);
            };
        });

        var result = shield.Execute(_ => ++attempts == 1 ? throw new InvalidOperationException() : attempts);

        await Assert.That(result).IsEqualTo(2);
        await Assert.That(generated).IsEqualTo(1);
    }

    private static readonly KevlarKey<string> ContextKey = new("async-delay-lifetime");
}
