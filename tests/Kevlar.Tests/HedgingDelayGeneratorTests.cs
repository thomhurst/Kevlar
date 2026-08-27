using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class HedgingDelayGeneratorTests
{
    private static readonly KevlarKey<TimeSpan> DelayKey = new("hedge-delay");

    [Test]
    public async Task DelayGenerator_Controls_Each_Hedge_Start_Time()
    {
        var time = new FakeTimeProvider();
        var origin = time.GetUtcNow();
        var starts = new ConcurrentQueue<TimeSpan>();
        var attempts = new AsyncCounter("hedge attempts");
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.DelayGenerator = hedge => new(hedge.AttemptNumber == 1
                ? TimeSpan.FromMilliseconds(100)
                : TimeSpan.FromMilliseconds(300));
        }).WithTimeProvider(time);

        var execution = shield.ExecuteAsync(async token =>
        {
            var attempt = attempts.Signal();
            starts.Enqueue(time.GetUtcNow() - origin);
            if (attempt < 3)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        }).AsTask();

        time.Advance(TimeSpan.FromMilliseconds(99));
        await Assert.That(attempts.Count).IsEqualTo(1);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await attempts.WaitForAsync(2);
        time.Advance(TimeSpan.FromMilliseconds(299));
        await Assert.That(attempts.Count).IsEqualTo(2);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await Assert.That(await execution).IsEqualTo(3);
        await Assert.That(starts).IsEquivalentTo([
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(400),
        ]);
    }

    [Test]
    public async Task DelayGenerator_Infinite_Waits_For_Previous_Attempt_To_Complete()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new AsyncCounter("hedge attempts");
        var generatorCalls = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.DelayGenerator = _ =>
            {
                generatorCalls++;
                return new(Timeout.InfiniteTimeSpan);
            };
        });

        var execution = shield.ExecuteAsync(async _ =>
        {
            var attempt = attempts.Signal();
            if (attempt == 1)
            {
                await release.Task;
                throw new InvalidOperationException("primary failed");
            }

            return attempt;
        }).AsTask();

        await Assert.That(attempts.Count).IsEqualTo(1);
        await Assert.That(generatorCalls).IsEqualTo(1);
        release.SetResult();

        await Assert.That(await execution).IsEqualTo(2);
    }

    [Test]
    public async Task DelayGenerator_Zero_Fires_Immediately()
    {
        var attempts = new AsyncCounter("hedge attempts");
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.FromDays(1);
            options.DelayGenerator = _ => new(TimeSpan.Zero);
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            var attempt = attempts.Signal();
            if (attempt == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        });

        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task DelayGenerator_Receives_Attempt_Context_And_Elapsed()
    {
        var time = new FakeTimeProvider();
        var observed = new List<(int Attempt, TimeSpan Delay, TimeSpan Elapsed)>();
        var attempts = new AsyncCounter("hedge attempts");
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.DelayGenerator = hedge =>
            {
                var delay = hedge.Context.Properties.GetOrDefault(DelayKey);
                observed.Add((hedge.AttemptNumber, delay, hedge.Elapsed));
                return new(delay);
            };
        }).WithTimeProvider(time);

        var execution = shield.ExecuteWithContextAsync(
            TimeSpan.FromMilliseconds(50),
            static (delay, properties) => properties.Set(DelayKey, delay),
            async (_, context) =>
            {
                var attempt = attempts.Signal();
                if (attempt < 3)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                }

                return attempt;
            }).AsTask();

        time.Advance(TimeSpan.FromMilliseconds(50));
        await attempts.WaitForAsync(2);
        time.Advance(TimeSpan.FromMilliseconds(50));

        await Assert.That(await execution).IsEqualTo(3);
        await Assert.That(observed).IsEquivalentTo([
            (1, TimeSpan.FromMilliseconds(50), TimeSpan.Zero),
            (2, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)),
        ]);
    }

    [Test]
    public async Task DelayGenerator_Reads_Properties_From_Context()
    {
        var observed = TimeSpan.Zero;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.DelayGenerator = hedge =>
            {
                observed = hedge.Context.Properties.GetOrDefault(DelayKey);
                return new(TimeSpan.Zero);
            };
        });

        var attempts = 0;
        var result = await shield.ExecuteWithContextAsync(
            TimeSpan.FromMilliseconds(125),
            static (delay, properties) => properties.Set(DelayKey, delay),
            async (_, context) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                }

                return attempts;
            });

        await Assert.That(result).IsEqualTo(2);
        await Assert.That(observed).IsEqualTo(TimeSpan.FromMilliseconds(125));
    }

    [Test]
    public async Task DelayGenerator_Is_Awaited_And_Cancellable()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.DelayGenerator = async hedge =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, hedge.Context.CancellationToken);
                return TimeSpan.Zero;
            };
        });

        var execution = shield.ExecuteAsync(async token =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        }, cancellation.Token).AsTask();

        await entered.Task;
        cancellation.Cancel();

        await Assert.That(async () => await execution).Throws<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task DelayGenerator_Exception_Surfaces_As_Execution_Failure()
    {
        var expected = new InvalidOperationException("delay failed");
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.DelayGenerator = _ => throw expected;
        });

        var caught = await Assert.That(async () => await shield.ExecuteAsync(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        })).Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(caught, expected)).IsTrue();
    }

    [Test]
    public async Task Negative_Delay_From_Generator_Is_Clamped_To_Zero()
    {
        var attempts = new AsyncCounter("hedge attempts");
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.DelayGenerator = _ => new(TimeSpan.FromTicks(-2));
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            var attempt = attempts.Signal();
            if (attempt == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        });

        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Delay_Above_MaximumDelay_Is_Clamped()
    {
        var time = new FakeTimeProvider();
        var attempts = new AsyncCounter("hedge attempts");
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.DelayGenerator = _ => new(TimeSpan.MaxValue);
        }).WithTimeProvider(time);

        var execution = shield.ExecuteAsync(async token =>
        {
            var attempt = attempts.Signal();
            if (attempt == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        }).AsTask();

        var maximum = Kevlar.Internal.DelayHelper.MaximumDelay;
        time.Advance(maximum - TimeSpan.FromMilliseconds(1));
        await Assert.That(attempts.Count).IsEqualTo(1);
        time.Advance(TimeSpan.FromMilliseconds(1));

        await Assert.That(await execution).IsEqualTo(2);
    }

    [Test]
    public async Task Generator_Not_Called_After_Winner_Completes()
    {
        var calls = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.DelayGenerator = _ =>
            {
                calls++;
                return new(TimeSpan.Zero);
            };
        });

        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(42))).IsEqualTo(42);
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task Typed_DelayGenerator_Controls_Hedge()
    {
        var attempts = new AsyncCounter("typed hedge attempts");
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.DelayGenerator = _ => new(TimeSpan.Zero);
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            var attempt = attempts.Signal();
            if (attempt == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        });

        await Assert.That(result).IsEqualTo(2);
    }
}
