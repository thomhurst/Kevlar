using System.Linq.Expressions;
using System.Reflection;
using Kevlar.Extensions.Http;

namespace Kevlar.Tests;

public class OptionsCloneTests
{
    [Test]
    [Arguments(typeof(RetryOptions))]
    [Arguments(typeof(RetryOptions<int>))]
    [Arguments(typeof(CircuitBreakerOptions))]
    [Arguments(typeof(CircuitBreakerOptions<int>))]
    [Arguments(typeof(TimeoutOptions))]
    [Arguments(typeof(ConcurrencyLimitOptions))]
    [Arguments(typeof(HedgeOptions))]
    [Arguments(typeof(HedgeOptions<int>))]
    [Arguments(typeof(StandardHttpShieldOptions))]
    [Arguments(typeof(StandardHedgeShieldOptions))]
    [Arguments(typeof(ShieldHttpHandlerOptions))]
    [Arguments(typeof(HttpEndpointRoutingOptions))]
    public async Task Clone_Copies_Every_Property_And_Isolates_Mutable_Containers(Type type)
    {
        var source = CreateConfiguredOptions(type);
        var clone = type.GetMethod("Clone", Type.EmptyTypes)!.Invoke(source, null)!;

        await VerifyCopy(source, clone);
        await VerifyMutationIsolation(source, clone);
    }

    [Test]
    public async Task Standard_Clone_Preserves_Absent_Optional_Stages()
    {
        var source = new StandardHttpShieldOptions();
        var clone = source.Clone();

        await Assert.That(clone.ConcurrencyLimit).IsNull();
        await Assert.That(clone.Handler.Routing).IsNull();
        clone.Retry.MaxRetries = 17;
        clone.Handler.MaxBufferSize = 37;
        await Assert.That(source.Retry.MaxRetries).IsEqualTo(3);
        await Assert.That(source.Handler.MaxBufferSize).IsEqualTo(1024L * 1024);
    }

