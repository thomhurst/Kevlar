namespace Kevlar.Tests;

public class OutcomeAndContextTests
{
    [Test]
    public async Task FromResult_Is_Success()
    {
        var outcome = Outcome<int>.FromResult(42);

        await Assert.That(outcome.IsSuccess).IsTrue();
        await Assert.That(outcome.Result).IsEqualTo(42);
        await Assert.That(outcome.Exception).IsNull();
        await Assert.That(outcome.ToString()).IsEqualTo("42");
    }

    [Test]
    public async Task FromException_Is_Failure()
    {
        var exception = new InvalidOperationException("boom");
        var outcome = Outcome<int>.FromException(exception);

        await Assert.That(outcome.IsSuccess).IsFalse();
        await Assert.That(outcome.Exception).IsEqualTo(exception);
    }

    [Test]
    public async Task FromException_Rejects_Null()
    {
        await Assert.That(() => Outcome<int>.FromException(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task GetResultOrRethrow_Throws_The_Original_Exception_Instance()
    {
        var original = new InvalidOperationException("boom");
        var outcome = Outcome<int>.FromException(original);

        try
        {
            outcome.GetResultOrRethrow();
            throw new Exception("should not be reached");
        }
        catch (InvalidOperationException caught)
        {
            await Assert.That(ReferenceEquals(caught, original)).IsTrue();
        }
    }

    [Test]
    public async Task Default_Outcome_Is_A_Success_With_The_Default_Value()
    {
        var outcome = default(Outcome<string>);

        await Assert.That(outcome.IsSuccess).IsTrue();
        await Assert.That(outcome.Result).IsNull();
        await Assert.That(outcome.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task KevlarKey_Exposes_Its_Name()
    {
        var key = new KevlarKey<int>("attempt-count");

        await Assert.That(key.Name).IsEqualTo("attempt-count");
        await Assert.That(key.ToString()).IsEqualTo("attempt-count");
    }

    [Test]
    public async Task KevlarKey_Rejects_A_Null_Name()
    {
        await Assert.That(() => new KevlarKey<int>(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Properties_Set_And_TryGet_Round_Trip()
    {
        var properties = CapturedProperties();
        var key = new KevlarKey<string>("k");

        properties.Set(key, "value");

        await Assert.That(properties.TryGet(key, out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("value");
    }

    [Test]
    public async Task Properties_Set_Overwrites_An_Existing_Value()
    {
        var properties = CapturedProperties();
        var key = new KevlarKey<int>("k");

        properties.Set(key, 1);
        properties.Set(key, 2);

        await Assert.That(properties.GetOrDefault(key)).IsEqualTo(2);
    }

    [Test]
    public async Task Properties_TryGet_Is_False_For_A_Missing_Key()
    {
        var properties = CapturedProperties();

        await Assert.That(properties.TryGet(new KevlarKey<int>("missing"), out _)).IsFalse();
        await Assert.That(properties.GetOrDefault(new KevlarKey<int>("missing"), 9)).IsEqualTo(9);
    }

    [Test]
    public async Task Properties_TryGet_Is_False_When_The_Stored_Type_Differs()
    {
        var properties = CapturedProperties();
        properties.Set(new KevlarKey<string>("k"), "text");

        await Assert.That(properties.TryGet(new KevlarKey<int>("k"), out _)).IsFalse();
    }

    [Test]
    public async Task Properties_Are_Cleared_Between_Executions()
    {
        // The context is pooled; whatever instance an execution rents must start with
        // empty properties even if a previous execution stored values in it.
        var key = new KevlarKey<string>("leak-check");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry => retry.Context.Properties.Set(key, "left-behind");
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        var leaked = false;
        var probePolicy = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry => leaked = retry.Context.Properties.TryGet(key, out _);
        });

        await Assert.That(async () => await probePolicy.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(leaked).IsFalse();
    }

    [Test]
    public async Task Context_Reports_Synchronous_And_Asynchronous_Executions()
    {
        bool? sawSynchronous = null;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry => sawSynchronous = retry.Context.IsSynchronous;
        });

        await Assert.That(() => shield.Execute<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(sawSynchronous!.Value).IsTrue();

        sawSynchronous = null;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(sawSynchronous!.Value).IsFalse();
    }

    // The internal constructor is reachable via InternalsVisibleTo.
    private static KevlarProperties CapturedProperties() => new();
}
