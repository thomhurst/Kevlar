using System.Collections.Concurrent;
using Kevlar.Strategies;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

[NotInParallel]
public class ContextLifecycleTests
{
    private static readonly KevlarKey<string> DirtyProperty = new("dirty");

    [Test]
    public async Task Every_Exit_Path_Resets_Every_Pooled_Context_Field()
    {
        foreach (var exitPath in Enum.GetValues<ExitPath>())
        {
            await AssertResetAfter(exitPath);
        }
    }

    [Test]
    public async Task Pool_Pressure_Beyond_Capacity_Returns_Only_Clean_Contexts()
    {
        const int pressureCount = 160;
        using var dirtyCancellation = new CancellationTokenSource();
        var dirtyTimeProvider = new FakeTimeProvider();
        var dirty = new PressureStrategy(
            pressureCount,
            DirtyProperty,
            setDirty: true,
            dirtyCancellation.Token,
            dirtyTimeProvider);
        var dirtyShield = Shield.Use(dirty)
            .WithName("dirty-pressure")
            .WithTimeProvider(dirtyTimeProvider);
        var dirtyExecutions = Enumerable.Range(0, pressureCount)
            .Select(index => dirtyShield.ExecuteAsync(
                _ => new ValueTask<int>(index),
                dirtyCancellation.Token).AsTask())
            .ToArray();

        try
        {
            await dirty.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(dirty.DirtyContexts).IsEqualTo(0);
            await Assert.That(dirty.Contexts.Distinct().Count()).IsEqualTo(pressureCount);
        }
        finally
        {
            dirty.Release.TrySetResult();
        }

        await Task.WhenAll(dirtyExecutions).WaitAsync(TimeSpan.FromSeconds(5));

        var clean = new PressureStrategy(
            KevlarContext.PoolCapacity,
            DirtyProperty,
            setDirty: false,
            default,
            TimeProvider.System);
        var cleanShield = Shield.Use(clean).WithName("clean-pressure");
        var cleanExecutions = Enumerable.Range(0, KevlarContext.PoolCapacity)
            .Select(index => cleanShield.ExecuteAsync(_ => new ValueTask<int>(index)).AsTask())
            .ToArray();

        try
        {
            await clean.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(clean.DirtyContexts).IsEqualTo(0);
            await Assert.That(clean.Contexts.Distinct().Count()).IsEqualTo(KevlarContext.PoolCapacity);
        }
        finally
        {
            clean.Release.TrySetResult();
        }

        await Task.WhenAll(cleanExecutions).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Reentrant_Execution_Never_Reuses_The_InUse_Outer_Context()
    {
        var strategy = new ReentrantStrategy(DirtyProperty);
        var result = await Shield.Use(strategy).ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(strategy.InnerUsedDifferentContext).IsTrue();
        await Assert.That(strategy.OuterPropertySurvived).IsTrue();
    }

    [Test]
    public async Task Forks_Copy_Initial_Properties_Without_Sharing_Later_Writes()
    {
        var key = new KevlarKey<string>("fork-value");
        var parent = KevlarContext.Rent(default, isSynchronous: false, TimeProvider.System, "parent");
        parent.Properties.Set(key, "initial");
        var first = parent.Fork(default);
        var second = parent.Fork(default);

        try
        {
            await Assert.That(first.Properties.GetOrDefault<string>(key)).IsEqualTo("initial");
            await Assert.That(second.Properties.GetOrDefault<string>(key)).IsEqualTo("initial");
            await Assert.That(ReferenceEquals(parent.Properties, first.Properties)).IsFalse();
            await Assert.That(ReferenceEquals(first.Properties, second.Properties)).IsFalse();

            first.Properties.Set(key, "first");
            second.Properties.Set(key, "second");

            await Assert.That(parent.Properties.GetOrDefault<string>(key)).IsEqualTo("initial");
            await Assert.That(first.Properties.GetOrDefault<string>(key)).IsEqualTo("first");
            await Assert.That(second.Properties.GetOrDefault<string>(key)).IsEqualTo("second");
        }
        finally
        {
            KevlarContext.Return(first);
            KevlarContext.Return(second);
            KevlarContext.Return(parent);
        }
    }

    [Test]
    public async Task A_Context_Returned_To_The_Pool_Rejects_Its_Public_Members()
    {
        KevlarContext? retained = null;
        var result = await Shield.Empty.ExecuteWithContextAsync(context =>
        {
            retained = context;
            return new ValueTask<int>(7);
        });

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(retained).IsNotNull();
#if DEBUG
        await AssertPooledAccessThrows(() => retained!.CancellationToken);
        await AssertPooledAccessThrows(() => retained!.Properties);
        await AssertPooledAccessThrows(() => retained!.ShieldName);
        await AssertPooledAccessThrows(() => retained!.TimeProvider);
        await AssertPooledAccessThrows(() => retained!.IsSynchronous);
#else
        await Assert.That(retained!.ShieldName).IsNull();
#endif
    }

    [Test]
    public async Task Renting_A_Pooled_Context_Makes_It_Usable_Again()
    {
        KevlarContext.Return(KevlarContext.Rent(default, isSynchronous: false, TimeProvider.System, "first"));
        var reused = KevlarContext.Rent(default, isSynchronous: true, TimeProvider.System, "second");

        try
        {
            await Assert.That(reused.ShieldName).IsEqualTo("second");
            await Assert.That(reused.IsSynchronous).IsTrue();
            await Assert.That(reused.Properties.TryGet(DirtyProperty, out _)).IsFalse();
            await Assert.That(ReferenceEquals(reused.TimeProvider, TimeProvider.System)).IsTrue();
        }
        finally
        {
            KevlarContext.Return(reused);
        }
    }

#if DEBUG
    private static async Task AssertPooledAccessThrows(Func<object?> access)
    {
        var error = await Assert.That(access).Throws<InvalidOperationException>();
        await Assert.That(error!.Message).Contains("returned to Kevlar's context pool");
    }
#endif

    private static async Task AssertResetAfter(ExitPath exitPath)
    {
        var observer = new ContextObserverStrategy(DirtyProperty);
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var shield = CreateShield(exitPath, observer)
            .WithName("dirty-context")
            .WithTimeProvider(timeProvider);

        switch (exitPath)
        {
            case ExitPath.AsyncSuccess:
                await shield.ExecuteAsync(_ => new ValueTask<int>(42), cancellation.Token);
                break;
            case ExitPath.AsyncFailure:
                await Assert.That(async () => await shield.ExecuteAsync<int>(
                    _ => throw new InvalidOperationException("failure"),
                    cancellation.Token)).Throws<InvalidOperationException>();
                break;
            case ExitPath.AsyncCancellation:
                await Assert.That(async () => await shield.ExecuteAsync<int>(token =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return new ValueTask<int>(42);
                }, cancellation.Token)).Throws<OperationCanceledException>();
                break;
            case ExitPath.CallbackFailure:
                await Assert.That(async () => await shield.ExecuteAsync<int>(
                    _ => throw new InvalidOperationException("handled"),
                    cancellation.Token)).Throws<ApplicationException>();
                break;
            case ExitPath.OutcomeFailure:
                var outcome = await shield.ExecuteOutcomeAsync<int>(
                    _ => throw new InvalidOperationException("captured"),
                    cancellation.Token);
                await Assert.That(outcome.IsSuccess).IsFalse();
                break;
            case ExitPath.SyncSuccess:
                await Assert.That(shield.Execute(_ => 42, cancellation.Token)).IsEqualTo(42);
                break;
            case ExitPath.SyncFailure:
                await Assert.That(() => shield.Execute<int>(
                    _ => throw new InvalidOperationException("sync failure"),
                    cancellation.Token)).Throws<InvalidOperationException>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(exitPath), exitPath, null);
        }

