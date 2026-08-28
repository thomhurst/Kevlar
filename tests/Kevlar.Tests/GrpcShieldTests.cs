using Grpc.Core;
using Kevlar.Extensions.Grpc;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class GrpcShieldTests
{
    [Test]
    public async Task RetryAfter_Uses_Server_Pushback_Delay()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var attemptsStarted = new AsyncCounter("gRPC pushback attempts");
        var shield = GrpcShield.WhenTransient()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = GrpcShield.RetryAfter;
            })
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(_ =>
        {
            attemptsStarted.Signal();
            return ++attempts == 1
                ? ValueTask.FromException<int>(CreateException("2500"))
                : new ValueTask<int>(42);
        }).AsTask();

        await attemptsStarted.WaitForAsync(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(2499));
        await Assert.That(attempts).IsEqualTo(1);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await attemptsStarted.WaitForAsync(2);

        await Assert.That(await execution).IsEqualTo(42);
    }

    [Test]
    [Arguments("-1")]
    [Arguments("invalid")]
    [Arguments("01")]
    public async Task WhenTransient_Suppresses_Invalid_Or_Negative_Pushback(string value)
    {
        var attempts = 0;
        var shield = GrpcShield.WhenTransient().Retry(3, Backoff.None);

        var exception = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw CreateException(value);
        })).Throws<RpcException>();

        await Assert.That(exception!.Trailers.GetValue("grpc-retry-pushback-ms")).IsEqualTo(value);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task IsTransient_Accepts_Missing_And_NonNegative_Pushback()
    {
        await Assert.That(GrpcShield.IsTransient(CreateException(pushback: null))).IsTrue();
        await Assert.That(GrpcShield.IsTransient(CreateException("0"))).IsTrue();
        await Assert.That(GrpcShield.IsTransient(CreateException("2147483647"))).IsTrue();
        await Assert.That(GrpcShield.IsTransient(
            new RpcException(new Status(StatusCode.InvalidArgument, "not transient")))).IsFalse();
        await Assert.That(GrpcShield.IsTransient((RpcException?)null)).IsFalse();
    }

    private static RpcException CreateException(string? pushback)
    {
        var trailers = new Metadata();
        if (pushback is not null)
        {
            trailers.Add("grpc-retry-pushback-ms", pushback);
        }

        return new RpcException(new Status(StatusCode.Unavailable, "transient"), trailers);
    }
}
