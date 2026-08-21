using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Chaos.Tests;

public class ChaosStrategyTests
{
    [Test]
    public async Task Chaos_Is_Disabled_By_Default_For_Every_Injection_Type()
    {
        var behaviorCalls = 0;
        var actionCalls = 0;
        var fault = ChaosShield.Fault(_ => { });
        var latency = ChaosShield.Latency(options => options.Delay = TimeSpan.FromDays(1));
        var outcome = ChaosShield.Outcome<int>(options => options.Result = -1);
        var behavior = ChaosShield.Behavior(options => options.Behavior = _ =>
        {
            behaviorCalls++;
            return ValueTask.CompletedTask;
        });

        var faultResult = await fault.ExecuteAsync(_ => new ValueTask<int>(1));
        var latencyResult = await latency.ExecuteAsync(_ => new ValueTask<int>(2));
        var outcomeResult = await outcome.ExecuteAsync(_ => new ValueTask<int>(3));
        var behaviorResult = await behavior.ExecuteAsync(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(4);
        });

        await Assert.That(faultResult).IsEqualTo(1);
        await Assert.That(latencyResult).IsEqualTo(2);
        await Assert.That(outcomeResult).IsEqualTo(3);
        await Assert.That(behaviorResult).IsEqualTo(4);
        await Assert.That(behaviorCalls).IsEqualTo(0);
        await Assert.That(actionCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Latency_Uses_TimeProvider_And_Waits_Before_The_Action()
    {
        var timeProvider = new FakeTimeProvider();
        var actionCalls = 0;
        var shield = ChaosShield.Latency(options =>
        {
            options.Enabled = true;
            options.Delay = TimeSpan.FromSeconds(2);
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(42);
        }).AsTask();

        await Assert.That(execution.IsCompleted).IsFalse();
        await Assert.That(actionCalls).IsEqualTo(0);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(actionCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Latency_Cancellation_Preserves_The_Caller_Token_And_Skips_The_Action()
    {
        using var cancellation = new CancellationTokenSource();
        var actionCalls = 0;
        var shield = ChaosShield.Latency(options =>
        {
            options.Enabled = true;
            options.Delay = TimeSpan.FromDays(1);
        });

        var execution = shield.ExecuteAsync(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(42);
        }, cancellation.Token).AsTask();
        cancellation.Cancel();

        OperationCanceledException? caught = null;
        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(actionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Fault_Short_Circuits_With_Exact_Exception_For_Result_And_Void_Execution()
    {
        var injected = new TestException("injected");
        var actionCalls = 0;
        var shield = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.Exception = injected;
        });

        var typed = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(42);
        });
        Exception? untyped = null;
        try
        {
            await shield.ExecuteAsync(_ =>
            {
                actionCalls++;
                return ValueTask.CompletedTask;
            });
        }
        catch (Exception exception)
        {
            untyped = exception;
        }

        await Assert.That(ReferenceEquals(typed.Exception, injected)).IsTrue();
        await Assert.That(ReferenceEquals(untyped, injected)).IsTrue();
        await Assert.That(actionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Typed_Outcome_Preserves_Result_Identity_And_Composes_With_Fallback()
    {
        var injected = new Payload("injected");
        var recovered = new Payload("recovered");
        var chaos = ChaosShield.Outcome<Payload>(options =>
        {
            options.Enabled = true;
            options.Result = injected;
        });
        var shield = Shield.For<Payload>()
            .WhenResult(value => ReferenceEquals(value, injected))
            .Fallback(recovered)
            .Wrap(chaos);

        var chaosOutcome = await chaos.ExecuteOutcomeAsync(_ => new ValueTask<Payload>(new Payload("real")));
        var result = await shield.ExecuteAsync(_ => new ValueTask<Payload>(new Payload("real")));

        await Assert.That(ReferenceEquals(chaosOutcome.Result, injected)).IsTrue();
        await Assert.That(ReferenceEquals(result, recovered)).IsTrue();
    }

    [Test]
    public async Task Behavior_Is_Awaited_Before_Continuing_And_Can_Inject_An_Exact_Failure()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var shield = ChaosShield.Behavior(options =>
        {
            options.Enabled = true;
            options.Behavior = async _ =>
            {
                order.Add("behavior-start");
                started.SetResult();
                await release.Task;
                order.Add("behavior-end");
            };
        });

        var execution = shield.ExecuteAsync(_ =>
        {
            order.Add("action");
            return new ValueTask<int>(42);
        }).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(order).IsEquivalentTo(["behavior-start"]);
        release.SetResult();

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(string.Join(",", order)).IsEqualTo("behavior-start,behavior-end,action");

        var injected = new TestException("behavior");
        var failing = ChaosShield.Behavior(options =>
        {
            options.Enabled = true;
            options.Behavior = _ => ValueTask.FromException(injected);
        });
        var failed = await failing.ExecuteOutcomeAsync<int>(_ => new ValueTask<int>(1));

        await Assert.That(ReferenceEquals(failed.Exception, injected)).IsTrue();
    }

    [Test]
    public async Task Seeded_Rate_Is_Deterministic_And_The_Generator_Receives_Context()
    {
        var first = CreateSeededFault(seed: 42);
        var second = CreateSeededFault(seed: 42);
        var firstSequence = await ExecuteSequence(first, 32);
        var secondSequence = await ExecuteSequence(second, 32);

        await Assert.That(firstSequence.SequenceEqual(secondSequence)).IsTrue();
        await Assert.That(firstSequence.Distinct().Count()).IsEqualTo(2);

        var generated = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.InjectionRate = 0;
            options.InjectionRateGenerator = context => context.ShieldName == "inject" ? 1 : 0;
        }).WithName("inject");
        var outcome = await generated.ExecuteOutcomeAsync<int>(_ => new ValueTask<int>(42));

