using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Kevlar.Tests;

/// <summary>
/// Guards the built-in metrics: every shield publishes counters through the "Kevlar" meter with
/// zero configuration. Assertions filter on a per-test <c>shield.name</c> tag so parallel tests
/// cannot cross-contaminate counts.
/// </summary>
public class MetricsTests
{
    private sealed class KevlarMeterListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentBag<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = [];

        public KevlarMeterListener()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var captured = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    captured[tag.Key] = tag.Value;
                }

                _measurements.Add((instrument.Name, value, captured));
            });
            _listener.Start();
        }

        public long Total(string instrument, string? shieldName = null, params (string Key, string Value)[] tags) =>
            _measurements
                .Where(m => m.Instrument == instrument)
                .Where(m => shieldName is null || (m.Tags.TryGetValue("shield.name", out var name) && Equals(name, shieldName)))
                .Where(m => tags.All(tag => m.Tags.TryGetValue(tag.Key, out var value) && Equals(value, tag.Value)))
                .Sum(m => m.Value);

        public void Dispose() => _listener.Dispose();
    }

    [Test]
    public async Task Executions_Are_Counted_With_Their_Outcome()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Retry(0, Backoff.None).WithName("metrics-executions");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(listener.Total("kevlar.executions", "metrics-executions", ("outcome", "success"))).IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.executions", "metrics-executions", ("outcome", "failure"))).IsEqualTo(1);
    }

    [Test]
    public async Task Retries_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Retry(2, Backoff.None).WithName("metrics-retries");

        var attempts = 0;
        await shield.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException();
            }

            return new ValueTask<int>(1);
        });

        await Assert.That(listener.Total("kevlar.retries", "metrics-retries")).IsEqualTo(2);
    }

    [Test]
    public async Task Timeouts_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Timeout(TimeSpan.FromMilliseconds(50)).WithName("metrics-timeouts");

        await Assert.That(async () => await shield.ExecuteAsync(async ct => await Task.Delay(Timeout.InfiniteTimeSpan, ct)))
            .Throws<TimeoutExceededException>();

        await Assert.That(listener.Total("kevlar.timeouts", "metrics-timeouts")).IsEqualTo(1);
    }

    [Test]
    public async Task Rate_Limit_Rejections_Are_Counted_With_Their_Kind()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.RateLimit(1, TimeSpan.FromHours(1)).WithName("metrics-rate");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();

        await Assert.That(listener.Total("kevlar.rejections", "metrics-rate", ("kind", "rate_limit"))).IsEqualTo(1);
    }

    [Test]
    public async Task Fallbacks_And_Circuit_Transitions_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Fallback(-1)
            .CircuitBreaker(1, TimeSpan.FromMinutes(1))
            .WithName("metrics-fallback");

        var recovered = await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(recovered).IsEqualTo(-1);
        await Assert.That(listener.Total("kevlar.fallbacks", "metrics-fallback")).IsEqualTo(1);

        // The breaker tripped Closed -> Open; transitions carry no shield name, and other tests
        // may trip breakers concurrently, so assert at least ours was recorded.
        var transitions = listener.Total("kevlar.circuit_breaker.transitions", null, ("from", "Closed"), ("to", "Open"));
        await Assert.That(transitions >= 1).IsTrue();
    }

    [Test]
    public async Task Hedged_Attempts_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Hedge(2, TimeSpan.Zero).WithName("metrics-hedge");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(listener.Total("kevlar.hedges", "metrics-hedge")).IsEqualTo(1);
    }

    [Test]
    public async Task Suppressed_Hedged_Attempts_Are_Not_Counted()
    {
        using var listener = new KevlarMeterListener();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ => cancellation.Cancel();
        }).WithName("metrics-suppressed-hedge");

        await shield.ExecuteOutcomeAsync<int>(async token =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token);

        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.hedges", "metrics-suppressed-hedge")).IsEqualTo(0);
    }
}
