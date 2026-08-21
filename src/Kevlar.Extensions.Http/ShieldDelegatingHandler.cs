namespace Kevlar.Extensions.Http;

/// <summary>A <see cref="DelegatingHandler"/> with safe per-attempt request replay.</summary>
/// <remarks>
/// The caller retains ownership of the original request. The handler owns request-factory results,
/// cloned requests, and nonselected responses. Non-idempotent replay requires explicit opt-in.
/// </remarks>
public sealed class ShieldDelegatingHandler : DelegatingHandler
{
    private readonly object _endpointGate = new();
    private readonly Dictionary<string, Lazy<Shield<HttpResponseMessage>>> _endpointShields = [];
    private readonly Shield<HttpResponseMessage> _policy;
    private readonly ShieldHttpHandlerOptions _options;

    /// <summary>Creates the handler with safe no-buffer replay defaults.</summary>
    public ShieldDelegatingHandler(Shield<HttpResponseMessage> shield)
        : this(shield, new ShieldHttpHandlerOptions())
    {
    }

    /// <summary>Creates the handler with explicit replay and routing options.</summary>
    public ShieldDelegatingHandler(
        Shield<HttpResponseMessage> shield,
        ShieldHttpHandlerOptions options)
    {
        _policy = shield ?? throw new ArgumentNullException(nameof(shield));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ValidateOptions(options);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request is null) { throw new ArgumentNullException(nameof(request)); }