        await Assert.That(outcome.Exception).IsTypeOf<ChaosInjectedException>();
    }

    [Test]
    public async Task Scope_Bounds_Operation_And_Environment_And_Flows_Across_Awaits()
    {
        var shield = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.Operation = "checkout";
            options.Environment = "staging";
        });

        var outside = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        Outcome<int> wrong;
        using (ChaosScope.Begin(operation: "search", environment: "staging"))
        {
            wrong = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(2));
        }

        Outcome<int> matched;
        using (ChaosScope.Begin(operation: "checkout", environment: "staging"))
        {
            await Task.Yield();
            matched = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(3));
        }

        await Assert.That(outside.Result).IsEqualTo(1);
        await Assert.That(wrong.Result).IsEqualTo(2);
        await Assert.That(matched.Exception).IsTypeOf<ChaosInjectedException>();
        await Assert.That(ChaosScope.Operation).IsNull();
        await Assert.That(ChaosScope.Environment).IsNull();
    }

    [Test]
    public async Task Predicate_And_Dynamic_Enablement_Bound_The_Blast_Radius()
    {
        var killSwitch = true;
        var shield = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.EnabledGenerator = _ => killSwitch;
            options.Predicate = context => !context.IsSynchronous;
        });

        var injected = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        var synchronous = shield.Execute(_ => 2);
        killSwitch = false;
        var disabled = await shield.ExecuteAsync(_ => new ValueTask<int>(3));

        await Assert.That(injected.Exception).IsTypeOf<ChaosInjectedException>();
        await Assert.That(synchronous).IsEqualTo(2);
        await Assert.That(disabled).IsEqualTo(3);
    }

    [Test]
    public async Task Injection_Event_Identifies_Type_Scope_Rate_And_Context()
    {
        ChaosEvent observed = default;
        string? observedShieldName = null;
        var shield = ChaosShield.Outcome<int>(options =>
        {
            options.Enabled = true;
            options.Result = 42;
            options.OnInjected = injection =>
            {
                observed = injection;
                observedShieldName = injection.Context.ShieldName;
            };
        }).WithName("chaos-event");

        using (ChaosScope.Begin("checkout", "test"))
        {
            _ = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        }

        await Assert.That(observed.Kind).IsEqualTo(ChaosInjectionKind.Outcome);
        await Assert.That(observedShieldName).IsEqualTo("chaos-event");
        await Assert.That(observed.Operation).IsEqualTo("checkout");
        await Assert.That(observed.Environment).IsEqualTo("test");
        await Assert.That(observed.InjectionRate).IsEqualTo(1);
        await Assert.That(observed.Sample).IsEqualTo(0);
    }

    [Test]
    public async Task Configuration_Is_Snapshotted_When_The_Shield_Is_Built()
    {
        ChaosFaultOptions? captured = null;
        var shield = ChaosShield.Fault(options =>
        {
            options.Enabled = false;
            captured = options;
        });

        captured!.Enabled = true;
        captured.InjectionRate = 1;
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Sync_Execution_Supports_Every_Semantically_Valid_Injection()
    {
        var latency = ChaosShield.Latency(options => options.Enabled = true);
        var outcome = ChaosShield.Outcome<int>(options =>
        {
            options.Enabled = true;
            options.Result = 7;
        });
        var behaviorCalls = 0;
        var behavior = ChaosShield.Behavior(options =>
        {
            options.Enabled = true;
            options.Behavior = _ =>
            {
                behaviorCalls++;
                return ValueTask.CompletedTask;
            };
        });

        await Assert.That(latency.Execute(_ => 1)).IsEqualTo(1);
        await Assert.That(outcome.Execute(_ => 2)).IsEqualTo(7);
        await Assert.That(behavior.Execute(_ => 3)).IsEqualTo(3);
        await Assert.That(behaviorCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Dynamic_Generator_Failures_Are_Preserved_As_Outcomes()
    {
        var generatedFailure = new TestException("generator");
        var shield = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.InjectionRateGenerator = _ => throw generatedFailure;
        });

        var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));

        await Assert.That(ReferenceEquals(outcome.Exception, generatedFailure)).IsTrue();
    }

    [Test]
    public async Task Invalid_Fixed_And_Generated_Values_Are_Rejected()
    {
        await Assert.That(() => ChaosShield.Fault(options => options.InjectionRate = 1.1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ChaosShield.Latency(options => options.Delay = TimeSpan.FromMilliseconds(-1)))
            .Throws<ArgumentOutOfRangeException>();

        var generated = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.InjectionRateGenerator = _ => double.NaN;
        });
        var outcome = await generated.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));

        await Assert.That(outcome.Exception).IsTypeOf<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Concurrent_Executions_Safely_Share_Seeded_Random_State()
    {
        var shield = CreateSeededFault(42);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2_000).Select(async _ =>
            await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(1))));

        var injected = outcomes.Count(static outcome => outcome.Exception is ChaosInjectedException);
        await Assert.That(injected).IsGreaterThan(0);
        await Assert.That(injected).IsLessThan(outcomes.Length);
    }

    [Test]
    [NotInParallel]
    public async Task Metrics_Distinguish_Every_Injection_Kind()
    {
        var prefix = $"chaos-metrics-{Guid.NewGuid():N}";
        var observed = new ConcurrentDictionary<string, string>();
        string? observedOperation = null;
        string? observedEnvironment = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (instrument.Meter.Name == ChaosDiagnostics.MeterName
                && instrument.Name == "kevlar.chaos.injections")
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? shieldName = null;
            string? kind = null;
            string? operation = null;
            string? environment = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "kevlar.shield.name")
                {
                    shieldName = tag.Value?.ToString();
                }
                else if (tag.Key == "kevlar.chaos.kind")
                {
                    kind = tag.Value?.ToString();
                }
                else if (tag.Key == "kevlar.chaos.operation")
                {
                    operation = tag.Value?.ToString();
                }
                else if (tag.Key == "kevlar.chaos.environment")
                {
                    environment = tag.Value?.ToString();
                }
            }

            if (shieldName is not null && kind is not null && shieldName.StartsWith(prefix, StringComparison.Ordinal))
            {
                observed[shieldName] = kind;
                observedOperation = operation;
                observedEnvironment = environment;
            }
        });
        listener.Start();

        var latency = ChaosShield.Latency(static options => options.Enabled = true)
            .WithName($"{prefix}-latency");
        var fault = ChaosShield.Fault(static options => options.Enabled = true)
            .WithName($"{prefix}-fault");
        var outcome = ChaosShield.Outcome<int>(static options => options.Enabled = true)
            .WithName($"{prefix}-outcome");
        var behavior = ChaosShield.Behavior(static options =>
        {
            options.Enabled = true;
            options.Behavior = static _ => ValueTask.CompletedTask;
        })
            .WithName($"{prefix}-behavior");

        using (ChaosScope.Begin("metrics-operation", "metrics-environment"))
        {
            _ = await latency.ExecuteAsync(static _ => new ValueTask<int>(1));
            _ = await fault.ExecuteOutcomeAsync(static _ => new ValueTask<int>(1));
            _ = await outcome.ExecuteAsync(static _ => new ValueTask<int>(1));
            _ = await behavior.ExecuteAsync(static _ => new ValueTask<int>(1));
        }

        await Assert.That(observed[$"{prefix}-latency"]).IsEqualTo("latency");
        await Assert.That(observed[$"{prefix}-fault"]).IsEqualTo("fault");
        await Assert.That(observed[$"{prefix}-outcome"]).IsEqualTo("outcome");
        await Assert.That(observed[$"{prefix}-behavior"]).IsEqualTo("behavior");
        await Assert.That(observedOperation).IsEqualTo("metrics-operation");
        await Assert.That(observedEnvironment).IsEqualTo("metrics-environment");
    }

    [Test]
    public async Task Dynamic_Payload_Generators_Receive_Context()
    {
        var resultShield = ChaosShield.Outcome<string>(options =>
        {
            options.Enabled = true;
            options.ResultGenerator = context => context.ShieldName!;
        }).WithName("generated-result");
        var faultShield = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.ExceptionGenerator = context => new TestException(context.ShieldName!);
        }).WithName("generated-fault");

        var result = await resultShield.ExecuteAsync(static _ => new ValueTask<string>("real"));
        var fault = await faultShield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(1));

        await Assert.That(result).IsEqualTo("generated-result");
        await Assert.That(fault.Exception!.Message).IsEqualTo("generated-fault");
        await Assert.That(resultShield.ToString()).Contains("ChaosOutcome(dynamic)");
        await Assert.That(faultShield.ToString()).Contains("ChaosFault(dynamic)");
    }

    [Test]
    public async Task Nested_Scopes_Inherit_And_Restore_Labels()
    {
        using (ChaosScope.Begin(operation: "outer-operation", environment: "outer-environment"))
        {
            var inner = ChaosScope.Begin(operation: "inner-operation");
            await Assert.That(ChaosScope.Operation).IsEqualTo("inner-operation");
            await Assert.That(ChaosScope.Environment).IsEqualTo("outer-environment");
            inner.Dispose();
            inner.Dispose();

            await Assert.That(ChaosScope.Operation).IsEqualTo("outer-operation");
            await Assert.That(ChaosScope.Environment).IsEqualTo("outer-environment");
        }

        await Assert.That(ChaosScope.Operation).IsNull();
        await Assert.That(ChaosScope.Environment).IsNull();
    }

    [Test]
    [NotInParallel]
    public async Task Injection_Callback_Failure_Is_Preserved()
    {
        var injected = new TestException("callback");
        var shieldName = $"callback-failure-{Guid.NewGuid():N}";
        var measurements = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (instrument.Meter.Name == ChaosDiagnostics.MeterName
                && instrument.Name == "kevlar.chaos.injections")
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "kevlar.shield.name"
                    && string.Equals(tag.Value?.ToString(), shieldName, StringComparison.Ordinal))
                {
                    measurements++;
                }
            }
        });
        listener.Start();

        var shield = ChaosShield.Latency(options =>
        {
            options.Enabled = true;
            options.OnInjected = _ => throw injected;
        }).WithName(shieldName);

        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(1));

        await Assert.That(ReferenceEquals(outcome.Exception, injected)).IsTrue();
        await Assert.That(measurements).IsEqualTo(0);
    }

    [Test]
    public async Task Missing_Behavior_Skips_Decision_And_Injection_Callbacks()
    {
        var decisionCallbacks = 0;
        var injections = 0;
        var shield = ChaosShield.Behavior(options =>
        {
            options.Enabled = true;
            options.EnabledGenerator = _ =>
            {
                decisionCallbacks++;
                return true;
            };
            options.Predicate = _ =>
            {
                decisionCallbacks++;
                return true;
            };
            options.InjectionRateGenerator = _ =>
            {
                decisionCallbacks++;
                return 1;
            };
            options.OnInjected = _ => injections++;
        });

        var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(decisionCallbacks).IsEqualTo(0);
        await Assert.That(injections).IsEqualTo(0);
    }

    [Test]
    public async Task Factories_Reject_Null_Configuration_Callbacks()
    {
        await Assert.That(() => ChaosShield.Latency(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => ChaosShield.Fault(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => ChaosShield.Outcome<int>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => ChaosShield.Behavior(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Dynamic_Latency_Uses_Generated_Delay_And_Describes_Itself()
    {
        var shield = ChaosShield.Latency(options =>
        {
            options.Enabled = true;
            options.DelayGenerator = static _ => TimeSpan.Zero;
        });

        var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(shield.ToString()).Contains("ChaosLatency(dynamic)");
    }

    [Test]
    public async Task Null_Generated_Exception_Is_Reported_As_A_Configuration_Failure()
    {
        var shield = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.ExceptionGenerator = static _ => null!;
        });

        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));

        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
    }

    private static Shield CreateSeededFault(int seed) => ChaosShield.Fault(options =>
    {
        options.Enabled = true;
        options.InjectionRate = 0.5;
        options.Seed = seed;
    });

    private static async Task<bool[]> ExecuteSequence(Shield shield, int count)
    {
        var sequence = new bool[count];
        for (var index = 0; index < count; index++)
        {
            var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
            sequence[index] = outcome.Exception is ChaosInjectedException;
        }

        return sequence;
    }

    private sealed record Payload(string Value);

    private sealed class TestException(string message) : Exception(message);
}
