namespace Kevlar.Tests;

public class ResultDisposalTests
{
    [Test]
    public async Task Retry_Disposes_Handled_Result_After_Callback_Before_Next_Attempt()
    {
        var handled = new DisposableResult(isHandled: true);
        var final = new DisposableResult(isHandled: false);
        var callbackSawLiveResult = false;
        var nextAttemptSawDisposedResult = false;
        var attempts = 0;
        var shield = Shield.For<DisposableResult>()
            .WhenResult(static result => result.IsHandled)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    callbackSawLiveResult = retry.Outcome.Result?.DisposeCount == 0;
                    return default;
                };
            });

        var result = await shield.ExecuteAsync(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 2)
            {
                nextAttemptSawDisposedResult = handled.DisposeCount == 1;
                return new ValueTask<DisposableResult>(final);
            }

            return new ValueTask<DisposableResult>(handled);
        });

        await Assert.That(ReferenceEquals(result, final)).IsTrue();
        await Assert.That(callbackSawLiveResult).IsTrue();
        await Assert.That(nextAttemptSawDisposedResult).IsTrue();
        await Assert.That(handled.DisposeCount).IsEqualTo(1);
        await Assert.That(final.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Retry_Disposes_Handled_Result_Before_Ordinary_Backoff()
    {
        var timeProvider = new ControlledTimeProvider();
        var handled = new DisposableResult(isHandled: true);
        var final = new DisposableResult(isHandled: false);
        var attempts = 0;
        var shield = Shield.For<DisposableResult>()
            .WhenResult(static result => result.IsHandled)
            .Retry(1, Backoff.Constant(TimeSpan.FromMinutes(1)))
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(_ => new ValueTask<DisposableResult>(
            Interlocked.Increment(ref attempts) == 1 ? handled : final)).AsTask();
        await timeProvider.WaitForTimersAsync(1);

        await Assert.That(handled.DisposeCount).IsEqualTo(1);
        timeProvider.FireTimer(0);
        var result = await execution;
        await Assert.That(ReferenceEquals(result, final)).IsTrue();
        await Assert.That(final.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Retry_Disposes_Result_After_Inner_Hedges_Finish()
    {
        var timeProvider = new ControlledTimeProvider();
        var first = new DisposableResult(isHandled: true);
        var second = new DisposableResult(isHandled: true);
        var final = new DisposableResult(isHandled: false);
        var attempts = 0;
        var shield = Shield.For<DisposableResult>()
            .WhenResult(static result => result.IsHandled)
            .Retry(1, Backoff.Constant(TimeSpan.FromMinutes(1)))
            .Hedge(1, Timeout.InfiniteTimeSpan)
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(_ => new ValueTask<DisposableResult>(
            Interlocked.Increment(ref attempts) switch
            {
                1 => first,
                2 => second,
                _ => final,
            })).AsTask();
        await timeProvider.WaitForTimersAsync(1);

        await Assert.That(first.DisposeCount).IsEqualTo(1);
        await Assert.That(second.DisposeCount).IsEqualTo(1);
        timeProvider.FireTimer(0);

        await Assert.That(ReferenceEquals(await execution, final)).IsTrue();
        await Assert.That(final.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Retry_Prefers_Async_Disposal()
    {
        var handled = new AsyncDisposableResult(isHandled: true);
        var final = new AsyncDisposableResult(isHandled: false);
        var attempts = 0;
        var shield = Shield.For<AsyncDisposableResult>()
            .WhenResult(static result => result.IsHandled)
            .Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(_ => new ValueTask<AsyncDisposableResult>(
            Interlocked.Increment(ref attempts) == 1 ? handled : final));

        await Assert.That(ReferenceEquals(result, final)).IsTrue();
        await Assert.That(handled.AsyncDisposeCount).IsEqualTo(1);
        await Assert.That(handled.DisposeCount).IsEqualTo(0);
        await Assert.That(final.AsyncDisposeCount).IsEqualTo(0);
        await Assert.That(final.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Retry_Awaits_Async_Disposal_Before_The_Next_Attempt()
    {
        var handled = new BlockingAsyncDisposableResult(isHandled: true);
        var final = new BlockingAsyncDisposableResult(isHandled: false);
        var attempts = 0;
        var shield = Shield.For<BlockingAsyncDisposableResult>()
            .WhenResult(static result => result.IsHandled)
            .Retry(1, Backoff.None);

        var execution = shield.ExecuteAsync(_ => new ValueTask<BlockingAsyncDisposableResult>(
            Interlocked.Increment(ref attempts) == 1 ? handled : final)).AsTask();

        await handled.DisposalStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(attempts).IsEqualTo(1);
        handled.AllowDisposal();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(ReferenceEquals(result, final)).IsTrue();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(handled.AsyncDisposeCount).IsEqualTo(1);
        await Assert.That(handled.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Cancellation_During_Async_Disposal_Stops_The_Retry()
    {
        var handled = new BlockingAsyncDisposableResult(isHandled: true);
        var final = new BlockingAsyncDisposableResult(isHandled: false);
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.For<BlockingAsyncDisposableResult>()
            .WhenResult(static result => result.IsHandled)
            .Retry(1, Backoff.None);
        var execution = shield.ExecuteAsync(
            _ => new ValueTask<BlockingAsyncDisposableResult>(
                Interlocked.Increment(ref attempts) == 1 ? handled : final),
            cancellation.Token).AsTask();
        await handled.DisposalStarted;

        cancellation.Cancel();
        handled.AllowDisposal();

        _ = await Assert.That(async () => await execution)
            .Throws<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(handled.AsyncDisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Concurrent_Suppression_During_Deferred_Disposal_Preserves_The_Result()
    {
        var first = new BlockingAsyncDisposableResult(isHandled: true);
        var second = new BlockingAsyncDisposableResult(isHandled: true);
        var suppressionRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.For<BlockingAsyncDisposableResult>()
            .WhenResult(static result => result.IsHandled)
            .Hedge(1, TimeSpan.Zero)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = async retry =>
                {
                    if (!ReferenceEquals(retry.Outcome.Result, second))
                    {
                        return;
                    }

                    await first.DisposalStarted;
                    retry.SuppressAdditionalAttempts();
                    suppressionRequested.TrySetResult();
                };
            });
        var execution = shield.ExecuteAsync(_ => new ValueTask<BlockingAsyncDisposableResult>(
            Interlocked.Increment(ref attempts) == 1 ? first : second)).AsTask();
        await first.DisposalStarted;
        await suppressionRequested.Task;

        second.AllowDisposal();
        first.AllowDisposal();
        var result = await execution;

        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(first.AsyncDisposeCount).IsEqualTo(1);
        await Assert.That(result.AsyncDisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Untyped_Hedge_Disposes_Cancelled_Loser_And_Preserves_Winner()
    {
        var loser = new DisposableResult(isHandled: false);
        var winner = new DisposableResult(isHandled: false);
        var primaryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.Hedge(1, TimeSpan.Zero);

        var result = await shield.ExecuteAsync(async token =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                primaryStarted.TrySetResult();
                try
                {
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    return loser;
                }
            }

            await primaryStarted.Task;
            return winner;
        });

        await loser.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(ReferenceEquals(result, winner)).IsTrue();
        await Assert.That(loser.DisposeCount).IsEqualTo(1);
        await Assert.That(winner.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Hedge_Disposes_Handled_Result_When_Superseded()
    {
        var handled = new DisposableResult(isHandled: true);
        var winner = new DisposableResult(isHandled: false);
        var releases = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.For<DisposableResult>()
            .WhenResult(static result => result.IsHandled)
            .Hedge(1, TimeSpan.Zero);

        var execution = shield.ExecuteAsync(async _ =>
        {
            var attempt = Interlocked.Increment(ref attempts) - 1;
            if (attempt == 1)
            {
                allStarted.TrySetResult();
            }

            await releases[attempt].Task;
            return attempt == 0 ? handled : winner;
        }).AsTask();

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releases[0].SetResult();
        await Task.Yield();
        releases[1].SetResult();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await handled.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(ReferenceEquals(result, winner)).IsTrue();
        await Assert.That(handled.DisposeCount).IsEqualTo(1);
        await Assert.That(winner.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Hedge_Does_Not_Dispose_A_Loser_Sharing_The_Winner_Instance()
    {
        var shared = new DisposableResult(isHandled: false);
        var shield = Shield.Hedge(1, TimeSpan.Zero);

        var result = await shield.ExecuteAsync(_ => new ValueTask<DisposableResult>(shared));

        await Assert.That(ReferenceEquals(result, shared)).IsTrue();
        await Assert.That(shared.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Hedge_ActionGenerator_Disposes_All_NonSelected_Original_Results()
    {
        var primaryLoser = new DisposableResult(isHandled: false);
        var firstOriginal = new DisposableResult(isHandled: false);
        var secondOriginal = new DisposableResult(isHandled: false);
        var generatedWinner = new DisposableResult(isHandled: false);
        var attempts = 0;
        var shield = Shield.For<DisposableResult>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = hedge => async token =>
            {
                _ = await hedge.OriginalAction(token);
                _ = await hedge.OriginalAction(token);
                return generatedWinner;
            };
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            switch (Interlocked.Increment(ref attempts))
            {
                case 1:
                    try
                    {
                        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return primaryLoser;
                    }

                    break;
                case 2:
                    return firstOriginal;
                case 3:
                    return secondOriginal;
            }

            throw new InvalidOperationException("Unexpected attempt.");
        });

        await primaryLoser.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(ReferenceEquals(result, generatedWinner)).IsTrue();
        await Assert.That(firstOriginal.DisposeCount).IsEqualTo(1);
        await Assert.That(secondOriginal.DisposeCount).IsEqualTo(1);
        await Assert.That(primaryLoser.DisposeCount).IsEqualTo(1);
        await Assert.That(generatedWinner.DisposeCount).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task Disposal_Failure_Is_Reported_Without_Changing_The_Outcome()
    {
        var failure = new IOException("dispose");
        var handled = new AsyncDisposableResult(isHandled: true, failure);
        var final = new AsyncDisposableResult(isHandled: false);
        CallbackErrorEvent? reported = null;
        Action<CallbackErrorEvent> handler = item => reported = item;
        KevlarDiagnostics.OnCallbackError += handler;

        try
        {
            var attempts = 0;
            var shield = Shield.For<AsyncDisposableResult>()
                .WhenResult(static result => result.IsHandled)
                .Retry(1, Backoff.None);

            var result = await shield.ExecuteAsync(_ => new ValueTask<AsyncDisposableResult>(
                Interlocked.Increment(ref attempts) == 1 ? handled : final));

            await Assert.That(ReferenceEquals(result, final)).IsTrue();
            await Assert.That(reported?.Kind).IsEqualTo(CallbackErrorKind.ResultDisposal);
            await Assert.That(reported?.Source).IsEqualTo("OutcomeDisposer");
            await Assert.That(ReferenceEquals(reported?.Exception, failure)).IsTrue();
        }
        finally
        {
            KevlarDiagnostics.OnCallbackError -= handler;
        }
    }

    private sealed class DisposableResult(bool isHandled) : IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public bool IsHandled { get; } = isHandled;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task Disposed => _disposed.Task;

        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                _disposed.TrySetResult();
            }
        }
    }

    private sealed class AsyncDisposableResult(bool isHandled, Exception? failure = null)
        : IDisposable, IAsyncDisposable
    {
        private int _asyncDisposeCount;
        private int _disposeCount;

        public bool IsHandled { get; } = isHandled;

        public int AsyncDisposeCount => Volatile.Read(ref _asyncDisposeCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _asyncDisposeCount);
            return failure is null ? default : ValueTask.FromException(failure);
        }
    }

    private sealed class BlockingAsyncDisposableResult(bool isHandled)
        : IDisposable, IAsyncDisposable
    {
        private readonly TaskCompletionSource _allowDisposal = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposalStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _asyncDisposeCount;
        private int _disposeCount;

        public bool IsHandled { get; } = isHandled;

        public int AsyncDisposeCount => Volatile.Read(ref _asyncDisposeCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task DisposalStarted => _disposalStarted.Task;

        public void AllowDisposal() => _allowDisposal.TrySetResult();

        public void Dispose() => Interlocked.Increment(ref _disposeCount);

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _asyncDisposeCount);
            _disposalStarted.TrySetResult();
            await _allowDisposal.Task;
        }
    }
}
