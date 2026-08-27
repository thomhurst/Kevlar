using Kevlar.Internal;

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
    private readonly bool _ownsReloadingPipeline;
    private readonly Func<HttpRequestMessage, ValueTask<Shield<HttpResponseMessage>>>? _shieldSelector;
    private readonly Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>>? _requestShieldDecorator;

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

    internal ShieldDelegatingHandler(
        Shield<HttpResponseMessage> shield,
        ShieldHttpHandlerOptions options,
        Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>> requestShieldDecorator)
        : this(shield, options)
    {
        _requestShieldDecorator = requestShieldDecorator
            ?? throw new ArgumentNullException(nameof(requestShieldDecorator));
    }

    internal ShieldDelegatingHandler(
        HttpShieldPipeline pipeline,
        Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>> requestShieldDecorator)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _requestShieldDecorator = requestShieldDecorator
            ?? throw new ArgumentNullException(nameof(requestShieldDecorator));
    }

    /// <summary>Creates the handler with a shield selected once for each request.</summary>
    public ShieldDelegatingHandler(
        Func<HttpRequestMessage, Shield<HttpResponseMessage>> shieldSelector)
        : this(shieldSelector, new ShieldHttpHandlerOptions())
    {
    }

    /// <summary>Creates the handler with per-request shield selection and explicit replay options.</summary>
    public ShieldDelegatingHandler(
        Func<HttpRequestMessage, Shield<HttpResponseMessage>> shieldSelector,
        ShieldHttpHandlerOptions options)
        : this(WrapSelector(shieldSelector), options)
    {
    }

    internal ShieldDelegatingHandler(
        Func<HttpRequestMessage, Shield<HttpResponseMessage>> shieldSelector,
        ShieldHttpHandlerOptions options,
        Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>> requestShieldDecorator)
        : this(WrapSelector(shieldSelector), options, requestShieldDecorator)
    {
    }

    internal ShieldDelegatingHandler(
        Func<HttpRequestMessage, ValueTask<Shield<HttpResponseMessage>>> shieldSelector,
        ShieldHttpHandlerOptions options,
        Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>>? requestShieldDecorator = null)
    {
        _shieldSelector = shieldSelector
            ?? throw new ArgumentNullException(nameof(shieldSelector));
        _requestShieldDecorator = requestShieldDecorator;
        _pipeline = new HttpShieldPipeline(Shield<HttpResponseMessage>.Empty, options);
    }

    private ShieldDelegatingHandler(
        ReloadingHttpShieldPipeline reloadingPipeline,
        Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>> requestShieldDecorator,
        bool ownsReloadingPipeline)
    {
        _reloadingPipeline = reloadingPipeline
            ?? throw new ArgumentNullException(nameof(reloadingPipeline));
        _requestShieldDecorator = requestShieldDecorator
            ?? throw new ArgumentNullException(nameof(requestShieldDecorator));
        _ownsReloadingPipeline = ownsReloadingPipeline;
        _pipeline = null!;
    }

    internal static ShieldDelegatingHandler CreateReloading(
        ReloadingHttpShieldPipeline reloadingPipeline,
        Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>> requestShieldDecorator,
        bool ownsReloadingPipeline = true) =>
        new(reloadingPipeline, requestShieldDecorator, ownsReloadingPipeline);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request is null) { throw new ArgumentNullException(nameof(request)); }

        cancellationToken.ThrowIfCancellationRequested();
        _ = KevlarHttp.TryGetRequestOptions(request, out var requestOptions);
        requestOptions?.CancellationToken.ThrowIfCancellationRequested();
        var pipeline = _reloadingPipeline?.Current ?? _pipeline;
        var selectedShield = requestOptions?.Shield;
        if (selectedShield is not null && _requestShieldDecorator is not null)
        {
            selectedShield = _requestShieldDecorator(selectedShield);
        }

        if (selectedShield is null && _shieldSelector is not null)
        {
            using (var selectionCancellation = CreateLinkedCancellation(
                cancellationToken,
                requestOptions?.CancellationToken ?? default))
            {
                var selectionCancellationToken = selectionCancellation?.Token ?? cancellationToken;
                selectedShield = await AwaitWithCancellationAsync(
                    _shieldSelector(request),
                    selectionCancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The HTTP shield selector returned null.");
            }

            if (requestOptions is null)
            {
                _ = KevlarHttp.TryGetRequestOptions(request, out requestOptions);
            }
        }

        selectedShield ??= pipeline.Policy;
        using var linkedCancellation = CreateLinkedCancellation(
            cancellationToken,
            requestOptions?.CancellationToken ?? default);
        var executionCancellationToken = linkedCancellation?.Token ?? cancellationToken;
        executionCancellationToken.ThrowIfCancellationRequested();
        var replay = await GetReplayDecisionAsync(
            request,
            pipeline.Options,
            requestOptions).ConfigureAwait(false);
        var execution = new RequestExecution(
            this,
            request,
            pipeline,
            requestOptions,
            replay.CanReplay,
            replay.SuppressionReason,
            reportSuppression: !replay.CanReplay && !selectedShield.InvokesContinuationAtMostOnce,
            executionCancellationToken: executionCancellationToken);
        try
        {
            var response = await selectedShield.ExecuteWithContextAsync(
                execution,
                static (state, properties) => state.InitializeProperties(properties),
                static (state, context) => state.SendAttemptAsync(context),
                executionCancellationToken).ConfigureAwait(false);
            execution.Complete(response);
            return response;
        }
        catch
        {
            execution.Complete(terminalResponse: null);
            throw;
        }
    }

    private static async ValueTask<ReplayDecision> GetReplayDecisionAsync(
        HttpRequestMessage request,
        HttpShieldPipelineOptions options,
        KevlarRequestOptions? requestOptions)
    {
        if (requestOptions?.AllowReplay == false)
        {
            return ReplayDecision.Suppressed("replay_disabled");
        }

        if (options.RequestFactory is not null)
        {
            return ReplayDecision.Allowed;
        }

        if (!RequestExecution.IsReplaySafeMethod(request.Method)
            && requestOptions?.AllowReplay != true
            && !options.AllowUnsafeMethodReplay)
        {
            return ReplayDecision.Suppressed("unsafe_method");
        }

        if (request.Content is not { } content
            || options.ContentReplayPolicy == HttpContentReplayPolicy.Buffer
            || HttpRequestTemplate.IsInherentlyReplayable(content))
        {
            return ReplayDecision.Allowed;
        }

        return await HttpRequestTemplate.IsAlreadyBufferedAsync(content).ConfigureAwait(false)
            ? ReplayDecision.Allowed
            : ReplayDecision.Suppressed("non_replayable_content");
    }

    private readonly struct ReplayDecision
    {
        private ReplayDecision(bool canReplay, string? suppressionReason)
        {
            CanReplay = canReplay;
            SuppressionReason = suppressionReason;
        }

        public static ReplayDecision Allowed { get; } = new(true, suppressionReason: null);

        public bool CanReplay { get; }

        public string? SuppressionReason { get; }

        public static ReplayDecision Suppressed(string reason) => new(false, reason);
    }

    private static CancellationTokenSource? CreateLinkedCancellation(
        CancellationToken handlerToken,
        CancellationToken requestToken)
    {
        if (!requestToken.CanBeCanceled)
        {
            return null;
        }

        return CancellationTokenSource.CreateLinkedTokenSource(handlerToken, requestToken);
    }

    private static async ValueTask<T> AwaitWithCancellationAsync<T>(
        ValueTask<T> operation,
        CancellationToken cancellationToken)
    {
        if (operation.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            return await operation.ConfigureAwait(false);
        }

        var task = operation.AsTask();
        var cancellation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellation);
        if (await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false) != task)
        {
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await task.ConfigureAwait(false);
    }

    private static Func<HttpRequestMessage, ValueTask<Shield<HttpResponseMessage>>> WrapSelector(
        Func<HttpRequestMessage, Shield<HttpResponseMessage>> shieldSelector)
    {
        if (shieldSelector is null)
        {
            throw new ArgumentNullException(nameof(shieldSelector));
        }

        return request => new ValueTask<Shield<HttpResponseMessage>>(shieldSelector(request));
    }

    private Task<HttpResponseMessage> BaseSendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        base.SendAsync(request, cancellationToken);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsReloadingPipeline)
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
        private readonly KevlarRequestOptions? _requestOptions;
        private readonly bool _canReplay;
        private readonly string? _suppressionReason;
        private readonly bool _reportSuppression;
        private readonly bool _hadInitialContent;
        private readonly CancellationToken _executionCancellationToken;
        private readonly List<HttpResponseMessage> _responses = [];
        private readonly Uri[]? _endpointOrder;

        private Task<HttpRequestTemplate>? _template;
        private Task<HttpResponseMessage>? _singleAttempt;
        private Task<HttpResponseMessage>? _singleTransportAttempt;
        private CancellationTokenSource? _templateCancellation;
        private HttpResponseMessage? _terminalResponse;
        private CancellationToken _lastAttemptToken;
        private int _attempt;
        private int _suppressionReported;
        private bool _completed;
        private bool _hasAttemptToken;

        public RequestExecution(
            ShieldDelegatingHandler handler,
            HttpRequestMessage original,
            HttpShieldPipeline pipeline,
            KevlarRequestOptions? requestOptions,
            bool canReplay,
            string? suppressionReason,
            bool reportSuppression,
            CancellationToken executionCancellationToken)
        {
            _handler = handler;
            _pipeline = pipeline;
            _original = original;
            _requestOptions = requestOptions;
            _canReplay = canReplay;
            _suppressionReason = suppressionReason;
            _reportSuppression = reportSuppression;
            _hadInitialContent = original.Content is not null;
            _executionCancellationToken = executionCancellationToken;
            _endpointOrder = pipeline.CreateEndpointOrder(original.RequestUri);
        }

        private HttpShieldPipelineOptions Options => _pipeline.Options;

        public void InitializeProperties(KevlarProperties properties)
        {
            _requestOptions?.ConfigureProperties?.Invoke(properties);
            properties.Set(KevlarHttpKeys.RequestMethod, _original.Method.Method);
            if (_original.RequestUri is { } requestUri)
            {
                properties.Set(KevlarHttpKeys.RequestUri, WithoutQueryOrFragment(requestUri));
            }
            if (!_canReplay)
            {
                properties.SuppressAdditionalAttempts = true;
            }
        }

        private static string WithoutQueryOrFragment(Uri uri)
        {
            if (uri.IsAbsoluteUri)
            {
                return uri.GetComponents(
                    UriComponents.SchemeAndServer | UriComponents.Path,
                    UriFormat.UriEscaped);
            }

            var value = uri.OriginalString;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] is '?' or '#')
                {
                    return value[..index];
                }
            }

            return value;
        }

        private ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            if (Options.RequestFactory is null
                && (_endpointOrder is not null
                    || (_hadInitialContent && _canReplay)))
            {
                return new ValueTask(GetTemplateAsync(cancellationToken));
            }

            return default;
        }

        public ValueTask<HttpResponseMessage> SendAttemptAsync(KevlarContext context)
        {
            if (_reportSuppression
                && Interlocked.Exchange(ref _suppressionReported, 1) == 0)
            {
                KevlarMetrics.HttpReplaySuppressed(context, _suppressionReason!);
            }

            var cancellationToken = context.CancellationToken;
            if (_canReplay)
            {
                return SendAttemptCoreAsync(cancellationToken);
            }

            Task<HttpResponseMessage> singleAttempt;
            lock (_gate)
            {
                singleAttempt = _singleAttempt ??= SendAttemptCoreAsync(cancellationToken).AsTask();
            }

            return new ValueTask<HttpResponseMessage>(singleAttempt);
        }

        private async ValueTask<HttpResponseMessage> SendAttemptCoreAsync(
            CancellationToken cancellationToken)
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

                var endpointShield = endpoint is null
                    ? null
                    : _pipeline.GetEndpointShield(endpoint, _handler._requestShieldDecorator);
                var response = endpointShield is null
                    ? await SendTransportAsync(request, cancellationToken).ConfigureAwait(false)
                    : await endpointShield.ExecuteWithContextAsync(
                        (Execution: this, Request: request),
                        static (state, properties) => state.Execution.InitializeProperties(properties),
                        static (state, context) => state.Execution.SendTransportAsync(
                            state.Request,
                            context.CancellationToken),
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

        private ValueTask<HttpResponseMessage> SendTransportAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_canReplay)
            {
                return new ValueTask<HttpResponseMessage>(
                    _handler.BaseSendAsync(request, cancellationToken));
            }

            Task<HttpResponseMessage> singleTransportAttempt;
            lock (_gate)
            {
                singleTransportAttempt = _singleTransportAttempt ??=
                    _handler.BaseSendAsync(request, cancellationToken);
            }

            return new ValueTask<HttpResponseMessage>(singleTransportAttempt);
        }

        public void Complete(HttpResponseMessage? terminalResponse)
        {
            List<HttpResponseMessage>? discarded = null;
            CancellationTokenSource? templateCancellation = null;
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
                if (_template is { IsCompleted: false })
                {
                    templateCancellation = _templateCancellation;
                }
            }

            if (templateCancellation is not null)
            {
                Cancel(templateCancellation);
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
                    Options.MaxBufferSize,
                    _canReplay,
                    _hadInitialContent,
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

        internal static bool IsReplaySafeMethod(HttpMethod method) =>
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
                // Template completion may dispose the source concurrently.
            }
        }
    }

}
