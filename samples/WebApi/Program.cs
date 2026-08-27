using System.Diagnostics.Metrics;
using System.Net;
using Kevlar;
using OpenTelemetry.Metrics;

var attempts = 0;
var retries = 0L;
using var listener = new MeterListener
{
    InstrumentPublished = (instrument, meterListener) =>
    {
        if (instrument.Meter.Name == KevlarDiagnostics.MeterName && instrument.Name == "kevlar.retries")
        {
            meterListener.EnableMeasurementEvents(instrument);
        }
    },
};
listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref retries, value));
listener.Start();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(KevlarDiagnostics.MeterName));
builder.Services.AddShield("background-jobs", Shield.Retry(1, Backoff.None));
builder.Services.AddHttpClient("downstream")
    .ConfigurePrimaryHttpMessageHandler(() => new FlakyHandler(() => Interlocked.Increment(ref attempts)))
    .AddStandardShield(options =>
    {
        options.Retry.MaxRetries = 2;
        options.Retry.Backoff = Backoff.None;
        options.TotalTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
    });

await using var app = builder.Build();
app.MapGet("/orders", async (IHttpClientFactory clientFactory, CancellationToken cancellationToken) =>
{
    var endpointClient = clientFactory.CreateClient("downstream");
    using var endpointResponse = await endpointClient.GetAsync("https://sample.invalid/orders", cancellationToken);
    return Results.Ok(new
    {
        Status = endpointResponse.StatusCode,
        Attempts = Volatile.Read(ref attempts),
        Retries = Interlocked.Read(ref retries),
    });
});

if (!args.Contains("--smoke", StringComparer.Ordinal))
{
    await app.RunAsync();
    return;
}

var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("downstream");
using var response = await client.GetAsync("https://sample.invalid/orders");
var provider = app.Services.GetRequiredKeyedService<Kevlar.Extensions.DependencyInjection.IShieldProvider>(
    "background-jobs");

if (response.StatusCode != HttpStatusCode.OK || attempts != 3 || provider.Current is null)
{
    throw new InvalidOperationException(
        $"Expected the standard shield to recover on attempt 3; status={response.StatusCode}, attempts={attempts}.");
}

#if NET10_0_OR_GREATER
if (retries != 2)
{
    throw new InvalidOperationException($"Expected two kevlar.retries measurements; observed {retries}.");
}
#endif

Console.WriteLine($"Web API sample passed after {attempts} HTTP attempts and observed {retries} retry measurements.");

internal sealed class FlakyHandler(Func<int> nextAttempt) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var status = nextAttempt() < 3
            ? HttpStatusCode.ServiceUnavailable
            : HttpStatusCode.OK;
        return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
    }
}
