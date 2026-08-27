namespace Kevlar.Extensions.Http;

internal sealed class HttpRequestTemplate
{
    private readonly HttpMethod _method;
    private readonly Uri? _requestUri;
    private readonly Version _version;
    private readonly List<KeyValuePair<string, IEnumerable<string>>> _headers;
    private readonly byte[]? _content;
    private readonly HttpContent? _reusableContent;
    private readonly List<KeyValuePair<string, IEnumerable<string>>>? _contentHeaders;
#if NETSTANDARD2_0
    private readonly List<KeyValuePair<string, object?>> _properties;
#else
    private readonly List<KeyValuePair<string, object?>> _options;
    private readonly HttpVersionPolicy _versionPolicy;
#endif

    private HttpRequestTemplate(
        HttpRequestMessage request,
        byte[]? content,
        HttpContent? reusableContent,
        bool includeContent)
    {
        _method = request.Method;
        _requestUri = request.RequestUri;
        _version = request.Version;
        _headers = request.Headers.Select(static header =>
            new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray())).ToList();
        _content = content;
        _reusableContent = reusableContent;
        _contentHeaders = includeContent
            ? request.Content?.Headers.Select(static header =>
                new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray())).ToList()
            : null;
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
        bool canReplay,
        bool includeContent,
        CancellationToken cancellationToken)
    {
        byte[]? content = null;
        if (includeContent
            && request.Content is not null
            && policy == HttpContentReplayPolicy.Buffer)
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
                    "Increase MaxBufferSize or provide RequestFactory.",
                    exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (content.LongLength > maximumBufferSize)
            {
                throw TooLarge(maximumBufferSize);
            }
        }

        HttpContent? reusableContent = null;
        if (policy == HttpContentReplayPolicy.NoBuffer
            && canReplay
            && includeContent
            && request.Content is { } requestContent
            && (IsInherentlyReplayable(requestContent)
                || await IsAlreadyBufferedAsync(requestContent).ConfigureAwait(false)))
        {
            reusableContent = requestContent;
        }

        return new HttpRequestTemplate(request, content, reusableContent, includeContent);
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
                var replayContent = firstAttemptContent ?? _reusableContent;
                if (replayContent is null)
                {
                    request.Dispose();
                    throw new HttpRequestReplayException(
                        "Request content is not replayable with ContentReplayPolicy.NoBuffer. " +
                        "Use Buffer with a bounded MaxBufferSize, or provide RequestFactory.");
                }

                request.Content = new ReplayableContent(replayContent, _contentHeaders);
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
        "Increase MaxBufferSize or provide RequestFactory.");

    internal static bool IsInherentlyReplayable(HttpContent content)
    {
        var contentType = content.GetType();
        return contentType == typeof(ByteArrayContent)
            || contentType == typeof(StringContent)
            || contentType == typeof(FormUrlEncodedContent)
            || IsReplayableJsonContent(content, contentType);
    }

#if NET8_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "JsonContent's active serializer requires its public ObjectType metadata; missing metadata is treated as non-replayable.")]
#endif
    private static bool IsReplayableJsonContent(HttpContent content, Type contentType)
    {
        if (contentType.FullName != "System.Net.Http.Json.JsonContent")
        {
            return false;
        }

        var objectType = contentType.GetProperty("ObjectType")?.GetValue(content) as Type;
        return objectType is not null && !IsAsyncEnumerable(objectType);
    }

#if NET8_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "The active JSON serializer preserves implemented interfaces needed to serialize the declared ObjectType.")]
#endif
    private static bool IsAsyncEnumerable(Type type)
    {
        if (type.IsGenericType
            && type.GetGenericTypeDefinition().FullName
                == "System.Collections.Generic.IAsyncEnumerable`1")
        {
            return true;
        }

        foreach (var implemented in type.GetInterfaces())
        {
            if (implemented.IsGenericType
                && implemented.GetGenericTypeDefinition().FullName
                    == "System.Collections.Generic.IAsyncEnumerable`1")
            {
                return true;
            }
        }

        return false;
    }

    internal static async ValueTask<bool> IsAlreadyBufferedAsync(HttpContent content)
    {
        long? declaredLength;
        try
        {
            declaredLength = content.Headers.ContentLength;
        }
        catch (Exception)
        {
            return false;
        }

        if (declaredLength is not { } contentLength)
        {
            return false;
        }

        if (contentLength == 0)
        {
            return false;
        }

        try
        {
            // HttpContent returns immediately when its buffer already exists. Otherwise the known,
            // positive length exceeds this zero-byte probe before serialization begins. A declared
            // zero is not probed because a false header could let serialization consume source bytes.
            await content.LoadIntoBufferAsync(0).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private sealed class ReplayableContent : HttpContent
    {
        private readonly HttpContent _content;

        public ReplayableContent(
            HttpContent content,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            _content = content;
            foreach (var header in headers)
            {
                _ = Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context) =>
            _content.CopyToAsync(stream);

#if NET8_0_OR_GREATER
        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken) =>
            _content.CopyToAsync(stream, cancellationToken);
#endif

        protected override bool TryComputeLength(out long length)
        {
            if (_content.Headers.ContentLength is { } contentLength)
            {
                length = contentLength;
                return true;
            }

            length = 0;
            return false;
        }
    }

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
