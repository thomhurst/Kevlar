using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

/// <summary>
/// Guards the API refinements: the unified When/Or clause grammar, WhenResultIsDefault, typed retry
/// events, the MaxDelay absolute cap, the context-only ExecuteWithContext overloads, and Compose
/// preserving metadata while sealing clauses.
/// </summary>
public class NewApiTests
{
    [Test]
    public async Task For_Returns_Typed_Shield_And_Builders_Expose_Only_Or_Continuations()
    {
        Shield<int> typed = Shield.For<int>();
        await Assert.That(typed.GetType()).IsEqualTo(typeof(Shield<int>));

        var untypedBuilderWhenMethods = typeof(ShieldBuilder)
            .GetMethods(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(static method => method.Name.StartsWith("When", StringComparison.Ordinal))
            .Select(static method => method.Name)
            .ToArray();
        var typedBuilderWhenMethods = typeof(ShieldBuilder<int>)
            .GetMethods(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(static method => method.Name.StartsWith("When", StringComparison.Ordinal))
            .Select(static method => method.Name)
            .ToArray();
        var shieldOrMethods = new[] { typeof(Shield), typeof(Shield<int>) }
            .SelectMany(static type => type.GetMethods(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(static method => method.Name.StartsWith("Or", StringComparison.Ordinal))
            .Select(static method => method.Name)
            .ToArray();

        await Assert.That(untypedBuilderWhenMethods).IsEmpty();
        await Assert.That(typedBuilderWhenMethods).IsEmpty();
        await Assert.That(shieldOrMethods).IsEmpty();

        var attempts = 0;
        var shield = Shield.For<int>().Retry(1, Backoff.None);
        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task When_And_Or_Compose_On_The_Untyped_Builder()
    {
        var attempts = 0;
        var shield = Shield
            .When<ArgumentException>()
            .Or<InvalidOperationException>()
            .Or(exception => exception is TimeoutException)
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
    public async Task WhenResultIsDefault_Retries_Null_Results()
    {
        var attempts = 0;
        var shield = Shield.For<string?>().WhenResultIsDefault().Retry(2, Backoff.None);

        var result = await shield.ExecuteAsync(_ => new ValueTask<string?>(attempts++ < 2 ? null : "loaded"));

        await Assert.That(result).IsEqualTo("loaded");
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task WhenResultIsNull_Retries_Null_Results()
    {
        var attempts = 0;
        var shield = Shield.For<string?>().WhenResultIsNull().Retry(2, Backoff.None);

        var result = await shield.ExecuteAsync(_ => new ValueTask<string?>(attempts++ < 2 ? null : "loaded"));

        await Assert.That(result).IsEqualTo("loaded");
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task OrResultIsNull_Extends_An_Exception_Clause()
    {
        var attempts = 0;
        var shield = Shield.For<string?>()
            .When<InvalidOperationException>()
            .OrResultIsNull()
            .Retry(2, Backoff.None);

        var result = await shield.ExecuteAsync(_ => attempts++ switch
        {
            0 => throw new InvalidOperationException(),
            1 => new ValueTask<string?>((string?)null),
            _ => new ValueTask<string?>("loaded"),
        });

        await Assert.That(result).IsEqualTo("loaded");
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task WhenResultIsDefault_Matches_Default_Value_Types()
    {
        var attempts = 0;
        var shield = Shield.For<int>().WhenResultIsDefault().Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(attempts++ == 0 ? 0 : 7));

        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task WhenAnyError_Resets_Untyped_Handling_To_The_Default()
    {
        var timeProvider = new FakeTimeProvider();
        var original = Shield.When<ArgumentException>()
            .Retry(1, Backoff.None)
            .WithName("reset-test")
            .WithTimeProvider(timeProvider);
        var reset = original.WhenAnyError();

        await Assert.That(reset.Name).IsEqualTo(original.Name);
        await Assert.That(ReferenceEquals(reset.Time, original.Time)).IsTrue();
        await Assert.That(ReferenceEquals(reset.Strategies, original.Strategies)).IsTrue();
        await Assert.That(reset.ToString()).IsEqualTo(original.ToString());

        var attempts = 0;
        var shield = reset.Retry(1, Backoff.None);

        await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42);
        });

        await Assert.That(attempts).IsEqualTo(2);

        attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new OperationCanceledException();
        })).Throws<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task WhenAnyError_Resets_Typed_Handling_And_Preserves_Shield_State()
    {
        var timeProvider = new FakeTimeProvider();
        var original = Shield.For<int>()
            .When<ArgumentException>()
            .Retry(1, Backoff.None)
            .WithName("reset-test")
            .WithTimeProvider(timeProvider);

        var reset = original.WhenAnyError();

        await Assert.That(reset.Name).IsEqualTo(original.Name);
        await Assert.That(ReferenceEquals(reset.Time, original.Time)).IsTrue();
        await Assert.That(ReferenceEquals(reset.Strategies, original.Strategies)).IsTrue();
        await Assert.That(reset.ToString()).IsEqualTo(original.ToString());

        var attempts = 0;
        var result = await reset.Retry(1, Backoff.None).ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task WhenAnyError_Reset_Survives_Compose()
    {
        var outer = Shield.When<TimeoutException>().Timeout(TimeSpan.FromMinutes(1));
        var reset = Shield.When<ArgumentException>()
            .Timeout(TimeSpan.FromMinutes(1))
            .WhenAnyError();
        var shield = Shield.Compose(outer, reset).Retry(1, Backoff.None);
        var attempts = 0;

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task WhenAnyError_Reset_Survives_Typed_Wrap()
    {
        var outer = Shield.For<int>().When<TimeoutException>().Timeout(TimeSpan.FromMinutes(1));
        var reset = Shield.For<int>()
            .When<ArgumentException>()
            .Timeout(TimeSpan.FromMinutes(1))
            .WhenAnyError();
        var shield = outer.Wrap(reset).Retry(1, Backoff.None);
        var attempts = 0;

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
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
                options.OnRetry = retry =>
                {
                    seen.Add(retry.Outcome);
                    return default;
                };
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
                    return default;
                };
                options.OnRetry = async _ =>
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
    public async Task Retry_Option_Types_Are_Siblings_And_Preserve_Delegate_Identity()
    {
        Func<RetryEvent, ValueTask> untypedOnRetry = static _ => ValueTask.CompletedTask;
        Func<RetryEvent, ValueTask<TimeSpan?>> untypedDelayGenerator =
            static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero);
        Func<RetryEvent<int>, ValueTask> typedOnRetry = static _ => ValueTask.CompletedTask;
        Func<RetryEvent<int>, ValueTask<TimeSpan?>> typedDelayGenerator =
            static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero);

        var untyped = new RetryOptions
        {
            OnRetry = untypedOnRetry,
            DelayGenerator = untypedDelayGenerator,
        };
        var typed = new RetryOptions<int>
        {
            OnRetry = typedOnRetry,
            DelayGenerator = typedDelayGenerator,
        };

        await Assert.That(typeof(RetryOptions).BaseType).IsEqualTo(typeof(object));
        await Assert.That(typeof(RetryOptions<int>).BaseType).IsEqualTo(typeof(object));
        await Assert.That(typeof(RetryOptions).IsAssignableFrom(typeof(RetryOptions<int>))).IsFalse();
        await Assert.That(ReferenceEquals(untyped.OnRetry, untypedOnRetry)).IsTrue();
        await Assert.That(ReferenceEquals(untyped.DelayGenerator, untypedDelayGenerator)).IsTrue();
        await Assert.That(ReferenceEquals(typed.OnRetry, typedOnRetry)).IsTrue();
        await Assert.That(ReferenceEquals(typed.DelayGenerator, typedDelayGenerator)).IsTrue();
    }

    [Test]
    public async Task Typed_Retry_Callbacks_Keep_Their_Defined_Order()
    {
        var order = new List<string>();
        var shield = Shield.For<int>()
            .WhenResult(-1)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = async _ =>
                {
                    order.Add("DelayGenerator");
                    await Task.Yield();
                    order.Add("DelayGenerator:awaited");
                    return TimeSpan.Zero;
                };
                options.OnRetry = async _ =>
                {
                    order.Add("OnRetry");
                    await Task.Yield();
                    order.Add("OnRetry:awaited");
                };
            });

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(attempts++ == 0 ? -1 : 42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(order).IsEquivalentTo(
            ["DelayGenerator", "DelayGenerator:awaited", "OnRetry", "OnRetry:awaited"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
                options.DelayGenerator = _ => new(TimeSpan.FromHours(6));
                options.OnRetry = retry =>
                {
                    reportedDelays.Add(retry.Delay);
                    return default;
                };
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
    public async Task Compose_Seals_The_Ambient_Clause_For_Further_Chaining()
    {
        var withClause = Shield.When<ArgumentException>().Timeout(TimeSpan.FromMinutes(1));
        var composed = Shield.Compose(Shield.Timeout(TimeSpan.FromMinutes(1)), withClause);
        var shield = composed.Retry(1, Backoff.None);

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42);
        });

        await Assert.That(composed.Ambient).IsNull();
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Or_Accepts_A_Bare_Exception_Predicate_On_Both_Builders()
    {
        // A bare lambda cannot infer TException for Or<TException>(Func<TException, bool>),
        // so this binds to the non-generic Or(Func<Exception, bool>) continuation.
        var untypedAttempts = 0;
        var untyped = Shield
            .When<ArgumentException>()
            .Or(exception => exception is TimeoutException)
            .Retry(2, Backoff.None);

        await Assert.That(async () => await untyped.ExecuteAsync<int>(_ =>
        {
            untypedAttempts++;
            throw untypedAttempts switch
            {
                1 => new ArgumentException(),
                2 => (Exception)new TimeoutException(),
                _ => new ShortCircuitException(),
            };
        })).Throws<ShortCircuitException>();
        await Assert.That(untypedAttempts).IsEqualTo(3);

        var typedAttempts = 0;
        var typed = Shield.For<int>()
            .WhenResult(0)
            .Or(exception => exception is TimeoutException)
            .Retry(2, Backoff.None);

        var result = await typed.ExecuteAsync(_ =>
        {
            typedAttempts++;
            return typedAttempts switch
            {
                1 => new ValueTask<int>(0),
                2 => throw new TimeoutException(),
                _ => new ValueTask<int>(42),
            };
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(typedAttempts).IsEqualTo(3);
    }

    [Test]
    public async Task Or_Uses_Distinct_Names_For_Simple_And_Context_Aware_Predicates()
    {
        foreach (var builderType in new[] { typeof(ShieldBuilder), typeof(ShieldBuilder<int>) })
        {
            var declared = builderType.GetMethods(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly);

            await Assert.That(declared.Any(static method => method.Name == "OrWhen")).IsFalse();

            var simpleContinuations = declared
                .Where(static method => method.Name == "Or" && !method.IsGenericMethodDefinition)
                .Select(static method => method.GetParameters().Single().ParameterType)
                .ToArray();
            var contextType = builderType == typeof(ShieldBuilder)
                ? typeof(Func<HandlingEvent, bool>)
                : typeof(Func<HandlingEvent<int>, bool>);
            var contextContinuations = declared
                .Where(static method => method.Name == "OrContext")
                .Select(static method => method.GetParameters().Single().ParameterType)
                .ToArray();

            await Assert.That(simpleContinuations).IsEquivalentTo([typeof(Func<Exception, bool>)]);
            await Assert.That(contextContinuations).IsEquivalentTo([contextType]);
        }
    }

    [Test]
    public async Task ExecuteWithContext_Reads_The_Context_Without_Property_Ceremony()
    {
        var shield = Shield.Retry(1, Backoff.None).WithName("context-only");
        var typed = Shield.For<int>().Retry(1, Backoff.None).WithName("typed-context-only");

        await Assert.That(await shield.ExecuteWithContextAsync(
            context => new ValueTask<string?>(context.ShieldName))).IsEqualTo("context-only");
        await Assert.That(await shield.ExecuteWithContextAsync(
            context => Task.FromResult(context.ShieldName))).IsEqualTo("context-only");
        await Assert.That(shield.ExecuteWithContext(context => context.ShieldName)).IsEqualTo("context-only");

        await Assert.That(await typed.ExecuteWithContextAsync(
            context => new ValueTask<int>(context.ShieldName!.Length))).IsEqualTo("typed-context-only".Length);
        await Assert.That(await typed.ExecuteWithContextAsync(
            context => Task.FromResult(context.ShieldName!.Length))).IsEqualTo("typed-context-only".Length);
        await Assert.That(typed.ExecuteWithContext(context => context.ShieldName!.Length))
            .IsEqualTo("typed-context-only".Length);

        string? fromValueTaskVoid = null;
        await shield.ExecuteWithContextAsync(context =>
        {
            fromValueTaskVoid = context.ShieldName;
            return ValueTask.CompletedTask;
        });

        string? fromTaskVoid = null;
        await shield.ExecuteWithContextAsync(context =>
        {
            fromTaskVoid = context.ShieldName;
            return Task.CompletedTask;
        });

        string? fromSyncVoid = null;
        shield.ExecuteWithContext(context => { fromSyncVoid = context.ShieldName; });

        await Assert.That(fromValueTaskVoid).IsEqualTo("context-only");
        await Assert.That(fromTaskVoid).IsEqualTo("context-only");
        await Assert.That(fromSyncVoid).IsEqualTo("context-only");
    }

    [Test]
    public async Task ExecuteWithContext_Without_Properties_Still_Retries_And_Threads_Cancellation()
    {
        var shield = Shield.Retry(1, Backoff.None);
        var attempts = 0;

        var result = await shield.ExecuteWithContextAsync(context =>
        {
            attempts++;
            context.CancellationToken.ThrowIfCancellationRequested();
            return attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(7);
        });

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(attempts).IsEqualTo(2);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.That(async () => await shield.ExecuteWithContextAsync(
            context => new ValueTask<int>(1),
            cancellation.Token)).Throws<OperationCanceledException>();
    }

    private sealed class ShortCircuitException : Exception;
}
