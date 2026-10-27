using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class HttpDecorationTests
{
    [Test]
    [Arguments(Selection.Request)]
    [Arguments(Selection.Partition)]
    [Arguments(Selection.AsyncPartition)]
    [Arguments(Selection.Override)]
    public async Task Concurrent_Requests_Decorate_Each_Selected_Shield_Once(Selection selection)
    {
        var first = Shield<HttpResponseMessage>.Empty.WithName("first");
        var second = Shield<HttpResponseMessage>.Empty.WithName("second");
        Shield<HttpResponseMessage> Select(string key) => key == "first.test" ? first : second;
        using var partitions = selection == Selection.AsyncPartition
            ? PartitionedShield<string, HttpResponseMessage>.CreateAsync(async key =>
            {
                await Task.Yield();
                return Select(key);
            })
            : new PartitionedShield<string, HttpResponseMessage>(Select);
        var decorator = new RecordingDecorator();
        var services = new ServiceCollection();
        services.AddSingleton<IShieldDecorator>(decorator);
        var builder = services.AddHttpClient("api")
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler());
        switch (selection)
        {
            case Selection.Request:
                builder.AddShield((request, _) => Select(request.RequestUri!.Host));
                break;
            case Selection.Partition:
            case Selection.AsyncPartition:
                builder.AddShield(partitions, static request => request.RequestUri!.Host);
                break;
            case Selection.Override:
                builder.AddShield(Shield<HttpResponseMessage>.Empty);
                break;
        }
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            await start.Task;
            var host = index % 2 == 0 ? "first.test" : "second.test";
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/");
            if (selection == Selection.Override)
            {
                request.WithShield(Select(host));
            }
            using var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(requests);

        await Assert.That(decorator.Calls.Count(call => ReferenceEquals(call.Shield, first))).IsEqualTo(1);
        await Assert.That(decorator.Calls.Count(call => ReferenceEquals(call.Shield, second))).IsEqualTo(1);
        await Assert.That(decorator.Calls.All(call => call.Name == "api")).IsTrue();
    }

    [Test]
    public async Task Defaults_Keep_Decoration_Separate_For_Each_Client_And_Service_Provider()
    {
        var shared = Shield<HttpResponseMessage>.Empty;
        var decorator = new RecordingDecorator();
        var services = new ServiceCollection();
        services.AddSingleton<IShieldDecorator>(decorator);
        services.ConfigureHttpClientDefaults(builder => builder
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler())
            .AddShield((_, _) => shared));

        for (var instance = 0; instance < 2; instance++)
        {
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            foreach (var name in new[] { "first", "second" })
            {
                using var client = factory.CreateClient(name);
                for (var request = 0; request < 2; request++)
                {
                    using var response = await client.GetAsync("https://dependency.test/");
                }
            }
        }

        await Assert.That(decorator.Calls.Select(call => call.Name))
            .IsEquivalentTo(new string?[] { "first", "second", "first", "second" });
    }

    [Test]
    public async Task Removing_A_Partition_Decorates_Its_Replacement()
    {
        using var partitions = new PartitionedShield<string, HttpResponseMessage>(
            static key => Shield<HttpResponseMessage>.Empty.WithName(key));
        var decorator = new RecordingDecorator();
        var services = new ServiceCollection();
        services.AddSingleton<IShieldDecorator>(decorator);
        services.AddHttpClient("api")
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler())
            .AddShield(partitions, static _ => "tenant");
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
        for (var generation = 0; generation < 2; generation++)
        {
            for (var request = 0; request < 2; request++)
            {
                using var response = await client.GetAsync("https://dependency.test/");
            }
            await partitions.TryRemoveAsync("tenant");
        }

        var calls = decorator.Calls.ToArray();
        await Assert.That(calls.Length).IsEqualTo(2);
        await Assert.That(calls[0].Shield).IsNotSameReferenceAs(calls[1].Shield);
    }

    [Test]
    public async Task Reloading_Wrapper_Decorates_Each_New_Snapshot()
    {
        var snapshot = Shield<HttpResponseMessage>.Empty.WithName("before");
        var wrapper = new Shield<HttpResponseMessage>(() => snapshot);
        var calls = 0;
        var cache = new HttpShieldDecorationCache(shield =>
        {
            calls++;
            return shield.WithName(shield.Name!);
        });
        var before = cache.Decorate(wrapper);
        await Assert.That(cache.Decorate(wrapper)).IsSameReferenceAs(before);
        snapshot = snapshot.WithName("after");
        var after = cache.Decorate(wrapper);
        await Assert.That(after.Name).IsEqualTo("after");
        await Assert.That(cache.Decorate(wrapper)).IsSameReferenceAs(after);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    [NotInParallel]
    public async Task Cache_Does_Not_Retain_Unselected_Shields_Or_Their_Decorations()
    {
        // The value deliberately references its key to cover ConditionalWeakTable ephemeron semantics.
        var cache = new HttpShieldDecorationCache(static shield => new Shield<HttpResponseMessage>(() => shield));
        var (original, decorated) = CacheTransientShield(cache);
        for (var attempt = 0; attempt < 10 && (original.IsAlive || decorated.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        await Assert.That(original.IsAlive).IsFalse();
        await Assert.That(decorated.IsAlive).IsFalse();
        GC.KeepAlive(cache);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Original, WeakReference Decorated) CacheTransientShield(HttpShieldDecorationCache cache)
    {
        var shield = Shield<HttpResponseMessage>.Empty.WithName("transient");
        return (new WeakReference(shield), new WeakReference(cache.Decorate(shield)));
    }

    public enum Selection { Request, Partition, AsyncPartition, Override }

    private sealed class RecordingDecorator : IShieldDecorator
    {
        public ConcurrentQueue<(object Shield, string? Name)> Calls { get; } = new();

        public Shield Decorate(Shield shield, string? name) => throw new NotSupportedException();

        public Shield<TResult> Decorate<TResult>(Shield<TResult> shield, string? name)
        {
            Calls.Enqueue((shield, name));
            return shield.WithName(name ?? "unnamed");
        }
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
