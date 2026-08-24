using System.Reflection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

[NotInParallel]
public class ExecutionContextPlumbingTests
{
    private const string ExceptionProxyDataKey =
        "Kevlar.Internal.ExceptionProxy.6b21d876-5f0c-45d4-a873-cd6d83e9158b";

    [Test]
    public async Task Every_Context_Initializer_Has_An_Exact_Null_Guard()
    {
        var methods = GetContextInitializerMethods().ToArray();

        await Assert.That(methods.Length > 0).IsTrue();
        foreach (var method in methods)
        {
            var closedMethod = CloseGenericMethod(method);
            var target = closedMethod.IsStatic ? null : CreateShield(closedMethod.DeclaringType!);
            var arguments = closedMethod.GetParameters()
                .Select(parameter => CreateArgument(parameter, nullInitializer: true))
                .ToArray();

            var exception = CaptureReflectionFailure(() => closedMethod.Invoke(target, arguments));

            await Assert.That(exception).IsTypeOf<ArgumentNullException>();
            await Assert.That(((ArgumentNullException)exception).ParamName)
                .IsEqualTo("initializeProperties");
        }
    }

    [Test]
    public async Task Synchronous_Context_Action_Failure_Returns_The_Exact_Context_Clean()
    {
        var key = new KevlarKey<string>("dirty");
        var expected = new InvalidOperationException("context action failed");
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        KevlarContext? captured = null;
        var shield = Shield.Empty
            .WithName("sync-context-failure")
            .WithTimeProvider(timeProvider);

        var failure = await Assert.That(() => shield.ExecuteWithContext<int, int>(
                42,
                (_, properties) => properties.Set(key, "value"),
                (_, context) =>
                {
                    captured = context;
                    throw expected;
                },
                cancellation.Token))
            .Throws<InvalidOperationException>();

        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(captured).IsNotNull();
        KevlarContext.AllowPooledInspection(captured!);
        await Assert.That(captured!.Properties.TryGet(key, out _)).IsFalse();
        await Assert.That(captured.CancellationToken).IsEqualTo(default(CancellationToken));
        await Assert.That(captured.IsSynchronous).IsFalse();
        await Assert.That(captured.ShieldName).IsNull();
        await Assert.That(captured.TimeProvider).IsSameReferenceAs(TimeProvider.System);
    }

    [Test]
    public async Task Returning_Context_Clears_A_Large_Property_Bag()
    {
        var keys = Enumerable.Range(0, 40)
            .Select(index => new KevlarKey<object>($"property-{index}"))
            .ToArray();
        var values = keys.Select(_ => new object()).ToArray();
        KevlarContext? captured = null;

        var result = await Shield.Empty.ExecuteWithContextAsync(
            (keys, values),
            static (state, properties) =>
            {
                for (var index = 0; index < state.keys.Length; index++)
                {
                    properties.Set(state.keys[index], state.values[index]);
                }
            },
            (_, context) =>
            {
                captured = context;
                return new ValueTask<int>(42);
            });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(captured).IsNotNull();
        KevlarContext.AllowPooledInspection(captured!);
        foreach (var key in keys)
        {
            await Assert.That(captured!.Properties.TryGet(key, out _)).IsFalse();
        }
    }

    [Test]
    public async Task Fork_Copies_Metadata_And_All_Properties_But_Isolates_Later_Writes()
    {
        var textKey = new KevlarKey<string>("text");
        var numberKey = new KevlarKey<int>("number");
        var nullKey = new KevlarKey<string?>("nullable");
        var timeProvider = new FakeTimeProvider();
        using var parentCancellation = new CancellationTokenSource();
        using var forkCancellation = new CancellationTokenSource();
        var parent = KevlarContext.Rent(
            parentCancellation.Token,
            isSynchronous: true,
            timeProvider,
            "parent");
        parent.StrategyIndex = 7;
        parent.Properties.Set(textKey, "original");
        parent.Properties.Set(numberKey, 42);
        parent.Properties.Set(nullKey, null);
        var fork = parent.Fork(forkCancellation.Token);

        try
        {
            parent.Properties.Set(textKey, "changed-parent");
            fork.Properties.Set(numberKey, 84);

            await Assert.That(fork.CancellationToken).IsEqualTo(forkCancellation.Token);
            await Assert.That(fork.IsSynchronous).IsTrue();
            await Assert.That(fork.TimeProvider).IsSameReferenceAs(timeProvider);
            await Assert.That(fork.ShieldName).IsEqualTo("parent");
            await Assert.That(fork.StrategyIndex).IsEqualTo(7);
            await Assert.That(fork.Properties.GetOrDefault<string>(textKey)).IsEqualTo("original");
            await Assert.That(fork.Properties.GetOrDefault(numberKey)).IsEqualTo(84);
            await Assert.That(fork.Properties.TryGet(nullKey, out var nullValue)).IsTrue();
            await Assert.That(nullValue).IsNull();
            await Assert.That(parent.Properties.GetOrDefault(numberKey)).IsEqualTo(42);
            await Assert.That(parent.Properties.GetOrDefault<string>(textKey)).IsEqualTo("changed-parent");
        }
        finally
        {
            KevlarContext.Return(fork);
            KevlarContext.Return(parent);
        }
    }

    [Test]
    public async Task Exception_Proxy_Never_Leaks_From_Public_Outcome_Members()
    {
        var original = new InvalidOperationException("original");
        var proxy = new Exception("transport proxy");
        proxy.Data[ExceptionProxyDataKey] = original;
        var outcome = Outcome<int>.FromException(proxy);

        var failure = await Assert.That(() => outcome.GetResultOrRethrow())
            .Throws<InvalidOperationException>();

        await Assert.That(outcome.Exception).IsSameReferenceAs(original);
        await Assert.That(failure).IsSameReferenceAs(original);
        await Assert.That(outcome.ToString()).IsEqualTo(original.ToString());
    }

    private static IEnumerable<MethodInfo> GetContextInitializerMethods() =>
        typeof(Shield).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Concat(typeof(Shield<int>).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Concat(typeof(VoidShield).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Concat(typeof(ShieldTaskExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(static method => method.Name is "ExecuteWithContext" or "ExecuteWithContextAsync")
            .Where(static method => method.GetParameters()
                .Any(parameter => parameter.Name == "initializeProperties"));

    private static MethodInfo CloseGenericMethod(MethodInfo method) =>
        method.IsGenericMethodDefinition
            ? method.MakeGenericMethod(Enumerable.Repeat(typeof(int), method.GetGenericArguments().Length).ToArray())
            : method;

    private static object CreateShield(Type type) => type == typeof(Shield)
        ? Shield.Empty
        : type == typeof(Shield<int>)
            ? Shield<int>.Empty
            : CreateVoidShield();

    private static object? CreateArgument(ParameterInfo parameter, bool nullInitializer)
    {
        if (parameter.Name == "initializeProperties" && nullInitializer)
        {
            return null;
        }

        if (parameter.Name == "shield")
        {
            return CreateShield(parameter.ParameterType);
        }

        if (parameter.Name == "cancellationToken")
        {
            return default(CancellationToken);
        }

        return parameter.ParameterType.IsValueType
            ? Activator.CreateInstance(parameter.ParameterType)
            : null;
    }

    private static VoidShield CreateVoidShield() =>
        Shield.Fallback(static _ => ValueTask.CompletedTask);

    private static Exception CaptureReflectionFailure(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected a null guard failure.");
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException!;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
