namespace Kevlar.Extensions.Http;

/// <summary>A <see cref="DelegatingHandler"/> with safe per-attempt request replay.</summary>
/// <remarks>
/// The caller retains ownership of the original request. The handler owns request-factory results,
/// cloned requests, and nonselected responses. Non-idempotent replay requires explicit opt-in.
/// </remarks>
public sealed class ShieldDelegatingHandler : DelegatingHandler
{
    private readonly HttpShieldPipeline _pipeline;
    private readonly ReloadingHttpShieldPipeline? _reloadingPipeline;

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
        _pipeline = new HttpShieldPipeline(shield, options);
    }

    private ShieldDelegatingHandler(
        ReloadingHttpShieldPipeline reloadingPipeline,
        bool _)
    {
        _reloadingPipeline = reloadingPipeline
            ?? throw new ArgumentNullException(nameof(reloadingPipeline));
        _pipeline = null!;
    }

    internal static ShieldDelegatingHandler CreateReloading(
        ReloadingHttpShieldPipeline reloadingPipeline) =>
        new(reloadingPipeline, true);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request is null) { throw new ArgumentNullException(nameof(request)); }

        var pipeline = _reloadingPipeline?.Current ?? _pipeline;
        var execution = new RequestExecution(
            this,
            request,
            pipeline,
            cancellationToken);
        try
        {
            var response = await pipeline.Policy.ExecuteAsync(
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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reloadingPipeline?.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class RequestExecution
    {
        private readonly object _gate = new();
        private readonly ShieldDelegatingHandler _handler;
        private readonly HttpShieldPipeline _pipeline;
        private readonly HttpRequestMessage _original;
        private readonly CancellationToken _executionCancellationToken;
        private readonly List<HttpResponseMessage> _responses = [];
        private readonly Uri[]? _endpointOrder;

        private Task<HttpRequestTemplate>? _template;
        private CancellationTokenSource? _templateCancellation;
        private int _templateWaiters;
        private HttpResponseMessage? _terminalResponse;
        private CancellationToken _lastAttemptToken;
        private int _attempt;
        private bool _completed;
        private bool _hasAttemptToken;

        public RequestExecution(
            ShieldDelegatingHandler handler,
            HttpRequestMessage original,
            HttpShieldPipeline pipeline,
            CancellationToken executionCancellationToken)
        {
            _handler = handler;
            _pipeline = pipeline;
            _original = original;
            _executionCancellationToken = executionCancellationToken;
            _endpointOrder = pipeline.CreateEndpointOrder();
        }

        private ShieldHttpHandlerOptions Options => _pipeline.Options;

        private ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            if (Options.RequestFactory is null
                && (_endpointOrder is not null
                    || (_original.Content is not null
                        && Options.ContentReplayPolicy == HttpContentReplayPolicy.Buffer)))
            {
                return new ValueTask(GetTemplateAsync(cancellationToken));
            }

            return default;
        }

        public async ValueTask<HttpResponseMessage> SendAttemptAsync(CancellationToken cancellationToken)
        {
            await PrepareAsync(cancellationToken).ConfigureAwait(false);
            int attempt;
            bool isSequentialReplay;
            lock (_gate)
            {
                attempt = _attempt++;
                isSequentialReplay = _hasAttemptToken
                    && _lastAttemptToken == cancellationToken;
                _lastAttemptToken = cancellationToken;
                _hasAttemptToken = true;
            }

            if (attempt > 0 && isSequentialReplay)
            {
                DisposePriorResponses();
            }

            if (attempt > 0
                && !IsReplaySafeMethod(_original.Method)
                && !Options.AllowUnsafeMethodReplay
                && Options.RequestFactory is null)
            {
                throw new HttpRequestReplayException(
                    $"HTTP {_original.Method} is not replayed automatically. Set AllowUnsafeMethodReplay " +
                    "only when the operation is idempotent, or provide RequestFactory.");
            }

            HttpRequestMessage request;
            if (Options.RequestFactory is { } requestFactory)
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
                request = (await GetTemplateAsync(cancellationToken).ConfigureAwait(false))
                    .CreateRequest(attempt == 0 ? _original.Content : null);
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

                var endpointShield = endpoint is null ? null : _pipeline.GetEndpointShield(endpoint);
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
                    if (ReferenceEquals(request.Content, _original.Content))
                    {
                        request.Content = null;
                    }

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

        private async Task<HttpRequestTemplate> GetTemplateAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<HttpRequestTemplate>? creation = null;
            CancellationTokenSource? creationCancellation = null;
            Task<HttpRequestTemplate> template;
            lock (_gate)
            {
                if (_template is null)
                {
                    creation = new TaskCompletionSource<HttpRequestTemplate>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _template = creation.Task;
                    _templateCancellation = creationCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            _executionCancellationToken);
                }

                template = _template;
                _templateWaiters++;
            }

            if (creation is not null)
            {
                _ = PopulateTemplateAsync(creation, creationCancellation!);
            }

            try
            {
                return await AwaitTemplateAsync(template, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                _executionCancellationToken.IsCancellationRequested
                && exception.CancellationToken != _executionCancellationToken)
            {
                throw new OperationCanceledException(
                    exception.Message,
                    exception,
                    _executionCancellationToken);
            }
            finally
            {
                CancellationTokenSource? abandon = null;
                lock (_gate)
                {
                    _templateWaiters--;
#if !NETSTANDARD2_0
                    if (_templateWaiters == 0
                        && ReferenceEquals(_template, template)
                        && !template.IsCompleted)
                    {
                        _template = null;
                        abandon = _templateCancellation;
                        _templateCancellation = null;
                    }
#endif
                }

                if (abandon is not null)
                {
                    Cancel(abandon);
                }
            }
        }

        private async Task PopulateTemplateAsync(
            TaskCompletionSource<HttpRequestTemplate> creation,
            CancellationTokenSource creationCancellation)
        {
            try
            {
                var template = await HttpRequestTemplate.CreateAsync(
                    _original,
                    Options.ContentReplayPolicy,
                    Options.MaximumBufferSize,
                    creationCancellation.Token).ConfigureAwait(false);
                creation.TrySetResult(template);
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_template, creation.Task))
                    {
                        _template = null;
                        _templateCancellation = null;
                    }
                }

                creation.TrySetException(exception);
            }
            finally
            {
                creationCancellation.Dispose();
            }
        }

        private static async Task<HttpRequestTemplate> AwaitTemplateAsync(
            Task<HttpRequestTemplate> template,
            CancellationToken cancellationToken)
        {
            if (template.IsCompleted || !cancellationToken.CanBeCanceled)
            {
                return await template.ConfigureAwait(false);
            }

            var cancellation = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                cancellation);
            if (await Task.WhenAny(template, cancellation.Task).ConfigureAwait(false) != template)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await template.ConfigureAwait(false);
        }

        private void DisposePriorResponses()
        {
            List<HttpResponseMessage>? prior = null;
            lock (_gate)
            {
                if (_responses.Count > 0)
                {
                    prior = [.. _responses];
                    _responses.Clear();
                }
            }

            DisposeResponses(prior);
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

        private static void Cancel(CancellationTokenSource cancellation)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Template completion may dispose the source as the last waiter exits.
            }
        }
    }

}
