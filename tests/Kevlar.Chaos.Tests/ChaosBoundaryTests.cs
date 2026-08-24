using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Chaos.Tests;

public class ChaosBoundaryTests
{
    [Test]
    public async Task Decision_Filters_Short_Circuit_In_Declared_Order()
    {
        var calls = new List<string>();
        var enabled = false;
        var shield = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.EnabledGenerator = _ =>
            {
                calls.Add("enabled");
                return enabled;
            };
            options.Predicate = _ =>
            {
                calls.Add("predicate");
                return false;
            };
            options.InjectionRateGenerator = _ =>
            {
                calls.Add("rate");
                return 1;
            };
            options.ExceptionGenerator = _ =>
            {
                calls.Add("exception");
                return new ChaosInjectedException();
            };
            options.OnInjected = _ => calls.Add("injected");
        });

        var disabled = await shield.ExecuteAsync(static _ => new ValueTask<int>(1));
        enabled = true;
        var rejected = await shield.ExecuteAsync(static _ => new ValueTask<int>(2));

        await Assert.That(disabled).IsEqualTo(1);
        await Assert.That(rejected).IsEqualTo(2);
        await Assert.That(string.Join(",", calls)).IsEqualTo("enabled,enabled,predicate");
    }

    [Test]
    public async Task Zero_Rate_Skips_Payload_Callback_And_Continues()
    {
        var resultGeneratorCalls = 0;
        var injectionCallbacks = 0;
        var actionCalls = 0;
        var shield = ChaosShield.Outcome<int>(options =>
        {
            options.Enabled = true;
            options.InjectionRate = 0;
            options.ResultGenerator = _ =>
            {
                resultGeneratorCalls++;
                return -1;
            };
            options.OnInjected = _ => injectionCallbacks++;
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(resultGeneratorCalls).IsEqualTo(0);
        await Assert.That(injectionCallbacks).IsEqualTo(0);
        await Assert.That(actionCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Outcome_Can_Inject_Null_And_Skip_The_Action()
    {
        var actionCalls = 0;
        var shield = ChaosShield.Outcome<string>(options =>
        {
            options.Enabled = true;
            options.Result = null;
        });

        var outcome = await shield.ExecuteOutcomeAsync(_ =>
        {
            actionCalls++;
            return new ValueTask<string>("real");
        });

        await Assert.That(outcome.Exception).IsNull();
        await Assert.That(outcome.Result).IsNull();
        await Assert.That(actionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Invalid_Generated_Delay_Fails_Before_Notification_Or_Action()
    {
        var injections = 0;
        var actionCalls = 0;
        var shield = ChaosShield.Latency(options =>
        {
            options.Enabled = true;
            options.DelayGenerator = static _ => TimeSpan.FromDays(100);
            options.OnInjected = _ => injections++;
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(42);
        });

        await Assert.That(outcome.Exception).IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(injections).IsEqualTo(0);
        await Assert.That(actionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Generated_Zero_Delay_Notifies_Then_Continues()
    {
        var order = new List<string>();
        var shield = ChaosShield.Latency(options =>
        {
            options.Enabled = true;
            options.DelayGenerator = _ =>
            {
                order.Add("delay");
                return TimeSpan.Zero;
            };
            options.OnInjected = _ => order.Add("injected");
        }).WithTimeProvider(new FakeTimeProvider());

        var result = await shield.ExecuteAsync(_ =>
        {
            order.Add("action");
            return new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(string.Join(",", order)).IsEqualTo("delay,injected,action");
    }

    [Test]
    public async Task Scope_Labels_Are_Ordinal_Case_Sensitive()
    {
        var shield = ChaosShield.Fault(options =>
        {
            options.Enabled = true;
            options.Operation = "Checkout";
            options.Environment = "Test";
        });

        Outcome<int> wrongCase;
        using (ChaosScope.Begin("checkout", "test"))
        {
            wrongCase = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(1));
        }

        Outcome<int> exact;
        using (ChaosScope.Begin("Checkout", "Test"))
        {
            exact = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(2));
        }

        await Assert.That(wrongCase.Result).IsEqualTo(1);
        await Assert.That(exact.Exception).IsTypeOf<ChaosInjectedException>();
    }

    [Test]
    public async Task Scope_State_Is_Isolated_Between_Concurrent_Async_Flows()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            using (ChaosScope.Begin("first", "one"))
            {
                firstEntered.SetResult();
                await secondEntered.Task;
                return (ChaosScope.Operation, ChaosScope.Environment);
            }
        });
        var second = Task.Run(async () =>
        {
            await firstEntered.Task;
            using (ChaosScope.Begin("second", "two"))
            {
                secondEntered.SetResult();
                await Task.Yield();
                return (ChaosScope.Operation, ChaosScope.Environment);
            }
        });

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(results[0]).IsEqualTo(("first", "one"));
        await Assert.That(results[1]).IsEqualTo(("second", "two"));
        await Assert.That(ChaosScope.Operation).IsNull();
        await Assert.That(ChaosScope.Environment).IsNull();
    }

    [Test]
    public async Task Default_Injected_Exception_Has_Stable_Contract()
    {
        var cause = new InvalidOperationException("cause");
        var defaultException = new ChaosInjectedException();
        var custom = new ChaosInjectedException("custom");
        var wrapped = new ChaosInjectedException("wrapped", cause);

        await Assert.That(defaultException.Message).IsEqualTo("A fault was injected by Kevlar.Chaos.");
        await Assert.That(custom.Message).IsEqualTo("custom");
        await Assert.That(wrapped.Message).IsEqualTo("wrapped");
        await Assert.That(ReferenceEquals(wrapped.InnerException, cause)).IsTrue();
    }
}
