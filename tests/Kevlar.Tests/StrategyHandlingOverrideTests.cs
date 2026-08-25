namespace Kevlar.Tests;

public class StrategyHandlingOverrideTests
{
    [Test]
    public async Task Retry_Exception_Override_Replaces_Ambient_Handling()
    {
        var attempts = 0;
        var shield = Shield.When<InvalidOperationException>().Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.HandlesException = exception => exception is ArgumentException;
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
    public async Task Result_Only_Retry_Override_Does_Not_Handle_Exceptions()
    {
        var attempts = 0;
        var shield = Shield.For<int>().When<InvalidOperationException>().Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.HandlesResult = result => result < 0;
        });

        var result = await shield.ExecuteAsync(_ =>
            new ValueTask<int>(++attempts == 1 ? -1 : 7));
        await Assert.That(result).IsEqualTo(7);
        await Assert.That(attempts).IsEqualTo(2);

        attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Typed_Retry_Exception_Override_Uses_Typed_Options()
    {
        var attempts = 0;
        var shield = Shield.For<int>().WhenResult(-1).Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.HandlesException = exception => exception is ArgumentException;
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new ArgumentException();
        })).Throws<ArgumentException>();
        await Assert.That(attempts).IsEqualTo(2);

        attempts = 0;
        await Assert.That(await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return new ValueTask<int>(-1);
        })).IsEqualTo(-1);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Typed_Circuit_Breaker_Override_Handles_Results_Only()
    {
        var shield = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.HandlesResult = result => result < 0;
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(1))).IsEqualTo(1);
        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(-1))).IsEqualTo(-1);
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Typed_Hedging_Override_Handles_Results_Only()
    {
        var attempts = 0;
        var shield = Shield.For<int>().When<InvalidOperationException>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
            options.HandlesResult = result => result < 0;
        });

        var result = await shield.ExecuteAsync(_ =>
            new ValueTask<int>(Interlocked.Increment(ref attempts) == 1 ? -1 : 7));
        await Assert.That(result).IsEqualTo(7);
        await Assert.That(attempts).IsEqualTo(2);

        attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Retry_Override_Leaves_Ambient_Fallback_For_Neighbor()
    {
        var attempts = 0;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .FallbackTo(42)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.HandlesException = exception => exception is ArgumentException;
            });

        var recovered = await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });
        await Assert.That(recovered).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(1);

        attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new ArgumentException();
        })).Throws<ArgumentException>();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_Fallback_Result_Override_Replaces_Ambient_Handling()
    {
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .FallbackTo(0, options =>
            {
                options.HandlesResult = result => result < 0;
            });

        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(-1))).IsEqualTo(0);
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Fallback_Validation_Distinguishes_Local_Overrides()
    {
        var shield = Shield.For<int>()
            .Retry(options => options.HandlesException = exception => exception is InvalidOperationException)
            .FallbackTo(0, options =>
            {
                options.HandlesException = exception => exception is InvalidOperationException;
            });

        await Assert.That(shield).IsNotNull();
        await Assert.That(() => Shield.For<int>()
            .Retry(1)
            .FallbackTo(0))
            .Throws<InvalidOperationException>();
    }
}
