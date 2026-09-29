namespace Kevlar.Tests;

/// <summary>
/// Covers the per-thread context cache in front of the shared pool and the strategy-free
/// context execution fast path: recycled contexts must stay isolated and clean.
/// </summary>
[NotInParallel]
public class ContextRecyclingTests
{
    private static readonly KevlarKey<string> Seed = new("recycling-seed");
    private static readonly KevlarKey<string> ChildValue = new("recycling-child");
    private static readonly KevlarKey<string> GrandchildValue = new("recycling-grandchild");

    [Test]
    public async Task Repeated_Nested_Executions_Use_Distinct_Clean_Contexts_At_Every_Depth()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            var observation = Shield.Empty.ExecuteWithContext(
                $"seed-{iteration}",
                static (seed, properties) => properties.Set(Seed, seed),
                static (seed, parent) =>
                {
                    var parentCount = parent.Properties.Count;
                    var (childCount, child, grandchild) = Shield.Empty.ExecuteWithContext(
                        parent,
                        seed,
                        static (_, child) =>
                        {
                            var countOnEntry = child.Properties.Count;
                            child.Properties.Set(ChildValue, "child");
                            var grandchild = Shield.Empty.ExecuteWithContext(
                                child,
                                static grandchild =>
                                {
                                    grandchild.Properties.Set(GrandchildValue, "grandchild");
                                    return grandchild;
                                });
                            return (countOnEntry, child, grandchild);
                        });

                    return new NestedObservation(
                        parent,
                        child,
                        grandchild,
                        parentCount,
                        childCount,
                        parent.Properties.GetOrDefault<string>(Seed),
                        parent.Properties.GetOrDefault<string>(ChildValue),
                        parent.Properties.GetOrDefault<string>(GrandchildValue));
                });

            await Assert.That(observation.ParentCountOnEntry).IsEqualTo(1);
            await Assert.That(observation.ChildCountOnEntry).IsEqualTo(1);
            await Assert.That(observation.Seed).IsEqualTo($"seed-{iteration}");
            await Assert.That(observation.ChildValue).IsEqualTo("child");
            await Assert.That(observation.GrandchildValue).IsEqualTo("grandchild");
            await Assert.That(ReferenceEquals(observation.Parent, observation.Child)).IsFalse();
            await Assert.That(ReferenceEquals(observation.Parent, observation.Grandchild)).IsFalse();
            await Assert.That(ReferenceEquals(observation.Child, observation.Grandchild)).IsFalse();

            foreach (var context in new[] { observation.Parent, observation.Child, observation.Grandchild })
            {
                KevlarContext.AllowPooledInspection(context);
                await Assert.That(context.Properties.Count).IsEqualTo(0);
                await Assert.That(context.ShieldName).IsNull();
                await Assert.That(context.CancellationToken).IsEqualTo(default(CancellationToken));
            }
        }
    }

    [Test]
    public async Task Contexts_Returned_On_Other_Threads_Are_Clean_When_Rented_Again()
    {
        using var cancellation = new CancellationTokenSource();
        var dirty = Shield.Empty.WithName("dirty-recycling");
        for (var i = 0; i < 64; i++)
        {
            await dirty.ExecuteWithContextAsync(
                "dirty",
                static (value, properties) => properties.Set(Seed, value),
                static async (_, context) =>
                {
                    await Task.Yield();
                    context.Properties.Set(ChildValue, "dirty");
                    return 0;
                },
                cancellation.Token);
        }

        var dirtyEntries = 0;
        var tasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(async () =>
            {
                var clean = await Shield.Empty.ExecuteWithContextAsync(static context =>
                    new ValueTask<bool>(context.Properties.Count == 0
                        && context.ShieldName is null
                        && context.CancellationToken == default));
                if (!clean)
                {
                    Interlocked.Increment(ref dirtyEntries);
                }
            }))
            .ToArray();
        await Task.WhenAll(tasks);

        await Assert.That(dirtyEntries).IsEqualTo(0);
    }

    [Test]
    public async Task Strategy_Free_Context_Execution_Surfaces_Every_Delegate_Outcome()
    {
        var synchronousThrow = await Assert.That(async () => await Shield.Empty.ExecuteWithContextAsync<int>(
                static _ => throw new InvalidOperationException("sync")))
            .Throws<InvalidOperationException>();
        await Assert.That(synchronousThrow!.Message).IsEqualTo("sync");

        var faulted = await Assert.That(async () => await Shield.Empty.ExecuteWithContextAsync(
                static _ => ValueTask.FromException<int>(new InvalidOperationException("faulted"))))
            .Throws<InvalidOperationException>();
        await Assert.That(faulted!.Message).IsEqualTo("faulted");

        KevlarContext? pendingContext = null;
        var pending = await Shield.Empty.ExecuteWithContextAsync(async context =>
        {
            pendingContext = context;
            await Task.Yield();
            context.Properties.Set(Seed, "pending");
            return context.Properties.GetOrDefault<string>(Seed);
        });
        await Assert.That(pending).IsEqualTo("pending");
        KevlarContext.AllowPooledInspection(pendingContext!);
        await Assert.That(pendingContext!.Properties.Count).IsEqualTo(0);

        var syncThrow = await Assert.That(() => Shield.Empty.ExecuteWithContext<int>(
                static _ => throw new InvalidOperationException("sync context")))
            .Throws<InvalidOperationException>();
        await Assert.That(syncThrow!.Message).IsEqualTo("sync context");
    }

    private sealed record NestedObservation(
        KevlarContext Parent,
        KevlarContext Child,
        KevlarContext Grandchild,
        int ParentCountOnEntry,
        int ChildCountOnEntry,
        string? Seed,
        string? ChildValue,
        string? GrandchildValue);
}
