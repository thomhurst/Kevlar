namespace Kevlar.Tests;

public class DefaultHandlingTests
{
    [Test]
    public async Task Default_Clause_Does_Not_Retry_Kevlar_Rejections()
    {
        Exception[] rejections =
        [
            new CircuitOpenException(TimeSpan.FromSeconds(1), isIsolated: false, lastException: null),
            new RateLimitExceededException(TimeSpan.FromSeconds(1)),
            new ConcurrencyLimitExceededException(),
        ];

        foreach (var rejection in rejections)
        {
            var calls = 0;
            var retries = 0;
            var shield = Shield.Retry(options =>
            {
                options.MaxRetries = 3;
                options.Backoff = Backoff.None;
                options.OnRetry = _ =>
                {
                    retries++;
                    return default;
                };
            });

            var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
            {
                calls++;
                throw rejection;
            });

            await Assert.That(ReferenceEquals(outcome.Exception, rejection)).IsTrue();
            await Assert.That(calls).IsEqualTo(1);
            await Assert.That(retries).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Default_Clause_Does_Not_Retry_Fatal_Exceptions()
    {
        Exception[] fatalExceptions =
        [
            new OutOfMemoryException(),
            new InsufficientExecutionStackException(),
            new StackOverflowException(),
            new AccessViolationException(),
        ];

        foreach (var fatalException in fatalExceptions)
        {
            var calls = 0;
            var shield = Shield.Retry(3, Backoff.None);

            var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
            {
                calls++;
                throw fatalException;
            });

            await Assert.That(ReferenceEquals(outcome.Exception, fatalException)).IsTrue();
            await Assert.That(calls).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Default_Clause_Still_Retries_Programming_Errors()
    {
        var calls = 0;
        var shield = Shield.Retry(2, Backoff.None);

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            calls++;
            throw new ArgumentNullException("value");
        });

        await Assert.That(outcome.Exception).IsTypeOf<ArgumentNullException>();
        await Assert.That(calls).IsEqualTo(3);
    }

    [Test]
    public async Task Explicit_Clause_Can_Retry_Kevlar_Rejections()
    {
        var calls = 0;
        var shield = Shield
            .When<CircuitOpenException>()
            .Retry(2, Backoff.None);

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            calls++;
            throw new CircuitOpenException(null, isIsolated: false, lastException: null);
        });

        await Assert.That(outcome.Exception).IsTypeOf<CircuitOpenException>();
        await Assert.That(calls).IsEqualTo(3);
    }

    [Test]
    public async Task Default_Fallback_Does_Not_Catch_Rejections_But_Explicit_Clause_Does()
    {
        var defaultFallback = Shield.For<int>().FallbackTo(42);
        var explicitFallback = Shield.For<int>()
            .When<KevlarException>()
            .FallbackTo(42);

        var unhandled = await defaultFallback.ExecuteOutcomeAsync(_ =>
            throw new RateLimitExceededException(retryAfter: null));
        var handled = await explicitFallback.ExecuteAsync(_ =>
            throw new RateLimitExceededException(retryAfter: null));

        await Assert.That(unhandled.Exception).IsTypeOf<RateLimitExceededException>();
        await Assert.That(handled).IsEqualTo(42);
    }

    [Test]
    public async Task Sync_Default_Clause_Does_Not_Retry_Rejections()
    {
        var calls = 0;
        var shield = Shield.Retry(3, Backoff.None);

        await Assert.That(() => shield.Execute<int>(_ =>
        {
            calls++;
            throw new ConcurrencyLimitExceededException();
        })).Throws<ConcurrencyLimitExceededException>();

        await Assert.That(calls).IsEqualTo(1);
    }
}
