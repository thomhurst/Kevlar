using System.Reflection;

namespace Kevlar.Tests;

public class BuilderParityTests
{
    [Test]
    public void Builders_Expose_The_Missing_Shield_Overload_Signatures()
    {
        AssertSignature(typeof(ShieldBuilder), nameof(ShieldBuilder.Timeout), typeof(Action<TimeoutOptions>));
        AssertSignature(typeof(ShieldBuilder<int>), nameof(ShieldBuilder<int>.Timeout), typeof(Action<TimeoutOptions>));
        AssertSignature(typeof(ShieldBuilder), nameof(ShieldBuilder.Use), typeof(Strategy));
        AssertSignature(typeof(ShieldBuilder<int>), nameof(ShieldBuilder<int>.Use), typeof(Strategy));
        AssertSignature(
            typeof(ShieldBuilder),
            nameof(ShieldBuilder.Fallback),
            typeof(Func<CancellationToken, ValueTask>));
        AssertSignature(
            typeof(ShieldBuilder),
            nameof(ShieldBuilder.Fallback),
            typeof(Func<CancellationToken, ValueTask>),
            typeof(Action<FallbackOptions>));

    }

    [Test]
    public async Task Builder_Timeout_Configure_Applies_TimeoutGenerator()
    {
        var generatorCalls = 0;
        var shield = Shield.When<InvalidOperationException>()
            .Timeout(options => options.TimeoutGenerator = _ =>
            {
                generatorCalls++;
                return new ValueTask<TimeSpan>(TimeSpan.FromMinutes(1));
            })
            .Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(generatorCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Builder_Use_Strategy_Preserves_The_Ambient_Clause()
    {
        var attempts = 0;
        var shield = Shield.When<InvalidOperationException>()
            .Use(new PassThroughStrategy())
            .Retry(1, Backoff.None);

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
    public async Task Typed_Builder_Timeout_And_Use_Preserve_The_Result_Clause()
    {
        var generatorCalls = 0;
        var attempts = 0;
        var shield = Shield.For<int>()
            .WhenResult(0)
            .Timeout(options => options.TimeoutGenerator = _ =>
            {
                generatorCalls++;
                return new ValueTask<TimeSpan>(TimeSpan.FromMinutes(1));
            })
            .Use(new PassThroughStrategy())
            .Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(attempts++ == 0 ? 0 : 42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(generatorCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Builder_Fallback_Without_Exception_Argument_Runs()
    {
        var fallbackCalls = 0;
        var notificationCalls = 0;
        var bare = Shield.When<InvalidOperationException>()
            .Fallback(_ =>
            {
                fallbackCalls++;
                return ValueTask.CompletedTask;
            });
        var configured = Shield.When<InvalidOperationException>()
            .Fallback(
                _ =>
                {
                    fallbackCalls++;
                    return ValueTask.CompletedTask;
                },
                options => options.OnFallback = _ =>
                {
                    notificationCalls++;
                    return default;
                });

        await bare.ExecuteAsync(static _ => ValueTask.FromException(new InvalidOperationException()));
        await configured.ExecuteAsync(static _ => ValueTask.FromException(new InvalidOperationException()));

        await Assert.That(fallbackCalls).IsEqualTo(2);
        await Assert.That(notificationCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Builder_Timeout_Configure_Has_The_Same_Description_As_Shield_Timeout()
    {
        var fromBuilder = Shield.When<InvalidOperationException>()
            .Timeout(static options => options.TimeoutGenerator =
                static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1)))
            .Retry(3);
        var fromShield = Shield.Timeout(static options => options.TimeoutGenerator =
                static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1)))
            .When<InvalidOperationException>()
            .Retry(3);

        const string expected =
            "Timeout(dynamic) → [when InvalidOperationException] " +
            "Retry(3, exponential 250ms ×2, equal jitter, cap 30s)";
        await Assert.That(fromBuilder.ToString()).IsEqualTo(expected);
        await Assert.That(fromShield.ToString()).IsEqualTo(expected);
    }

    private static void AssertSignature(Type type, string methodName, params Type[] parameterTypes)
    {
        var method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            binder: null,
            parameterTypes,
            modifiers: null);
        if (method is null)
        {
            throw new InvalidOperationException(
                $"{type.Name}.{methodName}({string.Join(", ", parameterTypes.Select(static parameter => parameter.Name))}) was not found.");
        }
    }

    private sealed class PassThroughStrategy : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }
}