        var execution = new RequestExecution(this, request, _options, cancellationToken);
        try
        {
            await execution.PrepareAsync().ConfigureAwait(false);
            var response = await _policy.ExecuteAsync(
                execution,
                static (state, token) => state.SendAttemptAsync(token),
                cancellationToken).ConfigureAwait(false);
            execution.Complete(response);
            return response;
        }
        catch
        {
            execution.Complete(terminalResponse: null);
            throw;
        }
    }

    private Task<HttpResponseMessage> BaseSendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        base.SendAsync(request, cancellationToken);

    private Shield<HttpResponseMessage>? GetEndpointShield(Uri endpoint)
    {
        var factory = _options.Routing?.ShieldFactory;
        if (factory is null)
        {
            return null;
        }

        var authority = endpoint.GetLeftPart(UriPartial.Authority);
        Lazy<Shield<HttpResponseMessage>> creation;
        lock (_endpointGate)
        {
            if (!_endpointShields.TryGetValue(authority, out creation!))
            {
                creation = new Lazy<Shield<HttpResponseMessage>>(
                    () => factory(endpoint)
                        ?? throw new InvalidOperationException("The endpoint shield factory returned null."),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _endpointShields.Add(authority, creation);
            }
        }

        try
        {
            return creation.Value;
        }
        catch
        {
            lock (_endpointGate)
            {
                if (_endpointShields.TryGetValue(authority, out var current)
                    && ReferenceEquals(current, creation))
                {
                    _endpointShields.Remove(authority);
                }
            }

            throw;
        }
    }

    private static void ValidateOptions(ShieldHttpHandlerOptions options)
    {
        if (!Enum.IsDefined(typeof(HttpContentReplayPolicy), options.ContentReplayPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ContentReplayPolicy is invalid.");
        }

        if (options.MaximumBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumBufferSize must be positive.");
        }

        if (options.Routing is not { } routing)
        {
            return;
        }

        if (!Enum.IsDefined(typeof(HttpEndpointSelectionMode), routing.SelectionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The endpoint selection mode is invalid.");
        }

        if (routing.Endpoints.Count == 0)
        {
            throw new ArgumentException("Routing requires at least one endpoint.", nameof(options));
        }

        if (routing.Endpoints.Any(static endpoint => endpoint is null))
        {
            throw new ArgumentException("Routing endpoints cannot contain null.", nameof(options));
        }
    }

    private sealed class RequestExecution
    {
        private readonly object _gate = new();
        private readonly ShieldDelegatingHandler _handler;
        private readonly HttpRequestMessage _original;
        private readonly ShieldHttpHandlerOptions _options;
        private readonly CancellationToken _callerToken;
        private readonly List<HttpResponseMessage> _responses = [];
        private readonly Uri[]? _endpointOrder;

        private Task<HttpRequestTemplate>? _template;
        private HttpResponseMessage? _terminalResponse;
        private int _attempt;
        private bool _completed;

        public RequestExecution(
            ShieldDelegatingHandler handler,
            HttpRequestMessage original,
            ShieldHttpHandlerOptions options,
            CancellationToken callerToken)
        {
            _handler = handler;
            _original = original;
            _options = options;
            _callerToken = callerToken;
            _endpointOrder = CreateEndpointOrder(options.Routing);
        }

        public ValueTask PrepareAsync()
        {
            if (_options.RequestFactory is null
                && (_endpointOrder is not null
                    || (_original.Content is not null
                        && _options.ContentReplayPolicy == HttpContentReplayPolicy.Buffer)))
            {
                return new ValueTask(GetTemplateAsync());
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask<HttpResponseMessage> SendAttemptAsync(CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempt) - 1;
            if (attempt > 0
                && !IsReplaySafeMethod(_original.Method)
                && !_options.AllowUnsafeMethodReplay
                && _options.RequestFactory is null)
            {
                throw new HttpRequestReplayException(
                    $"HTTP {_original.Method} is not replayed automatically. Set AllowUnsafeMethodReplay " +
                    "only when the operation is idempotent, or provide RequestFactory.");
            }

            HttpRequestMessage request;
            if (_options.RequestFactory is { } requestFactory)
            {
                request = await requestFactory(_original, attempt, cancellationToken).ConfigureAwait(false)
                    ?? throw new HttpRequestReplayException("RequestFactory returned null.");
            }
            else if (attempt == 0 && _endpointOrder is null)
            {
                request = _original;
            }
            else
            {
                request = (await GetTemplateAsync().ConfigureAwait(false)).CreateRequest();
            }

            var ownsRequest = !ReferenceEquals(request, _original);
            try
            {
                Uri? endpoint = null;
                if (_endpointOrder is not null)
                {
                    endpoint = _endpointOrder[attempt % _endpointOrder.Length];
                    RouteToAuthority(request, endpoint);
                }

                var endpointShield = endpoint is null ? null : _handler.GetEndpointShield(endpoint);
                var response = endpointShield is null
                    ? await _handler.BaseSendAsync(request, cancellationToken).ConfigureAwait(false)
                    : await endpointShield.ExecuteAsync(
                        (Handler: _handler, Request: request),
                        static (state, token) => new ValueTask<HttpResponseMessage>(
                            state.Handler.BaseSendAsync(state.Request, token)),
                        cancellationToken).ConfigureAwait(false);
                RegisterResponse(response);
                return response;
            }
            finally
            {
                if (ownsRequest)
                {
                    request.Dispose();
                }
            }
        }

        public void Complete(HttpResponseMessage? terminalResponse)
        {
            List<HttpResponseMessage>? discarded = null;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                _terminalResponse = terminalResponse;
                foreach (var response in _responses)
                {
                    if (!ReferenceEquals(response, terminalResponse))
                    {
                        (discarded ??= []).Add(response);
                    }
                }

                _responses.Clear();
            }

            DisposeResponses(discarded);
        }

        private Task<HttpRequestTemplate> GetTemplateAsync()
        {
            lock (_gate)
            {
                return _template ??= HttpRequestTemplate.CreateAsync(
                    _original,
                    _options.ContentReplayPolicy,
                    _options.MaximumBufferSize,
                    _callerToken).AsTask();
            }
        }

        private void RegisterResponse(HttpResponseMessage response)
        {
            var dispose = false;
            lock (_gate)
            {
                if (_completed)
                {
                    dispose = !ReferenceEquals(response, _terminalResponse);
                }
                else
                {
                    _responses.Add(response);
                }
            }

            if (dispose)
            {
                response.Dispose();
            }
        }

        private static Uri[]? CreateEndpointOrder(HttpEndpointRoutingOptions? routing)
        {
            if (routing is null)
            {
                return null;
            }

            if (routing.SelectionMode == HttpEndpointSelectionMode.Ordered)
            {
                return routing.Endpoints.Select(static endpoint => endpoint.Uri).ToArray();
            }

            var random = new DeterministicRandom(routing.Seed);
            return routing.Endpoints
                .Select(endpoint => (
                    endpoint.Uri,
                    Priority: -Math.Log(random.NextExclusiveDouble()) / endpoint.Weight))
                .OrderBy(static endpoint => endpoint.Priority)
                .Select(static endpoint => endpoint.Uri)
                .ToArray();
        }

        private static void RouteToAuthority(HttpRequestMessage request, Uri endpoint)
        {
            if (request.RequestUri is not { IsAbsoluteUri: true } requestUri)
            {
                throw new HttpRequestReplayException("Endpoint routing requires an absolute request URI.");
            }

            var routed = new UriBuilder(requestUri)
            {
                Scheme = endpoint.Scheme,
                Host = endpoint.Host,
                Port = endpoint.Port,
            };
            request.RequestUri = routed.Uri;
        }

        private static bool IsReplaySafeMethod(HttpMethod method) =>
            method.Method is "GET" or "HEAD" or "OPTIONS" or "TRACE" or "PUT" or "DELETE";

        private static void DisposeResponses(List<HttpResponseMessage>? responses)
        {
            if (responses is null)
            {
                return;
            }

            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private struct DeterministicRandom(int seed)
    {
        private uint _state = unchecked((uint)seed) + 0x9E3779B9u;

        public double NextExclusiveDouble()
        {
            var value = NextUInt32();
            return (value + 1d) / (uint.MaxValue + 2d);
        }

        private uint NextUInt32()
        {
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value == 0 ? 0xA341316Cu : value;
            return _state;
        }
    }
}
