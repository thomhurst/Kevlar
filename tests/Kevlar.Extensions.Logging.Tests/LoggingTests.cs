using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Extensions.Logging.Tests;

public class LoggingTests
{
    [Test]
    [NotInParallel]
    public async Task Retry_Logs_Attempt_Delay_And_Exception_Type()
    {
        var logger = new FakeLogger();
        var failure = new TestException("transient");
        var shield = Shield.Retry(1, Backoff.None)
            .WithName("checkout")
            .WithLogging(logger);

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
            new ValueTask<int>(Task.FromException<int>(failure)));

        var record = logger.Collector.GetSnapshot().Single();
        await Assert.That(ReferenceEquals(outcome.Exception, failure)).IsTrue();
        await Assert.That(record.Id).IsEqualTo(new EventId(1001, "Retry"));
        await Assert.That(record.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(record.GetStructuredStateValue("ShieldName")).IsEqualTo("checkout");
        await Assert.That(record.GetStructuredStateValue("StrategyIndex")).IsEqualTo("0");
        await Assert.That(record.GetStructuredStateValue("Attempt")).IsEqualTo("1");
        await Assert.That(record.GetStructuredStateValue("Delay")).IsEqualTo(TimeSpan.Zero.ToString());
        await Assert.That(record.GetStructuredStateValue("Outcome")).IsEqualTo(typeof(TestException).FullName);
        await Assert.That(ReferenceEquals(record.Exception, failure)).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task WithLogging_On_Typed_And_Untyped_Shields()
    {
        var logger = new FakeLogger();
        var typed = Shield.For<int>().WhenResult(-1).Retry(1, Backoff.None).WithLogging(logger);
        var untyped = Shield.Retry(1, Backoff.None).WithLogging(logger);

        _ = await typed.ExecuteAsync(static _ => new ValueTask<int>(-1));
        _ = await untyped.ExecuteOutcomeAsync<int>(static _ =>
            new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));

        await Assert.That(logger.Collector.Count).IsEqualTo(2);
    }

    [Test]
    [NotInParallel]
    public async Task Retry_With_Result_Logs_Formatted_Result()
    {
        var logger = new FakeLogger();
        object? formatted = null;
        var shield = Shield.For<int>()
            .WhenResult(-1)
            .Retry(1, Backoff.None)
            .WithLogging(logger, options => options.ResultFormatter = result =>
            {
                formatted = result;
                return $"result:{result}";
            });

        _ = await shield.ExecuteAsync(static _ => new ValueTask<int>(-1));

        await Assert.That(formatted).IsEqualTo(-1);
        await Assert.That(logger.LatestRecord.GetStructuredStateValue("Outcome"))
            .IsEqualTo("result:-1");
    }

    [Test]
    [NotInParallel]
    public async Task Timeout_Logs_Duration()
    {
        var logger = new FakeLogger();
        var timeout = TimeSpan.FromMilliseconds(20);
        var shield = Shield.Timeout(timeout).WithLogging(logger);

        var outcome = await shield.ExecuteOutcomeAsync(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 42;
        });

