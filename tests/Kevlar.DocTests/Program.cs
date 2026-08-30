using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Kevlar;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Kevlar.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kevlar.DocTests;

internal static class Program
{
    public static Task Main() => SnippetCatalog.RunAsync();
}

internal abstract class SnippetContext
{
    protected static readonly CancellationToken cancellationToken = default;
    protected static readonly CancellationToken ct = default;
    protected static readonly int id = 1;
    protected static readonly string url = "https://example.test";
    protected static readonly object message = new();
    protected static readonly object cached = new();
    protected static readonly Shield shield = Shield.Empty;
    protected static readonly Shield timeoutShield = Shield.Timeout(TimeSpan.FromSeconds(1));
    protected static readonly Shield retryShield = Shield.Retry(1, Backoff.None);
    protected static readonly Shield breakerShield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(1));
    protected static readonly TimeSpan breakDuration = TimeSpan.FromSeconds(1);
    protected static readonly ILogger logger = NullLogger.Instance;
    protected static readonly StubCache cache = new();
    protected static readonly StubPublisher deadLetter = new();
    protected static readonly StubBus bus = new();
    protected static readonly StubMetrics metrics = new();
    protected static readonly StubAudit audit = new();
    protected static readonly StubClient client = new();
    protected static readonly StubViewModel viewModel = new();
    protected static readonly HttpClient httpClient = new(new StubHttpHandler());
    protected static readonly HttpClient http = httpClient;
    protected static readonly GrpcChannel channel = GrpcChannel.ForAddress("https://grpc.example.test");
    protected static readonly IServiceCollection services = new ServiceCollection();
    protected static readonly IKevlarRegistry registry = null!;
    protected static readonly StubBuilder builder = new();
    protected static readonly KevlarContext context = null!;
    private static readonly RateLimiter _tenantLimiter = new ConcurrencyLimiter(
        new ConcurrencyLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 0,
        });

    protected static ValueTask<User> LoadUserAsync(int _, CancellationToken __) => new(new User());

    protected static ValueTask<User> LoadAsync(CancellationToken _) => new(new User());

    protected static ValueTask<User> FetchAsync(CancellationToken _) => new(new User());

    protected static ValueTask SaveAsync(CancellationToken _) => ValueTask.CompletedTask;

    protected static ValueTask CallAsync(CancellationToken _) => ValueTask.CompletedTask;

    protected static int ComputeSync(CancellationToken _) => 42;

    protected static ValueTask<string> FlakyAsync(CancellationToken _) => new("ok");

    protected static ValueTask<object> GetReposAsync(CancellationToken _) => new(new object());

    protected static ValueTask<User> GetUserAsync(CancellationToken _) => new(new User());

    protected static ValueTask<int> SucceedAsync(CancellationToken _) => new(42);

    protected static ValueTask<RateLimitLease> AcquireTenantLeaseAsync(
        int permitCount,
        CancellationToken cancellationToken) =>
        _tenantLimiter.AcquireAsync(permitCount, cancellationToken);

    protected static HttpResponseMessage CachedResponse() => new(HttpStatusCode.OK);
}

internal sealed class User;

internal sealed class Config
{
    public static Config Default { get; } = new();
}

internal sealed class Quote
{
    public static Quote Unavailable { get; } = new();
}

internal sealed class Profile;

internal sealed class Report;

internal sealed class TenantResult;

internal sealed class MessagingException : Exception;

internal sealed class StubCache
{
    public ValueTask<HttpResponseMessage> GetCachedResultsAsync(CancellationToken _) =>
        new(new HttpResponseMessage(HttpStatusCode.OK));

    public Config Get() => Config.Default;

    public User GetUser() => new();
}

internal sealed class StubPublisher
{
    public ValueTask PublishAsync(Exception _, CancellationToken __) => ValueTask.CompletedTask;
}

internal sealed class StubBus
{
    public ValueTask PublishAsync(object _, CancellationToken __) => ValueTask.CompletedTask;
}

internal sealed class StubMetrics
{
    public void Record(CircuitState _) { }

    public void Increment(string _) { }
}

internal sealed class StubAudit
{
    public ValueTask RecordFallbackAsync<T>(Outcome<T> _, CancellationToken __) =>
        ValueTask.CompletedTask;
}

internal sealed class StubViewModel
{
    public Task RefreshAsync(CancellationToken _) => Task.CompletedTask;

    public Task ShowRetryAsync(int _, CancellationToken __) => Task.CompletedTask;
}

internal sealed class StubClient
{
    public ValueTask<User> GetUserAsync(int _, CancellationToken __) => new(new User());

    public Task<HttpResponseMessage> GetAsync(string _, CancellationToken __ = default) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
}

internal sealed class StubBuilder
{
    public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
}

internal sealed class StubHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
}

internal sealed class ReportsClient
{
    public ReportsClient(HttpClient _, Shield? __ = null) { }
}

internal sealed class ProfileClient
{
    public ProfileClient(Shield<Profile?>? _ = null) { }
}

internal class RetryOnceStrategy(HandlingClause handling) : Strategy
{
    protected override HandlingClause? Handling => handling;

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var strategyIndex = context.StrategyIndex;
        var outcome = await next.InvokeAsync(context);
        return handling.ShouldHandle(in outcome, context, 0, strategyIndex)
            ? await next.InvokeAsync(context)
            : outcome;
    }
}

internal sealed class LibraryRetryStrategy(HandlingClause handling)
    : RetryOnceStrategy(handling);

internal static class Assert
{
    public static Task<TException> ThrowsAsync<TException>(Func<Task> _)
        where TException : Exception => Task.FromResult((TException)null!);
}
