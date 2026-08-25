namespace Kevlar.Tests;

public class ContextAwarePredicateTests
{
    private static readonly KevlarKey<bool> IsRead = new("is-read");

    [Test]
    public async Task Predicate_Receives_Attempt_Number_In_Retry()
    {
        var shield = Shield
            .When(handling => handling.Exception is TimeoutException && handling.Attempt < 1)
            .Or(handling => handling.Exception is HttpRequestException && handling.Attempt < 3)
            .Retry(5, Backoff.None);

        await AssertAttemptsAsync<TimeoutException>(shield, expected: 2);
        await AssertAttemptsAsync<HttpRequestException>(shield, expected: 4);
    }

    [Test]
    public async Task Predicate_Receives_Context_Properties_And_Strategy_Index()
    {
        HandlingEvent observed = default;
        var shield = Shield.Timeout(TimeSpan.FromMinutes(1))
            .When(handling =>
            {
                observed = handling;
                return handling.Context.Properties.GetOrDefault(IsRead);
            })
            .Retry(1, Backoff.None);
        var attempts = 0;

        await Assert.That(async () => await shield.ExecuteWithContextAsync(
            true,
            static (isRead, properties) => properties.Set(IsRead, isRead),
            (_, _) =>
            {
                attempts++;
                return ValueTask.FromException<int>(new InvalidOperationException());
            }))
            .Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(observed.Attempt).IsEqualTo(0);
        await Assert.That(observed.StrategyIndex).IsEqualTo(1);
    }

    [Test]
    public async Task Predicate_Attempt_Is_Zero_For_Non_Attempting_Strategies()
    {
        var attempts = new List<int>();
        var breaker = Shield
            .When(handling =>
            {
                attempts.Add(handling.Attempt);
                return true;
            })
            .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromMinutes(1));

        await Assert.That(async () => await breaker.ExecuteAsync<int>(
            _ => ValueTask.FromException<int>(new InvalidOperationException())))
            .Throws<InvalidOperationException>();

        var fallback = Shield.For<int>()
            .When(handling =>
            {
                attempts.Add(handling.Attempt);
                return handling.Outcome.Exception is InvalidOperationException;
            })
            .FallbackTo(42);
        var result = await fallback.ExecuteAsync(
            _ => ValueTask.FromException<int>(new InvalidOperationException()));
        await Assert.That(result).IsEqualTo(42);

        await Assert.That(attempts).IsEquivalentTo([0, 0]);
    }

    [Test]
    public async Task Ambient_Context_Aware_Clause_Applies_To_Later_Strategies()
    {
        var attempts = 0;
        var shield = Shield
            .When(handling => handling.Context.Properties.GetOrDefault(IsRead))
            .Retry(1, Backoff.None)
            .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromMinutes(1));

        await Assert.That(async () => await shield.ExecuteWithContextAsync(
            true,
            static (isRead, properties) => properties.Set(IsRead, isRead),
            (_, _) =>
            {
                attempts++;
                return ValueTask.FromException<int>(new InvalidOperationException());
            }))
            .Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Context_Aware_Clause_Is_Sealed_By_Wrap_And_Compose()
    {
        var contextual = Shield
            .When(handling => handling.Exception is TimeoutException)
            .Retry(1, Backoff.None);
        var wrapped = contextual.Wrap(Shield.Empty).Retry(1, Backoff.None);
        var composed = Shield.Compose(contextual, Shield.Empty).Retry(1, Backoff.None);

        await AssertAttemptsAsync<InvalidOperationException>(wrapped, expected: 2);
        await AssertAttemptsAsync<InvalidOperationException>(composed, expected: 2);
    }

    [Test]
    public async Task Typed_Result_Predicate_Receives_Outcome_And_Hedge_Attempt()
    {
        var observedAttempts = new List<int>();
        var executions = 0;
        var shield = Shield.For<int>()
            .WhenResult(handling =>
            {
                observedAttempts.Add(handling.Attempt);
                return handling.Outcome.TryGetResult(out var result) && result < 0;
            })
            .Hedge(maxAttempts: 2, delay: Timeout.InfiniteTimeSpan);

        var result = await shield.ExecuteAsync(_ =>
            new ValueTask<int>(++executions == 1 ? -1 : 42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedAttempts).IsEquivalentTo([0, 1]);
    }

    [Test]
    public async Task HandlesException_Context_Override_Replaces_Ambient_Clause()
    {
        var attempts = 0;
        var shield = Shield.When<InvalidOperationException>().Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.HandlesExceptionWithContext = handling =>
                handling.Exception is ArgumentException && handling.Attempt == 0;
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new ArgumentException();
        })).Throws<ArgumentException>();
        await Assert.That(attempts).IsEqualTo(2);

        attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Predicate_Exception_Is_Treated_As_Not_Handled()
    {
        var predicateFailure = new DivideByZeroException("predicate");
        var shield = Shield.When((HandlingEvent handling) => throw predicateFailure)
            .Retry(1, Backoff.None);
        var attempts = 0;

        var executionFailure = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException("execution");
        })).Throws<InvalidOperationException>();

        await Assert.That(executionFailure).IsNotSameReferenceAs(predicateFailure);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Sync_Predicate_Receives_Attempt_Number()
    {
        var attempts = 0;
        var shield = Shield
            .When(handling => handling.Attempt == 0)
            .Retry(3, Backoff.None);

        await Assert.That(() => shield.Execute<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Describe_Renders_Context_Aware_Clause_As_Custom()
    {
        var shield = Shield
            .When(handling => handling.Context.Properties.GetOrDefault(IsRead))
            .Retry(1, Backoff.None);

        await Assert.That(shield.ToString()).IsEqualTo("[when custom] Retry(1, no delay)");
    }

    private static async Task AssertAttemptsAsync<TException>(Shield shield, int expected)
        where TException : Exception, new()
    {
        var attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new TException();
        })).Throws<TException>();
        await Assert.That(attempts).IsEqualTo(expected);
    }
}
