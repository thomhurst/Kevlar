using System.Collections.Concurrent;
using System.Diagnostics;

namespace Kevlar.OutageTests;

internal static class OutageRunner
{
    internal static readonly string[] Scenarios = ["retry", "retry-breaker", "limit-retry-breaker", "limit-hedge-breaker"];

    internal static Shield CreateShield(string scenario)
    {
        var shield = scenario.StartsWith("limit-", StringComparison.Ordinal)
            ? Shield.ConcurrencyLimit(OutageOptions.Concurrency, queueLimit: OutageOptions.QueueLimit)
            : Shield.Empty;
        shield = scenario.Contains("hedge", StringComparison.Ordinal)
            ? shield.When<IOException>().Hedge(1, delay: TimeSpan.FromMilliseconds(OutageOptions.HedgeMilliseconds))
            : shield.When<IOException>().Retry(2, backoff: Backoff.None);
        if (scenario.EndsWith("breaker", StringComparison.Ordinal))
        {
            shield = shield.CircuitBreaker(consecutiveFailures: 5,
                breakDuration: TimeSpan.FromMilliseconds(OutageOptions.BreakMilliseconds));
        }
        return shield;
    }

    internal static async Task<ScenarioResult> RunAsync(string scenario, OutageOptions options)
    {
        // Warm code separately; measured shields and dependency state always start fresh.
        await CreateShield(scenario).ExecuteAsync(static _ => new ValueTask<int>(1));
        var shield = CreateShield(scenario);
        var requests = new List<RequestMeasurement>(options.TotalArrivals);
        var attempts = new ConcurrentBag<AttemptMeasurement>();
        var completions = new List<Task>(options.TotalArrivals);
        var clock = Stopwatch.StartNew();
        var logicalActive = 0;
        var downstreamActive = 0;
        var peakLogical = 0;
        var peakDownstream = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(options.PhaseSeconds * 4 + 30));