        var snapshot = observer.Snapshot
            ?? throw new InvalidOperationException($"{exitPath}: observer did not run.");
        await Assert.That(snapshot.Name).IsEqualTo("dirty-context");
        await Assert.That(ReferenceEquals(snapshot.TimeProvider, timeProvider)).IsTrue();
        await Assert.That(snapshot.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(snapshot.IsSynchronous).IsEqualTo(exitPath is ExitPath.SyncSuccess or ExitPath.SyncFailure);
        await Assert.That(snapshot.PropertiesWereInitiallyClean).IsTrue();

        var returned = observer.Context!;
        KevlarContext.AllowPooledInspection(returned);
        await Assert.That(returned.CancellationToken).IsEqualTo(default(CancellationToken));
        await Assert.That(returned.IsSynchronous).IsFalse();
        await Assert.That(returned.ShieldName).IsNull();
        await Assert.That(ReferenceEquals(returned.TimeProvider, TimeProvider.System)).IsTrue();
        await Assert.That(returned.Properties.TryGet(DirtyProperty, out _)).IsFalse();
    }

    private static Shield CreateShield(ExitPath exitPath, ContextObserverStrategy observer)
    {
        var shield = Shield.Use(observer);
        return exitPath == ExitPath.CallbackFailure
            ? shield.Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = _ => throw new ApplicationException("callback failure");
            })
            : shield;
    }

    private enum ExitPath
    {
        AsyncSuccess,
        AsyncFailure,
        AsyncCancellation,
        CallbackFailure,
        OutcomeFailure,
        SyncSuccess,
        SyncFailure,
    }

    private sealed class ContextObserverStrategy(KevlarKey<string> dirtyProperty) : Strategy
    {
        public KevlarContext? Context { get; private set; }

        public ContextSnapshot? Snapshot { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Context = context;
            Snapshot = new ContextSnapshot(
                context.ShieldName,
                context.TimeProvider,
                context.CancellationToken,
                context.IsSynchronous,
                !context.Properties.TryGet(dirtyProperty, out _));
            context.Properties.Set(dirtyProperty, "dirty");
            return next.InvokeAsync(context);
        }
    }

    private sealed class PressureStrategy : Strategy
    {
        private readonly int _expectedCount;
        private readonly KevlarKey<string> _dirtyProperty;
        private readonly bool _setDirty;
        private readonly CancellationToken _expectedToken;
        private readonly TimeProvider _expectedTimeProvider;
        private int _entered;
        private int _dirtyContexts;

        public PressureStrategy(
            int expectedCount,
            KevlarKey<string> dirtyProperty,
            bool setDirty,
            CancellationToken expectedToken,
            TimeProvider expectedTimeProvider)
        {
            _expectedCount = expectedCount;
            _dirtyProperty = dirtyProperty;
            _setDirty = setDirty;
            _expectedToken = expectedToken;
            _expectedTimeProvider = expectedTimeProvider;
        }

        public ConcurrentBag<KevlarContext> Contexts { get; } = [];

        public TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DirtyContexts => Volatile.Read(ref _dirtyContexts);

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Contexts.Add(context);
            if (context.Properties.TryGet(_dirtyProperty, out _)
                || context.ShieldName != (_setDirty ? "dirty-pressure" : "clean-pressure")
                || context.IsSynchronous
                || context.CancellationToken != _expectedToken
                || !ReferenceEquals(context.TimeProvider, _expectedTimeProvider))
            {
                Interlocked.Increment(ref _dirtyContexts);
            }

            if (_setDirty)
            {
                context.Properties.Set(_dirtyProperty, "dirty");
            }

            if (Interlocked.Increment(ref _entered) == _expectedCount)
            {
                AllEntered.TrySetResult();
            }

            await Release.Task.ConfigureAwait(false);
            return await next.InvokeAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class ReentrantStrategy(KevlarKey<string> key) : Strategy
    {
        public bool InnerUsedDifferentContext { get; private set; }

        public bool OuterPropertySurvived { get; private set; }

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            context.Properties.Set(key, "outer");
            var inner = new InnerObserverStrategy(context, key);
            _ = await Shield.Use(inner).ExecuteAsync(_ => new ValueTask<int>(1)).ConfigureAwait(false);

            InnerUsedDifferentContext = inner.UsedDifferentContext;
            OuterPropertySurvived = context.Properties.GetOrDefault<string>(key) == "outer";
            return await next.InvokeAsync(context).ConfigureAwait(false);
        }
    }

    private sealed class InnerObserverStrategy(KevlarContext outer, KevlarKey<string> key) : Strategy
    {
        public bool UsedDifferentContext { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            UsedDifferentContext = !ReferenceEquals(context, outer)
                && !context.Properties.TryGet(key, out _);
            context.Properties.Set(key, "inner");
            return next.InvokeAsync(context);
        }
    }

    private sealed record ContextSnapshot(
        string? Name,
        TimeProvider TimeProvider,
        CancellationToken CancellationToken,
        bool IsSynchronous,
        bool PropertiesWereInitiallyClean);
}
