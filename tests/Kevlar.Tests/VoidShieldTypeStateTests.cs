namespace Kevlar.Tests;

public class VoidShieldTypeStateTests
{
    [Test]
    public async Task Fallback_Transitions_To_VoidShield_And_Later_Strategies_Preserve_It()
    {
        var attempts = 0;
        var fallbackCalls = 0;
        var shield = Shield
            .Fallback(_ =>
            {
                fallbackCalls++;
                return ValueTask.CompletedTask;
            })
            .Retry(1, Backoff.None)
            .Timeout(TimeSpan.FromMinutes(1))
            .WithName("void-chain");

        await shield.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(shield).IsTypeOf<VoidShield>();
        await Assert.That(shield.Name).IsEqualTo("void-chain");
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(fallbackCalls).IsEqualTo(1);
    }

    [Test]
    public async Task VoidShieldBuilder_Preserves_The_Restriction_And_Clause()
    {
        var attempts = 0;
        var builder = Shield
            .Fallback(static _ => ValueTask.CompletedTask)
            .When<InvalidOperationException>();
        var shield = builder.Or<TimeoutException>().Retry(1, Backoff.None);

        await shield.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(builder).IsTypeOf<VoidShieldBuilder>();
        await Assert.That(shield).IsTypeOf<VoidShield>();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(shield.ToString()).Contains("InvalidOperationException | TimeoutException");
    }

    [Test]
    public async Task Wrap_Propagates_VoidOnly_State_In_Either_Order()
    {
        var innerFallbackCalls = 0;
        var outerFallbackCalls = 0;
        var innerVoid = Shield.Timeout(TimeSpan.FromMinutes(1)).Wrap(
            Shield.Fallback(_ =>
            {
                innerFallbackCalls++;
                return ValueTask.CompletedTask;
            }));
        var outerVoid = Shield
            .Fallback(_ =>
            {
                outerFallbackCalls++;
                return ValueTask.CompletedTask;
            })
            .Wrap(Shield.Retry(1, Backoff.None));

        await innerVoid.ExecuteAsync(_ => throw new InvalidOperationException());
        await outerVoid.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(innerVoid).IsTypeOf<VoidShield>();
        await Assert.That(outerVoid).IsTypeOf<VoidShield>();
        await Assert.That(innerFallbackCalls).IsEqualTo(1);
        await Assert.That(outerFallbackCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Task_And_Context_Execution_Overloads_Remain_Available()
    {
        var calls = 0;
        var shield = Shield.Fallback(static _ => ValueTask.CompletedTask);

        await shield.ExecuteAsync(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        await shield.ExecuteAsync(1, (state, _) =>
        {
            calls += state;
            return Task.CompletedTask;
        });
        await shield.ExecuteWithContextAsync(context =>
        {
            calls += context.IsSynchronous ? 0 : 1;
            return Task.CompletedTask;
        });

        await Assert.That(calls).IsEqualTo(3);
    }

    [Test]
    public async Task Public_Surface_Contains_No_Result_Execution_Escape()
    {
        var executionMethods = typeof(VoidShield)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(static method => method.Name.StartsWith("Execute", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(typeof(Shield).IsAssignableFrom(typeof(VoidShield))).IsFalse();
        await Assert.That(typeof(VoidShield).GetMethod("For")).IsNull();
        await Assert.That(executionMethods).IsNotEmpty();
        await Assert.That(executionMethods).All(static method =>
            method.ReturnType == typeof(void) || method.ReturnType == typeof(ValueTask));
        await Assert.That(executionMethods.Any(static method => method.Name == "ExecuteOutcomeAsync")).IsFalse();
    }

    [Test]
    public async Task VoidShieldBuilder_Remains_Immutable_When_Branched()
    {
        var builder = Shield
            .Fallback(static _ => ValueTask.CompletedTask)
            .When<InvalidOperationException>();
        var timeoutBranch = builder.Or<TimeoutException>().Retry(0, Backoff.None);
        var argumentBranch = builder.Or<ArgumentException>().Retry(0, Backoff.None);

        await Assert.That(timeoutBranch.ToString()).Contains("TimeoutException");
        await Assert.That(timeoutBranch.ToString()).DoesNotContain("ArgumentException");
        await Assert.That(argumentBranch.ToString()).Contains("ArgumentException");
        await Assert.That(argumentBranch.ToString()).DoesNotContain("TimeoutException");
    }
}
