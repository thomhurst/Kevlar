using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

/// <summary>
/// Guards the API refinements: the unified When/Or clause grammar, WhenDefault, typed retry
/// events, the MaxDelay absolute cap, and Compose preserving names, time providers and clauses.
/// </summary>
public class NewApiTests
{
    [Test]
    public async Task When_And_Or_Compose_On_The_Untyped_Builder()
    {
        var attempts = 0;
        var shield = Shield
            .When<ArgumentException>()
            .Or<InvalidOperationException>()
            .OrWhen(exception => exception is TimeoutException)
            .Retry(3, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw attempts switch
            {
                1 => new ArgumentException(),
                2 => new InvalidOperationException(),
                3 => (Exception)new TimeoutException(),
                _ => new ShortCircuitException(),
            };
        })).Throws<ShortCircuitException>();

        // All three clause styles handled their exception; the fourth type broke the loop.
        await Assert.That(attempts).IsEqualTo(4);
    }

    [Test]
    public async Task When_And_Or_Compose_On_The_Typed_Builder()
    {
        var attempts = 0;
        var shield = Shield.For<int>()
            .When<ArgumentException>()
            .Or<InvalidOperationException>()
            .OrResult(0)
            .Retry(3, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts switch
            {
                1 => new ValueTask<int>(0),
                2 => throw new ArgumentException(),
                3 => throw new InvalidOperationException(),
                _ => new ValueTask<int>(42),
            };
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(4);
    }

    [Test]
    public async Task WhenDefault_Retries_Null_Results()
    {
        var attempts = 0;
        var shield = Shield.For<string?>().WhenDefault().Retry(2, Backoff.None);

        var result = await shield.ExecuteAsync(_ => new ValueTask<string?>(attempts++ < 2 ? null : "loaded"));

        await Assert.That(result).IsEqualTo("loaded");
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task WhenDefault_Matches_Default_Value_Types()
    {
        var attempts = 0;
        var shield = Shield.For<int>().WhenDefault().Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(attempts++ == 0 ? 0 : 7));

        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task Typed_Retry_Events_Carry_The_Typed_Outcome()
    {
        var seen = new List<Outcome<int>>();
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .OrResult(0)
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.Backoff = Backoff.None;
                options.OnRetry = retry => seen.Add(retry.Outcome);
            });

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts switch
            {
                1 => new ValueTask<int>(0),
                2 => throw new InvalidOperationException("boom"),
                _ => new ValueTask<int>(9),
            };
        });

        await Assert.That(result).IsEqualTo(9);
        await Assert.That(seen.Count).IsEqualTo(2);
        await Assert.That(seen[0].IsSuccess).IsTrue();
        await Assert.That(seen[0].Result).IsEqualTo(0);
        await Assert.That(seen[1].IsSuccess).IsFalse();
        await Assert.That(seen[1].Exception!.Message).IsEqualTo("boom");
    }

    [Test]
    public async Task Typed_Async_Retry_Events_And_Delay_Generators_Are_Typed_Too()
    {
        var generatorSaw = new List<int>();
        var asyncEvents = 0;
        var shield = Shield.For<int>()
            .WhenResult(result => result < 0)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = retry =>
                {
                    generatorSaw.Add(retry.Outcome.Result);
                    return null;
                };
                options.OnRetryAsync = async _ =>
                {
                    await Task.Yield();
                    asyncEvents++;
                };
            });

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(attempts++ == 0 ? -5 : 3));

        await Assert.That(result).IsEqualTo(3);
        await Assert.That(generatorSaw).IsEquivalentTo([-5]);
        await Assert.That(asyncEvents).IsEqualTo(1);
    }

    [Test]
    public async Task MaxDelay_Caps_Generator_Supplied_Delays()
    {
        var reportedDelays = new List<TimeSpan>();
        var shield = Shield.For<int>()
            .WhenResult(0)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.MaxDelay = TimeSpan.FromMilliseconds(1);
                options.DelayGenerator = _ => TimeSpan.FromHours(6);
                options.OnRetry = retry => reportedDelays.Add(retry.Delay);
            });

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(attempts++ == 0 ? 0 : 4));

        // A generator (e.g. a hostile Retry-After header) cannot exceed the absolute cap.
        await Assert.That(result).IsEqualTo(4);
        await Assert.That(reportedDelays).IsEquivalentTo([TimeSpan.FromMilliseconds(1)]);
    }

    [Test]
    public async Task Compose_Keeps_The_First_Name_And_TimeProvider()
    {
        var time = new FakeTimeProvider();
        var named = Shield.Timeout(TimeSpan.FromSeconds(5)).WithName("outer").WithTimeProvider(time);
        var other = Shield.Retry(1, Backoff.None);

        var composed = Shield.Compose(other, named);

        await Assert.That(composed.Name).IsEqualTo("outer");
        await Assert.That(ReferenceEquals(composed.Time, time)).IsTrue();
    }

    [Test]
    public async Task Compose_Keeps_The_Last_Ambient_Clause_For_Further_Chaining()
    {
        var withClause = Shield.When<ArgumentException>().Timeout(TimeSpan.FromMinutes(1));
        var composed = Shield.Compose(Shield.Timeout(TimeSpan.FromMinutes(1)), withClause).Retry(1, Backoff.None);

        var attempts = 0;
        await Assert.That(async () => await composed.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw attempts == 1 ? new ArgumentException() : new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        // The retry appended after Compose inherited the ArgumentException clause:
        // it retried the first failure and surfaced the unhandled second one.
        await Assert.That(attempts).IsEqualTo(2);
    }

    private sealed class ShortCircuitException : Exception;
}