        var record = logger.Collector.GetSnapshot().Single();
        await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(record.Id).IsEqualTo(new EventId(1002, "Timeout"));
        await Assert.That(record.GetStructuredStateValue("Duration")).IsEqualTo(timeout.ToString());
    }

    [Test]
    [NotInParallel]
    public async Task Breaker_Opened_Logs_Error_With_State_And_Break_Duration()
    {
        var logger = new FakeLogger();
        var failure = new TestException("failure");
        var breakDuration = TimeSpan.FromSeconds(5);
        var shield = Shield.CircuitBreaker(1, breakDuration).WithLogging(logger);

        _ = await shield.ExecuteOutcomeAsync<int>(_ =>
            new ValueTask<int>(Task.FromException<int>(failure)));

        var record = logger.Collector.GetSnapshot().Single();
        await Assert.That(record.Id).IsEqualTo(new EventId(1003, "CircuitState"));
        await Assert.That(record.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(record.GetStructuredStateValue("FromState")).IsEqualTo("Closed");
        await Assert.That(record.GetStructuredStateValue("ToState")).IsEqualTo("Open");
        await Assert.That(record.GetStructuredStateValue("BreakDuration"))
            .IsEqualTo(breakDuration.ToString());
    }

    [Test]
    [NotInParallel]
    public async Task Breaker_Opened_Logs_Generated_Break_Duration()
    {
        var logger = new FakeLogger();
        var generatedDuration = TimeSpan.FromSeconds(17);
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.BreakDurationGenerator = _ => new ValueTask<TimeSpan>(generatedDuration);
        }).WithLogging(logger);

        _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
            new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));

        var record = logger.Collector.GetSnapshot().Single();
        await Assert.That(record.GetStructuredStateValue("BreakDuration"))
            .IsEqualTo(generatedDuration.ToString());
    }

    [Test]
    [NotInParallel]
    public async Task Open_Circuit_Rejection_Uses_A_Rejection_Event()
    {
        var logger = new FakeLogger();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.Name = "checkout-breaker";
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
        }).WithLogging(logger);

        _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
            new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => new ValueTask<int>(42));

        var rejection = logger.Collector.GetSnapshot()
            .Single(record => record.Id.Name == "CircuitRejected");
        await Assert.That(rejection.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(rejection.GetStructuredStateValue("Attempt")).IsEqualTo("0");
        await Assert.That(rejection.GetStructuredStateValue("CircuitState")).IsEqualTo("Open");
        await Assert.That(rejection.GetStructuredStateValue("RetryAfter")).IsNotNull();
        await Assert.That(rejection.Message).Contains("circuit is Open");
        await Assert.That(rejection.StructuredState?.Any(pair => pair.Key == "FromState") ?? false)
            .IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task Hedge_And_Fallback_Log_Their_Stable_Event_Ids()
    {
        var logger = new FakeLogger();
        var attempts = 0;
        var hedge = Shield.Hedge(1, TimeSpan.Zero).WithLogging(logger);
        var fallback = Shield.For<int>()
            .WhenResult(-1)
            .FallbackTo(0)
            .WithLogging(logger, options => options.ResultFormatter = result => $"result:{result}");

        _ = await hedge.ExecuteOutcomeAsync<int>(_ => Interlocked.Increment(ref attempts) == 1
            ? new ValueTask<int>(Task.FromException<int>(new TestException("primary")))
            : new ValueTask<int>(42));
        _ = await fallback.ExecuteAsync(static _ => new ValueTask<int>(-1));

        var records = logger.Collector.GetSnapshot();
        await Assert.That(records.Any(record =>
            record.Id == new EventId(1004, "Hedge")
            && record.Level == LogLevel.Information
            && record.GetStructuredStateValue("Attempt") == "1")).IsTrue();
        await Assert.That(records.Any(record =>
            record.Id == new EventId(1005, "Fallback")
            && record.Level == LogLevel.Warning
            && record.GetStructuredStateValue("Attempt") == "0"
            && record.GetStructuredStateValue("Outcome") == "result:-1")).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task RateLimit_And_Concurrency_Rejections_Log()
    {
        var logger = new FakeLogger();
        var rateLimit = Shield.RateLimit(options =>
        {
            options.Name = "tenant-budget";
            options.Permits = 1;
            options.Window = TimeSpan.FromMinutes(1);
        }).WithLogging(logger);
        await rateLimit.ExecuteAsync(static _ => ValueTask.CompletedTask);
        _ = await rateLimit.ExecuteOutcomeAsync(static _ => ValueTask.CompletedTask);

        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrency = Shield.ConcurrencyLimit(options =>
        {
            options.Name = "database-pool";
            options.MaxConcurrency = 1;
        }).WithLogging(logger);
        var first = concurrency.ExecuteAsync(async _ =>
        {
            entered.TrySetResult(true);
            await release.Task;
        }).AsTask();
        await entered.Task;
        _ = await concurrency.ExecuteOutcomeAsync(static _ => ValueTask.CompletedTask);
        release.TrySetResult(true);
        await first;

        var records = logger.Collector.GetSnapshot();
        await Assert.That(records.Any(record =>
            record.Id == new EventId(1006, "RateLimitRejected")
            && record.Level == LogLevel.Warning
            && record.GetStructuredStateValue("RetryAfter") is not null)).IsTrue();
        await Assert.That(records.Any(record =>
            record.Id == new EventId(1007, "ConcurrencyLimitRejected")
            && record.Level == LogLevel.Warning
            && record.GetStructuredStateValue("Attempt") == "0")).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task Callback_Error_Logs_Error_Without_Replacing_User_Callback()
    {
        var logger = new FakeLogger();
        var callbackCalls = 0;
        var order = new List<string>();
        var callbackFailure = new TestException("callback");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ =>
            {
                order.Add("callback");
                callbackCalls++;
                throw callbackFailure;
            };
        }).WithLogging(logger, options => options.SeverityProvider = logEvent =>
        {
            if (logEvent.Kind == KevlarLogEventKind.Retry)
            {
                order.Add("log");
            }

            return logEvent.Kind == KevlarLogEventKind.CallbackError
                ? LogLevel.Error
                : LogLevel.Warning;
        });

        _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
            new ValueTask<int>(Task.FromException<int>(new TestException("execution"))));

        var callbackRecord = logger.Collector.GetSnapshot()
            .Single(record => record.Id == new EventId(1008, "CallbackError"));
        await Assert.That(callbackCalls).IsEqualTo(1);
        await Assert.That(order.SequenceEqual(["log", "callback"])).IsTrue();
        await Assert.That(callbackRecord.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(callbackRecord.GetStructuredStateValue("CallbackKind")).IsEqualTo("Retry");
        await Assert.That(ReferenceEquals(callbackRecord.Exception, callbackFailure)).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task SeverityProvider_Overrides_Level_And_None_Skips_Formatting()
    {
        var logger = new FakeLogger();
        var formatterCalls = 0;
        var information = Shield.For<int>()
            .WhenResult(-1)
            .Retry(1, Backoff.None)
            .WithLogging(logger, options =>
            {
                options.SeverityProvider = _ => LogLevel.Information;
                options.ResultFormatter = result => $"result:{result}";
            });
        var disabled = Shield.For<int>()
            .WhenResult(-1)
            .Retry(1, Backoff.None)
            .WithLogging(logger, options =>
            {
                options.SeverityProvider = _ => LogLevel.None;
                options.ResultFormatter = _ =>
                {
                    formatterCalls++;
                    return "unexpected";
                };
            });

        _ = await information.ExecuteAsync(static _ => new ValueTask<int>(-1));
        var count = logger.Collector.Count;
        _ = await disabled.ExecuteAsync(static _ => new ValueTask<int>(-1));

        await Assert.That(logger.LatestRecord.Level).IsEqualTo(LogLevel.Information);
        await Assert.That(logger.LatestRecord.GetStructuredStateValue("Attempt")).IsEqualTo("1");
        await Assert.That(logger.LatestRecord.GetStructuredStateValue("Delay"))
            .IsEqualTo(TimeSpan.Zero.ToString());
        await Assert.That(logger.LatestRecord.GetStructuredStateValue("Outcome"))
            .IsEqualTo("result:-1");
        await Assert.That(logger.Collector.Count).IsEqualTo(count);
        await Assert.That(formatterCalls).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task Logging_Chains_When_Called_Twice()
    {
        var first = new FakeLogger();
        var second = new FakeLogger();
        var shield = Shield.Retry(1, Backoff.None)
            .WithLogging(first)
            .WithLogging(second);

        _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
            new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));

        await Assert.That(first.Collector.Count).IsEqualTo(1);
        await Assert.That(second.Collector.Count).IsEqualTo(1);
        await Assert.That(shield.ToString()).IsEqualTo(Shield.Retry(1, Backoff.None).ToString());
    }

    [Test]
    [NotInParallel]
    public async Task Scopes_Include_ShieldName_When_Enabled()
    {
        var logger = new FakeLogger();
        var shield = Shield.Retry(1, Backoff.None)
            .WithName("checkout")
            .WithLogging(logger, options => options.IncludeScopes = true);

        _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
            new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));

        await Assert.That(logger.LatestRecord.Scopes.Any(scope =>
            scope?.ToString() == "Kevlar shield checkout")).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task Scope_Disposal_Continues_After_One_Scope_Fails()
    {
        var failure = new TestException("scope");
        var failingScope = new TrackingScope(failure);
        var survivingScope = new TrackingScope();
        CallbackErrorEvent? reported = null;
        Action<CallbackErrorEvent> handler = callback => reported = callback;
        KevlarDiagnostics.OnCallbackError += handler;
        try
        {
            var shield = Shield.Retry(1, Backoff.None)
                .WithLogging(new ScopeLogger(failingScope), options => options.IncludeScopes = true)
                .WithLogging(new ScopeLogger(survivingScope), options => options.IncludeScopes = true);

            _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
                new ValueTask<int>(Task.FromException<int>(new TestException("execution"))));

            await Assert.That(failingScope.IsDisposed).IsTrue();
            await Assert.That(survivingScope.IsDisposed).IsTrue();
            await Assert.That(reported?.Kind).IsEqualTo(CallbackErrorKind.Logging);
            await Assert.That(ReferenceEquals(reported?.Exception, failure)).IsTrue();
        }
        finally
        {
            KevlarDiagnostics.OnCallbackError -= handler;
        }
    }

    [Test]
    [NotInParallel]
    public async Task ResultFormatter_Exception_Is_Swallowed_And_Reported()
    {
        var logger = new FakeLogger();
        var formatterFailure = new TestException("formatter");
        var shieldName = $"logging-{Guid.NewGuid():N}";
        var measurements = new List<string>();
        CallbackErrorEvent? reported = null;
        Action<CallbackErrorEvent> handler = callback => reported = callback;
        using var listener = CreateCallbackErrorListener(shieldName, measurements);
        KevlarDiagnostics.OnCallbackError += handler;
        try
        {
            var shield = Shield.For<int>()
                .WhenResult(-1)
                .Retry(1, Backoff.None)
                .WithName(shieldName)
                .WithLogging(logger, options => options.ResultFormatter = _ => throw formatterFailure);

            var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(-1));

            await Assert.That(result).IsEqualTo(-1);
            await Assert.That(reported?.Kind).IsEqualTo(CallbackErrorKind.Logging);
            await Assert.That(ReferenceEquals(reported?.Exception, formatterFailure)).IsTrue();
            await Assert.That(measurements).IsEquivalentTo(["logging"]);
            await Assert.That(logger.Collector.GetSnapshot().Any(record =>
                record.Id == new EventId(1008, "CallbackError"))).IsTrue();
        }
        finally
        {
            KevlarDiagnostics.OnCallbackError -= handler;
        }
    }

    [Test]
    [NotInParallel]
    public async Task Logger_Exception_Is_Swallowed_And_Reported_Once()
    {
        CallbackErrorEvent? reported = null;
        var reports = 0;
        Action<CallbackErrorEvent> handler = callback =>
        {
            reported = callback;
            reports++;
        };
        KevlarDiagnostics.OnCallbackError += handler;
        try
        {
            var logger = new ThrowingLogger();
            var shield = Shield.Retry(1, Backoff.None).WithLogging(logger);

            var outcome = await shield.ExecuteOutcomeAsync<int>(static _ =>
                new ValueTask<int>(Task.FromException<int>(new TestException("execution"))));

            await Assert.That(outcome.Exception).IsTypeOf<TestException>();
            await Assert.That(reports).IsEqualTo(1);
            await Assert.That(reported?.Kind).IsEqualTo(CallbackErrorKind.Logging);
            await Assert.That(ReferenceEquals(reported?.Exception, logger.Failure)).IsTrue();
        }
        finally
        {
            KevlarDiagnostics.OnCallbackError -= handler;
        }
    }

    [Test]
    [NotInParallel]
    public async Task Log_Volume_Under_Retry_Storm_Is_Bounded()
    {
        var logger = new FakeLogger();
        var shield = Shield.Retry(1, Backoff.None)
            .WithLogging(logger, options => options.MaxLogsPerSecond = 5);

        for (var iteration = 0; iteration < 100; iteration++)
        {
            _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
                new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));
        }

        await Assert.That(logger.Collector.Count).IsEqualTo(5);
    }

    [Test]
    [NotInParallel]
    public async Task Zero_Log_Volume_Skips_First_Result_Capture()
    {
        var logger = new FakeLogger();
        var formatterCalls = 0;
        var shield = Shield.For<int>()
            .WhenResult(-1)
            .Retry(1, Backoff.None)
            .WithLogging(logger, options =>
            {
                options.MaxLogsPerSecond = 0;
                options.ResultFormatter = _ =>
                {
                    formatterCalls++;
                    return "unexpected";
                };
            });

        _ = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(-1));

        await Assert.That(logger.Collector.Count).IsEqualTo(0);
        await Assert.That(formatterCalls).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task Concurrent_Executions_Log_Every_Retry()
    {
        var logger = new FakeLogger();
        var shield = Shield.Retry(1, Backoff.None).WithLogging(logger);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(iteration => Task.Run(async () =>
        {
            var outcome = await shield.ExecuteOutcomeAsync<int>(static _ =>
                new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));
            _ = outcome;
            _ = iteration;
        })));

        await Assert.That(logger.Collector.Count).IsEqualTo(32);
    }

    [Test]
    [NotInParallel]
    public async Task Breaker_HalfOpen_And_Closed_Log_Information()
    {
        var logger = new FakeLogger();
        var time = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(1))
            .WithTimeProvider(time)
            .WithLogging(logger);

        _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
            new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));
        time.Advance(TimeSpan.FromSeconds(1));
        _ = await shield.ExecuteAsync(static _ => new ValueTask<int>(42));

        var transitions = logger.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1003, "CircuitState"))
            .ToArray();
        await Assert.That(transitions.Length).IsEqualTo(3);
        await Assert.That(transitions[1].GetStructuredStateValue("ToState")).IsEqualTo("HalfOpen");
        await Assert.That(transitions[1].Level).IsEqualTo(LogLevel.Information);
        await Assert.That(transitions[2].GetStructuredStateValue("ToState")).IsEqualTo("Closed");
        await Assert.That(transitions[2].Level).IsEqualTo(LogLevel.Information);
        await Assert.That(transitions[2].GetStructuredStateValue("Outcome")).IsEqualTo("success");
    }

    [Test]
    [NotInParallel]
    public async Task Composition_Attaches_Manual_Circuit_Listeners()
    {
        var logger = new FakeLogger();
        var wrappedMonitor = new CircuitBreakerMonitor();
        var composedMonitor = new CircuitBreakerMonitor();
        var loggedEmpty = Shield.Empty.WithLogging(logger);
        var wrapped = loggedEmpty.Wrap(
            Shield.CircuitBreaker(options => options.Monitor = wrappedMonitor));
        var composed = Shield.Compose(
            loggedEmpty,
            Shield.CircuitBreaker(options => options.Monitor = composedMonitor));

        wrappedMonitor.Isolate();
        composedMonitor.Isolate();

        var transitions = logger.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1003, "CircuitState"))
            .ToArray();
        await Assert.That(transitions.Length).IsEqualTo(2);
        GC.KeepAlive(wrapped);
        GC.KeepAlive(composed);
    }

    [Test]
    [NotInParallel]
    public async Task Composition_Uses_The_Nearest_Circuit_Logging_Observer()
    {
        var outerLogger = new FakeLogger();
        var innerLogger = new FakeLogger();
        var outerMonitor = new CircuitBreakerMonitor();
        var innerMonitor = new CircuitBreakerMonitor();
        var outer = Shield.CircuitBreaker(options => options.Monitor = outerMonitor)
            .WithLogging(outerLogger);
        var inner = Shield.CircuitBreaker(options => options.Monitor = innerMonitor)
            .WithLogging(innerLogger);
        var composed = outer.Wrap(inner);

        outerMonitor.Isolate();
        innerMonitor.Isolate();

        var outerTransitions = outerLogger.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1003, "CircuitState"))
            .ToArray();
        var innerTransitions = innerLogger.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1003, "CircuitState"))
            .ToArray();
        await Assert.That(outerTransitions.Length).IsEqualTo(1);
        await Assert.That(innerTransitions.Length).IsEqualTo(1);
        await Assert.That(outerTransitions[0].GetStructuredStateValue("StrategyIndex"))
            .IsEqualTo("0");
        await Assert.That(innerTransitions[0].GetStructuredStateValue("StrategyIndex"))
            .IsEqualTo("1");
        GC.KeepAlive(composed);
    }

    [Test]
    [NotInParallel]
    public async Task Isolated_Logs_Error()
    {
        var logger = new FakeLogger();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options => options.Monitor = monitor)
            .WithName("payments")
            .WithLogging(logger);

        monitor.Isolate();

        var record = logger.Collector.GetSnapshot().Single();
        await Assert.That(record.Id).IsEqualTo(new EventId(1003, "CircuitState"));
        await Assert.That(record.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(record.GetStructuredStateValue("ShieldName")).IsEqualTo("payments");
        await Assert.That(record.GetStructuredStateValue("FromState")).IsEqualTo("Closed");
        await Assert.That(record.GetStructuredStateValue("ToState")).IsEqualTo("Isolated");
        await Assert.That(record.StructuredState?.Any(pair => pair.Key == "BreakDuration") ?? false)
            .IsFalse();
        await Assert.That(record.Message).DoesNotContain(" for ");
        GC.KeepAlive(shield);
    }

    [Test]
    [NotInParallel]
    public async Task Naming_A_Logged_Shield_Refreshes_Manual_Circuit_Metadata()
    {
        var logger = new FakeLogger();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options => options.Monitor = monitor)
            .WithLogging(logger)
            .WithName("catalog");

        monitor.Isolate();

        var record = logger.Collector.GetSnapshot().Single();
        await Assert.That(record.GetStructuredStateValue("ShieldName")).IsEqualTo("catalog");
        await Assert.That(record.GetStructuredStateValue("ToState")).IsEqualTo("Isolated");
        GC.KeepAlive(shield);
    }

    [Test]
    [NotInParallel]
    public async Task Breaker_Appended_After_Logging_Logs_Manual_Isolation()
    {
        var logger = new FakeLogger();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.Empty
            .WithLogging(logger)
            .CircuitBreaker(options => options.Monitor = monitor);

        monitor.Isolate();

        var record = logger.Collector.GetSnapshot().Single();
        await Assert.That(record.Id).IsEqualTo(new EventId(1003, "CircuitState"));
        await Assert.That(record.GetStructuredStateValue("ToState")).IsEqualTo("Isolated");
        GC.KeepAlive(shield);
    }

    private static MeterListener CreateCallbackErrorListener(
        string shieldName,
        List<string> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, activeListener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName
                    && instrument.Name == "kevlar.callback_errors")
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? observedShield = null;
            string? kind = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "kevlar.shield.name")
                {
                    observedShield = tag.Value?.ToString();
                }
                else if (tag.Key == "kevlar.callback.kind")
                {
                    kind = tag.Value?.ToString();
                }
            }

            if (string.Equals(observedShield, shieldName, StringComparison.Ordinal)
                && kind is not null)
            {
                measurements.Add(kind);
            }
        });
        listener.Start();
        return listener;
    }

    private sealed class TestException(string message) : Exception(message);

    private sealed class ThrowingLogger : ILogger
    {
        public Exception Failure { get; } = new TestException("logger");

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => throw Failure;
    }

    private sealed class ScopeLogger(IDisposable scope) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => scope;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class TrackingScope(Exception? failure = null) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
