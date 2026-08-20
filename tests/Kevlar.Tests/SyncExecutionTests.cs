using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class SyncExecutionTests
{
    [Test]
    public async Task Sync_Exhausted_Retries_Rethrow_The_Last_Exception()
    {
        var attempts = 0;
        var policy = Policy.Retry(2, Backoff.None);

        await Assert.That(() => policy.Execute<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException($"attempt {attempts}");
        })).Throws<InvalidOperationException>().WithMessage("attempt 3");
    }

    [Test]
    public async Task Sync_Retry_Delays_Block_And_Then_Complete()
    {
        var attempts = 0;
        var policy = Policy.Retry(2, Backoff.Constant(TimeSpan.FromMilliseconds(10)));

        var result = policy.Execute(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException();
            }

            return attempts;
        });

        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Sync_CircuitBreaker_Trips_And_Rejects()
    {
        var policy = Policy.CircuitBreaker(2, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 2; i++)
        {
            await Assert.That(() => policy.Execute<int>(_ => throw new InvalidOperationException()))
                .Throws<InvalidOperationException>();
        }

        await Assert.That(() => policy.Execute(_ => 1)).Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Sync_RateLimit_Rejects_When_Drained()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = Policy.RateLimit(2, TimeSpan.FromSeconds(10)).WithTimeProvider(fakeTime);

        await Assert.That(policy.Execute(_ => 1)).IsEqualTo(1);
        await Assert.That(policy.Execute(_ => 2)).IsEqualTo(2);

        await Assert.That(() => policy.Execute(_ => 3)).Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Sync_Bulkhead_Rejects_When_Full()
    {
        var policy = Policy.Bulkhead(maxConcurrency: 1);
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var occupier = policy.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        await Assert.That(() => policy.Execute(_ => 2)).Throws<BulkheadRejectedException>();

        gate.SetResult();
        await Assert.That(await occupier).IsEqualTo(1);
    }

    [Test]
    public async Task Sync_HandleResult_Retries()
    {
        var attempts = 0;
        var policy = Policy.For<int>().HandleResult(value => value < 0).Retry(3, Backoff.None);

        var result = policy.Execute(_ =>
        {
            attempts++;
            return attempts < 3 ? -1 : attempts;
        });

        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Sync_State_Passing_Overloads_Thread_State()
    {
        var policy = Policy.Retry(1, Backoff.None);

        var result = policy.Execute(21, static (state, _) => state * 2);
        await Assert.That(result).IsEqualTo(42);

        var sideEffect = 0;
        policy.Execute(7, (state, _) => sideEffect = state);
        await Assert.That(sideEffect).IsEqualTo(7);
    }

    [Test]
    public async Task Sync_Empty_Policy_Passes_Through()
    {
        var result = Policy.Empty.Execute(_ => 99);
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Sync_Composed_Pipeline_Runs_End_To_End()
    {
        var attempts = 0;
        // Fallback first (outermost), retry inside it: the retries exhaust before the
        // fallback replaces the final failure.
        var policy = Policy.For<string>()
            .Handle<InvalidOperationException>()
            .Fallback("fallback")
            .Retry(2, Backoff.None);

        var result = policy.Execute(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        // Retries exhaust (3 attempts), then the fallback replaces the failure.
        await Assert.That(result).IsEqualTo("fallback");
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Sync_Exceptions_Keep_Their_Original_Stack_Trace()
    {
        var policy = Policy.Retry(0, Backoff.None);

        try
        {
            policy.Execute<int>(_ => ThrowDeep());
            throw new Exception("should not be reached");
        }
        catch (InvalidOperationException exception)
        {
            await Assert.That(exception.StackTrace!.Contains(nameof(ThrowDeep))).IsTrue();
        }
    }

    private static int ThrowDeep() => throw new InvalidOperationException("deep");
}