        for (var id = 0; id < options.TotalArrivals; id++)
        {
            var scheduled = (double)id / options.RequestsPerSecond;
            var remaining = scheduled - clock.Elapsed.TotalSeconds;
            while (remaining > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, remaining * 1000)), deadline.Token);
                remaining = scheduled - clock.Elapsed.TotalSeconds;
            }
            deadline.Token.ThrowIfCancellationRequested();
            var request = new RequestMeasurement(id, scheduled) { StartedSeconds = clock.Elapsed.TotalSeconds };
            requests.Add(request);
            if (Volatile.Read(ref logicalActive) >= options.MaximumInFlight)
            {
                request.Outcome = "harness_rejected";
                request.CompletedSeconds = clock.Elapsed.TotalSeconds;
                continue;
            }
            Measurements.UpdatePeak(ref peakLogical, Interlocked.Increment(ref logicalActive));
            // Do not await logical completion: the next arrival keeps its original scheduled time.
            completions.Add(ExecuteAsync(request));
        }

        await Task.WhenAll(completions).WaitAsync(deadline.Token);
        // Hedging may return before losing operations finish their cancellation cleanup.
        // Observe every attempt completion separately before publishing or starting another scenario.
        do
        {
            await Task.WhenAll(attempts.Select(attempt => attempt.Completion.Task)).WaitAsync(deadline.Token);
        } while (Volatile.Read(ref downstreamActive) != 0);
        clock.Stop();
        if (requests.Any(request => request.Outcome == "pending")) { throw new InvalidOperationException("Incomplete request accounting."); }
        return Summarize(scenario, shield.ToString(), options, requests, attempts.ToArray(),
            clock.Elapsed.TotalSeconds, peakLogical, peakDownstream, downstreamActive);

        async Task ExecuteAsync(RequestMeasurement request)
        {
            try
            {
                _ = await shield.ExecuteAsync(token => DependencyAsync(request, token), deadline.Token);
                request.Outcome = "success";
            }
            catch (IOException) { request.Outcome = "dependency_failed"; }
            catch (ConcurrencyLimitExceededException) { request.Outcome = "concurrency_rejected"; }
            catch (CircuitOpenException) { request.Outcome = "circuit_rejected"; }
            finally
            {
                request.CompletedSeconds = clock.Elapsed.TotalSeconds;
                Interlocked.Decrement(ref logicalActive);
            }
        }

        async ValueTask<int> DependencyAsync(RequestMeasurement request, CancellationToken token)
        {
            var number = Interlocked.Increment(ref request.Attempts);
            var attempt = new AttemptMeasurement(request, clock.Elapsed.TotalSeconds);
            attempts.Add(attempt);
            Measurements.UpdatePeak(ref peakDownstream, Interlocked.Increment(ref downstreamActive));
            try
            {
                var phase = Measurements.Phase(attempt.StartedSeconds, options.PhaseSeconds);
                var milliseconds = phase switch
                {
                    1 => number == 1 ? OutageOptions.SlowMilliseconds : OutageOptions.AlternateMilliseconds,
                    2 => OutageOptions.FailureMilliseconds,
                    _ => OutageOptions.HealthyMilliseconds
                };
                await Task.Delay(milliseconds, token);
                if (phase == 2) { throw new IOException("Controlled dependency outage"); }
                return 1;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                attempt.Cancelled = true;
                // Model asynchronous resource cleanup that continues after loser cancellation.
                await Task.Delay(OutageOptions.CleanupMilliseconds, CancellationToken.None);
                throw;
            }
            finally
            {
                attempt.CompletedSeconds = clock.Elapsed.TotalSeconds;
                Interlocked.Decrement(ref downstreamActive);
                attempt.Completion.TrySetResult();
            }
        }
    }

    internal static ScenarioResult Summarize(string scenario, string pipeline, OutageOptions options,
        IReadOnlyList<RequestMeasurement> requests, IReadOnlyList<AttemptMeasurement> attempts,
        double wallSeconds, int peakLogical, int peakDownstream, int activeAfterDrain)
    {
        var phases = Enumerable.Range(0, 4).Select(phase =>
        {
            var group = requests.Where(request => Measurements.Phase(request.ScheduledSeconds, options.PhaseSeconds) == phase).ToArray();
            var submitted = group.Where(request => request.Outcome != "harness_rejected").ToArray();
            var admitted = group.Count(request => request.Attempts > 0);
            var downstream = group.Sum(request => request.Attempts);
            return new PhaseResult(Measurements.PhaseNames[phase], phase * options.PhaseSeconds, (phase + 1) * options.PhaseSeconds,
                group.Length, submitted.Length, admitted, group.Count(request => request.Outcome == "success"),
                group.Count(request => request.Outcome != "success"), group.Length - submitted.Length,
                group.Count(request => request.Outcome == "concurrency_rejected"), group.Count(request => request.Outcome == "circuit_rejected"),
                downstream, group.Length == 0 ? 0 : (double)downstream / group.Length,
                admitted == 0 ? 0 : (double)downstream / admitted,
                Measurements.Percentile(submitted.Select(Latency), .95), Measurements.Percentile(submitted.Select(Latency), .99),
                Measurements.Percentile(submitted.Where(request => request.Outcome == "success").Select(Latency), .99),
                Measurements.Percentile(group.Select(request => Math.Max(0, request.StartedSeconds - request.ScheduledSeconds) * 1000), .99),
                attempts.Count(attempt => Measurements.Phase(attempt.StartedSeconds, options.PhaseSeconds) == phase));
        }).ToArray();
        var cleanup = attempts.Where(attempt => attempt.Cancelled)
            .Select(attempt => Math.Max(0, attempt.CompletedSeconds - attempt.Request.CompletedSeconds) * 1000).ToArray();
        const double recoveryWindow = .25;
        var requiredSuccesses = Math.Max(1, (int)Math.Floor(options.RequestsPerSecond * recoveryWindow));
        var recovery = Measurements.Recovery(requests, options.PhaseSeconds * 3, options.PhaseSeconds * 4, recoveryWindow, requiredSuccesses);
        return new ScenarioResult(scenario, pipeline, wallSeconds, recovery * 1000, recoveryWindow * 1000, requiredSuccesses,
            peakLogical, peakDownstream, activeAfterDrain, cleanup.Length, cleanup.Count(value => value > 0),
            Measurements.Percentile(cleanup, .99) ?? 0, cleanup.DefaultIfEmpty().Max(), phases);
    }

    private static double Latency(RequestMeasurement request) => Math.Max(0, request.CompletedSeconds - request.ScheduledSeconds) * 1000;
}