    [Test]
    public async Task Hedge_Clone_Preserves_Absent_Optional_Stages()
    {
        var source = new StandardHedgeShieldOptions();
        var clone = source.Clone();

        await Assert.That(clone.ConcurrencyLimit).IsNull();
        await Assert.That(clone.Routing).IsNull();
        await Assert.That(clone.Handler.Routing).IsNull();
        clone.Hedge.MaxHedgedAttempts = 17;
        clone.AttemptTimeout.Timeout = TimeSpan.FromSeconds(37);
        await Assert.That(source.Hedge.MaxHedgedAttempts).IsEqualTo(1);
        await Assert.That(source.AttemptTimeout.Timeout).IsEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Clone_Preserves_Shared_Budget_Monitor_And_Captured_Delegate_State()
    {
        var calls = 0;
        var source = new StandardHttpShieldOptions();
        source.Retry.Budget = new RetryBudget(maxTokens: 7);
        source.Retry.OnRetry = _ => { calls++; return default; };
        source.CircuitBreaker.Monitor = new CircuitBreakerMonitor();

        var clone = source.Clone();

        await Assert.That(ReferenceEquals(source.Retry.Budget, clone.Retry.Budget)).IsTrue();
        await Assert.That(ReferenceEquals(source.CircuitBreaker.Monitor, clone.CircuitBreaker.Monitor)).IsTrue();
        await clone.Retry.OnRetry!(default);
        await source.Retry.OnRetry!(default);
        await Assert.That(calls).IsEqualTo(2);
    }

    private static object CreateConfiguredOptions(Type type)
    {
        var options = Activator.CreateInstance(type)!;
        foreach (var property in SettableProperties(type))
        {
            var value = CreateValue(property.PropertyType, property.GetValue(options), alternate: false);
            if (Equals(value, property.GetValue(options)))
            {
                throw new InvalidOperationException($"The guard needs a non-default value for {type.Name}.{property.Name}.");
            }
            property.SetValue(options, value);
        }

        if (options is HttpEndpointRoutingOptions routing)
        {
            routing.Endpoints.Add(new HttpEndpoint(new Uri("https://first.test"), weight: 3));
            routing.Endpoints.Add(new HttpEndpoint(new Uri("https://second.test"), weight: 7));
        }

        return options;
    }

    private static async Task VerifyCopy(object source, object clone)
    {
        await Assert.That(ReferenceEquals(source, clone)).IsFalse();
        foreach (var property in SettableProperties(source.GetType()))
        {
            var expected = property.GetValue(source)!;
            var actual = property.GetValue(clone);
            if (IsConfiguration(property.PropertyType))
            {
                if (actual is null)
                {
                    throw new InvalidOperationException($"Clone omitted {source.GetType().Name}.{property.Name}.");
                }
                await VerifyCopy(expected, actual);
            }
            else if (!Equals(expected, actual)
                     || !property.PropertyType.IsValueType && !ReferenceEquals(expected, actual))
            {
                throw new InvalidOperationException($"Clone omitted {source.GetType().Name}.{property.Name}.");
            }
        }

        if (source is HttpEndpointRoutingOptions routing)
        {
            var copiedRouting = (HttpEndpointRoutingOptions)clone;
            await Assert.That(ReferenceEquals(routing.Endpoints, copiedRouting.Endpoints)).IsFalse();
            await Assert.That(copiedRouting.Endpoints.Count).IsEqualTo(routing.Endpoints.Count);
            for (var index = 0; index < routing.Endpoints.Count; index++)
            {
                await Assert.That(ReferenceEquals(routing.Endpoints[index], copiedRouting.Endpoints[index])).IsTrue();
            }
        }
    }

    private static async Task VerifyMutationIsolation(object source, object clone)
    {
        foreach (var property in SettableProperties(source.GetType()))
        {
            var original = property.GetValue(source)!;
            if (IsConfiguration(property.PropertyType))
            {
                await VerifyMutationIsolation(original, property.GetValue(clone)!);
            }

            property.SetValue(clone, CreateValue(property.PropertyType, property.GetValue(clone), alternate: true));
            await Assert.That(property.GetValue(source)).IsEqualTo(original);
        }

        if (source is HttpEndpointRoutingOptions routing)
        {
            ((HttpEndpointRoutingOptions)clone).Endpoints.Clear();
            await Assert.That(routing.Endpoints.Count).IsEqualTo(2);
        }
    }

    private static IEnumerable<PropertyInfo> SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.SetMethod?.IsPublic is true);

    private static bool IsConfiguration(Type type) =>
        type.Name.Contains("Options", StringComparison.Ordinal);

    private static object CreateValue(Type type, object? current, bool alternate)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsConfiguration(type)) return CreateConfiguredOptions(type);
        if (type == typeof(string)) return alternate ? "derived" : "base";
        if (type == typeof(int)) return alternate ? 91 : 37;
        if (type == typeof(long)) return alternate ? 91L : 37L;
        if (type == typeof(double)) return alternate ? 0.91 : 0.73;
        if (type == typeof(bool)) return !(bool)current!;
        if (type == typeof(TimeSpan)) return TimeSpan.FromSeconds(alternate ? 91 : 73);
        if (type == typeof(Backoff)) return alternate ? Backoff.Default : Backoff.None;
        if (type == typeof(RetryBudget)) return new RetryBudget(maxTokens: 7);
        if (type == typeof(CircuitBreakerMonitor)) return new CircuitBreakerMonitor();
        if (type.IsEnum) return Enum.GetValues(type).Cast<object>().First(value => !Equals(value, current));
        if (typeof(Delegate).IsAssignableFrom(type))
        {
            var invoke = type.GetMethod("Invoke")!;
            return Expression.Lambda(type, Expression.Default(invoke.ReturnType),
                invoke.GetParameters().Select(static parameter => Expression.Parameter(parameter.ParameterType))).Compile();
        }
        throw new InvalidOperationException($"Add a non-default clone-test value for {type}.");
    }
}
