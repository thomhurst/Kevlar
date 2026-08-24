namespace Kevlar.Extensions.Http;

internal sealed class HttpRequestTemplate
{
    private readonly HttpMethod _method;
    private readonly Uri? _requestUri;
    private readonly Version _version;
    private readonly List<KeyValuePair<string, IEnumerable<string>>> _headers;
    private readonly byte[]? _content;
    private readonly List<KeyValuePair<string, IEnumerable<string>>>? _contentHeaders;
#if NETSTANDARD2_0
    private readonly List<KeyValuePair<string, object?>> _properties;
#else
    private readonly List<KeyValuePair<string, object?>> _options;
    private readonly HttpVersionPolicy _versionPolicy;
#endif

    private HttpRequestTemplate(HttpRequestMessage request, byte[]? content)
    {
        _method = request.Method;
        _requestUri = request.RequestUri;
        _version = request.Version;
        _headers = request.Headers.Select(static header =>
            new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value)).ToList();
        _content = content;
        _contentHeaders = request.Content?.Headers.Select(static header =>
            new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value)).ToList();
#if NETSTANDARD2_0
        _properties = request.Properties.ToList();
#else
        _versionPolicy = request.VersionPolicy;
        _options = request.Options.Select(static option =>
            new KeyValuePair<string, object?>(option.Key, option.Value)).ToList();
#endif
    }

    public static async ValueTask<HttpRequestTemplate> CreateAsync(
        HttpRequestMessage request,
        HttpContentReplayPolicy policy,
        long maximumBufferSize,
        CancellationToken cancellationToken)
    {
        byte[]? content = null;
        if (request.Content is not null && policy == HttpContentReplayPolicy.Buffer)
        {
            if (request.Content.Headers.ContentLength is { } length && length > maximumBufferSize)
            {
                throw TooLarge(maximumBufferSize);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
#if NET9_0_OR_GREATER
                await request.Content.LoadIntoBufferAsync(maximumBufferSize, cancellationToken).ConfigureAwait(false);
#else
                await AwaitWithCancellationAsync(
                    request.Content.LoadIntoBufferAsync(maximumBufferSize),
                    cancellationToken).ConfigureAwait(false);
#endif
#if NETSTANDARD2_0
                content = await AwaitWithCancellationAsync(
                    request.Content.ReadAsByteArrayAsync(),
                    cancellationToken).ConfigureAwait(false);
#else
                content = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#endif
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new HttpRequestReplayException(
                    $"Request content could not be buffered safely within the {maximumBufferSize}-byte limit. " +
                    "Increase MaximumBufferSize or provide RequestFactory.",
                    exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (content.LongLength > maximumBufferSize)
            {
                throw TooLarge(maximumBufferSize);
            }
        }

        return new HttpRequestTemplate(request, content);
    }

    public HttpRequestMessage CreateRequest(HttpContent? firstAttemptContent = null)
    {
        var request = new HttpRequestMessage(_method, _requestUri)
        {
            Version = _version,
        };
#if !NETSTANDARD2_0
        request.VersionPolicy = _versionPolicy;
#endif
        foreach (var header in _headers)
        {
            _ = request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (_contentHeaders is not null)
        {
            if (_content is null)
            {
                if (firstAttemptContent is null)
                {
                    request.Dispose();
                    throw new HttpRequestReplayException(
                        "Request content is not replayable with ContentReplayPolicy.NoBuffer. " +
                        "Use Buffer with a bounded MaximumBufferSize, or provide RequestFactory.");
                }

                request.Content = firstAttemptContent;
            }
            else
            {
                request.Content = new ByteArrayContent(_content);
                foreach (var header in _contentHeaders)
                {
                    _ = request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

#if NETSTANDARD2_0
        foreach (var property in _properties)
        {
            request.Properties[property.Key] = property.Value;
        }
#else
        foreach (var option in _options)
        {
            request.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }
#endif
        return request;
    }

    private static HttpRequestReplayException TooLarge(long maximumBufferSize) => new(
        $"Request content exceeds the {maximumBufferSize}-byte replay buffer limit. " +
        "Increase MaximumBufferSize or provide RequestFactory.");

#if !NET9_0_OR_GREATER
    private static async Task<T> AwaitWithCancellationAsync<T>(
        Task<T> operation,
        CancellationToken cancellationToken)
    {
        if (operation.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            return await operation.ConfigureAwait(false);
        }

        var cancellation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellation);
        if (await Task.WhenAny(operation, cancellation.Task).ConfigureAwait(false) != operation)
        {
            ObserveFault(operation);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await operation.ConfigureAwait(false);
    }

    private static async Task AwaitWithCancellationAsync(
        Task operation,
        CancellationToken cancellationToken)
    {
        if (operation.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            await operation.ConfigureAwait(false);
            return;
        }

        var cancellation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellation);
        if (await Task.WhenAny(operation, cancellation.Task).ConfigureAwait(false) != operation)
        {
            ObserveFault(operation);
            cancellationToken.ThrowIfCancellationRequested();
        }

        await operation.ConfigureAwait(false);
    }

    private static void ObserveFault(Task operation) =>
        _ = operation.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
#endif
}
