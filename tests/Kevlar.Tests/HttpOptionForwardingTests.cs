using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class HttpOptionForwardingTests
{
    [Test]
    [Arguments(typeof(HttpShield), typeof(TimeoutOptions))]
    [Arguments(typeof(HttpShield), typeof(RetryOptions<HttpResponseMessage>))]
    [Arguments(typeof(HttpShield), typeof(CircuitBreakerOptions<HttpResponseMessage>))]
    [Arguments(typeof(HttpShield), typeof(ConcurrencyLimitOptions))]
    [Arguments(typeof(ShieldHttpClientBuilderExtensions), typeof(TimeoutOptions))]
    [Arguments(typeof(ShieldHttpClientBuilderExtensions), typeof(HedgeOptions<HttpResponseMessage>))]
    [Arguments(typeof(ShieldHttpClientBuilderExtensions), typeof(CircuitBreakerOptions<HttpResponseMessage>))]
    [Arguments(typeof(ShieldHttpClientBuilderExtensions), typeof(ConcurrencyLimitOptions))]
    public async Task Copy_Preserves_Every_Public_Settable_Strategy_Property(Type owner, Type optionsType)
    {
        var source = Activator.CreateInstance(optionsType)!;
        var target = Activator.CreateInstance(optionsType)!;
        var properties = optionsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.SetMethod?.IsPublic is true).ToArray();
        foreach (var property in properties)
        {
            var value = CreateNonDefaultValue(property.PropertyType);
            await Assert.That(Equals(value, property.GetValue(source))).IsFalse();
            property.SetValue(source, value);
        }

        var retry = optionsType == typeof(RetryOptions<HttpResponseMessage>);
        var signature = retry ? new[] { optionsType, optionsType, typeof(bool) } : new[] { optionsType, optionsType };
        var copy = owner.GetMethod("Copy", BindingFlags.NonPublic | BindingFlags.Static, signature)!;
        // Disable HTTP delay composition to compare the original delegate by identity.
        copy.Invoke(null, retry ? [source, target, false] : [source, target]);
        foreach (var property in properties)
        {
            if (!Equals(property.GetValue(source), property.GetValue(target)))
            {
                throw new InvalidOperationException($"{owner.Name}.Copy omitted {optionsType.Name}.{property.Name}.");
            }
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Standard_Registration_Preserves_Shared_Retry_Budget(bool defaults, bool serviceProviderFactory)
    {
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromHours(1));
        var inner = new ResponseHandler();
        var services = new ServiceCollection();
        void Configure(StandardHttpShieldOptions options)
        {
            ConfigureStandard(options);
            options.Retry.Budget = budget;
        }
        void Register(IHttpClientBuilder builder)
        {
            builder.ConfigurePrimaryHttpMessageHandler(() => inner);
            if (serviceProviderFactory)
            {
                builder.AddStandardShield((_, options) => Configure(options));
            }
            else
            {
                builder.AddStandardShield(Configure);
            }
        }
        if (defaults)
        {
            services.ConfigureHttpClientDefaults(Register);
        }
        else
        {
            Register(services.AddHttpClient("standard"));
        }
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("standard");
        using var other = factory.CreateClient(defaults ? "other" : "standard");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            using var response = await (index % 2 == 0 ? client : other).GetAsync("https://dependency.test/");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }));
        await Assert.That(inner.Calls).IsEqualTo(13);
        await Assert.That(budget.Tokens).IsEqualTo(0);
    }

    [Test]
    public async Task Standard_Registration_Respects_Deadline_With_Composed_RetryAfter()
    {
        var inner = new ResponseHandler(retryAfter: TimeSpan.FromSeconds(20));
        var notifications = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IShieldDecorator>(new ClockDecorator(new FakeTimeProvider()));
        services.AddHttpClient("standard")
            .ConfigurePrimaryHttpMessageHandler(() => inner)
            .AddStandardShield(options =>
            {
                ConfigureStandard(options);
                options.TotalTimeout.Timeout = TimeSpan.FromSeconds(15);
                options.UseRetryAfterHeader = true;
                options.Retry.RespectDeadline = true;
                options.Retry.MaxDelay = TimeSpan.FromSeconds(30);
                options.Retry.DelayGenerator = async _ =>
                {
                    await Task.Yield();
                    return TimeSpan.FromSeconds(10);
                };
                options.Retry.OnRetry = _ => { notifications++; return default; };
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("standard");
        using var response = await client.GetAsync("https://dependency.test/").WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(inner.Calls).IsEqualTo(1);
        await Assert.That(notifications).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Standard_Registration_Preserves_Retry_Context_Predicates(bool exception)
    {
        var inner = new ResponseHandler(throwException: exception);
        var seenAttempts = new List<int>();
        var services = new ServiceCollection();
        services.AddHttpClient("standard")
            .ConfigurePrimaryHttpMessageHandler(() => inner)
            .AddStandardShield(options =>
            {
                ConfigureStandard(options);
                bool Handles(HandlingEvent<HttpResponseMessage> handling)
                {
                    seenAttempts.Add(handling.AttemptNumber);
                    return handling.AttemptNumber == 0;
                }
                if (exception)
                {
                    options.Retry.HandlesExceptionContext = Handles;
                }
                else
                {
                    options.Retry.HandlesResultContext = Handles;
                }
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("standard");
        if (exception)
        {
            await Assert.That(async () => await client.GetAsync("https://dependency.test/")).Throws<IOException>();
        }
        else
        {
            using var response = await client.GetAsync("https://dependency.test/");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
        await Assert.That(inner.Calls).IsEqualTo(2);
        await Assert.That(seenAttempts).IsEquivalentTo(new[] { 0, 1 });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Standard_Registration_Preserves_Breaker_Context_Predicates(bool exception)
    {
        var inner = new ResponseHandler(throwException: exception);
        var predicateCalls = 0;
        var services = new ServiceCollection();
        services.AddHttpClient("standard")
            .ConfigurePrimaryHttpMessageHandler(() => inner)
            .AddStandardShield(options =>
            {
                ConfigureStandard(options);
                options.Retry.MaxRetries = 0;
                options.CircuitBreaker.FailureRatio = null;
                options.CircuitBreaker.ConsecutiveFailures = 1;
                bool Handles(HandlingEvent<HttpResponseMessage> _)
                {
                    predicateCalls++;
                    return false;
                }
                if (exception)
                {
                    // IOException is not handled by the ambient HTTP clause. Override it locally.
                    options.CircuitBreaker.HandlesExceptionContext = handling =>
                    {
                        predicateCalls++;
                        return handling.Outcome.Exception is IOException;
                    };
                }
                else
                {
                    // A context predicate can also exclude otherwise transient responses.
                    options.CircuitBreaker.HandlesResultContext = Handles;
                }
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("standard");
        if (exception)
        {
            await Assert.That(async () => await client.GetAsync("https://dependency.test/")).Throws<IOException>();
            await Assert.That(async () => await client.GetAsync("https://dependency.test/")).Throws<CircuitOpenException>();
            await Assert.That(inner.Calls).IsEqualTo(1);
        }
        else
        {
            for (var index = 0; index < 2; index++)
            {
                using var response = await client.GetAsync("https://dependency.test/");
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            }
            await Assert.That(inner.Calls).IsEqualTo(2);
        }
        await Assert.That(predicateCalls).IsEqualTo(exception ? 1 : 2);
    }

    [Test]
    public async Task Standard_Hedge_Registration_Preserves_Shared_Budget()
    {
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromHours(1));
        var inner = new ResponseHandler();
        var services = new ServiceCollection();
        services.AddHttpClient("hedge")
            .ConfigurePrimaryHttpMessageHandler(() => inner)
            .AddStandardHedgeShield(options =>
            {
                options.TotalTimeout.Timeout = Timeout.InfiniteTimeSpan;
                options.AttemptTimeout.Timeout = Timeout.InfiniteTimeSpan;
                options.Hedge.Budget = budget;
                options.Hedge.MaxHedgedAttempts = 2;
                options.Hedge.Delay = TimeSpan.Zero;
                options.CircuitBreaker.MinimumThroughput = 1000;
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("hedge");
        for (var index = 0; index < 2; index++)
        {
            using var response = await client.GetAsync("https://dependency.test/");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
        await Assert.That(inner.Calls).IsEqualTo(3);
        await Assert.That(budget.Tokens).IsEqualTo(0);
    }

    private static void ConfigureStandard(StandardHttpShieldOptions options)
    {
        options.TotalTimeout.Timeout = Timeout.InfiniteTimeSpan;
        options.AttemptTimeout.Timeout = Timeout.InfiniteTimeSpan;
        options.UseRetryAfterHeader = false;
        options.Retry.MaxRetries = 2;
        options.Retry.Backoff = Backoff.None;
        options.CircuitBreaker.MinimumThroughput = 1000;
    }

    private static object CreateNonDefaultValue(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return "copied-strategy";
        if (type == typeof(int)) return 37;
        if (type == typeof(double)) return 0.73;
        if (type == typeof(bool)) return true;
        if (type == typeof(TimeSpan)) return TimeSpan.FromSeconds(73);
        if (type == typeof(Backoff)) return Backoff.None;
        if (type == typeof(RetryBudget)) return new RetryBudget(maxTokens: 7);
        if (type == typeof(CircuitBreakerMonitor)) return new CircuitBreakerMonitor();
        if (typeof(Delegate).IsAssignableFrom(type))
        {
            var invoke = type.GetMethod("Invoke")!;
            return Expression.Lambda(type, Expression.Default(invoke.ReturnType),
                invoke.GetParameters().Select(static parameter => Expression.Parameter(parameter.ParameterType))).Compile();
        }
        throw new InvalidOperationException($"Add a non-default copy-test value for {type}.");
    }

    private sealed class ResponseHandler(bool throwException = false, TimeSpan? retryAfter = null) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (throwException) return Task.FromException<HttpResponseMessage>(new IOException("non-transient failure"));
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (retryAfter is { } delay) response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
            return Task.FromResult(response);
        }
    }

    private sealed class ClockDecorator(TimeProvider timeProvider) : IShieldDecorator
    {
        public Shield Decorate(Shield shield, string? name) => shield.WithTimeProvider(timeProvider);
        public Shield<TResult> Decorate<TResult>(Shield<TResult> shield, string? name) => shield.WithTimeProvider(timeProvider);
    }
}
