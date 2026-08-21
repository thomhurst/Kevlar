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
                await request.Content.LoadIntoBufferAsync(maximumBufferSize).ConfigureAwait(false);
                content = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
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

    public HttpRequestMessage CreateRequest()
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
                request.Dispose();
                throw new HttpRequestReplayException(
                    "Request content is not replayable with ContentReplayPolicy.NoBuffer. " +
                    "Use Buffer with a bounded MaximumBufferSize, or provide RequestFactory.");
            }

            request.Content = new ByteArrayContent(_content);
            foreach (var header in _contentHeaders)
            {
                _ = request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
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
}
