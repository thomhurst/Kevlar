using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Kevlar.IntegrationTests.Infrastructure;

/// <summary>
/// A real HTTP server on a loopback port whose behaviour is scripted per call number,
/// so tests exercise genuine sockets, cancellation and header handling.
/// </summary>
internal sealed class FlakyHttpServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Func<int, HttpListenerContext, Task> _handler;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _acceptLoop;
    private int _calls;

    private FlakyHttpServer(HttpListener listener, string url, Func<int, HttpListenerContext, Task> handler)
    {
        _listener = listener;
        _handler = handler;
        Url = url;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string Url { get; }

    /// <summary>Total requests received so far.</summary>
    public int CallCount => Volatile.Read(ref _calls);

    /// <summary>Starts a server whose handler receives the 1-based call number of each request.</summary>
    public static FlakyHttpServer Start(Func<int, HttpListenerContext, Task> handler)
    {
        for (var attempt = 0; ; attempt++)
        {
            var port = GetFreePort();
            var url = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(url);

            try
            {
                listener.Start();
                return new FlakyHttpServer(listener, url, handler);
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                // The port was taken between probing and binding; try another.
            }
        }
    }

    /// <summary>Writes a response and closes it, tolerating clients that already disconnected.</summary>
    public static async Task Respond(HttpListenerContext context, int statusCode, string body = "", string? retryAfterSeconds = null)
    {
        try
        {
            context.Response.StatusCode = statusCode;

            if (retryAfterSeconds is not null)
            {
                context.Response.AddHeader("Retry-After", retryAfterSeconds);
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or IOException)
        {
            // The client gave up (timeout / hedging cancellation). Nothing to do.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        _listener.Close();

        try
        {
            await _acceptLoop;
        }
        catch
        {
            // Shutdown races are expected.
        }

        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            var call = Interlocked.Increment(ref _calls);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _handler(call, context);
                }
                catch
                {
                    await Respond(context, 500, "handler error");
                }
            });
        }
    }

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
