using Grpc.Core;
using Grpc.Core.Interceptors;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Kevlar.Extensions.Grpc;

/// <summary>Applies explicit, replay-safe resilience boundaries to asynchronous streaming calls.</summary>
/// <remarks>
/// Server-streaming calls may retry only until response headers or the first response item become
/// observable. Client-streaming and duplex calls never replay messages, so their per-operation
/// shield must invoke its continuation at most once.
/// </remarks>
public sealed class ShieldStreamingClientInterceptor : Interceptor
{
    private readonly Shield _establishmentShield;
    private readonly Shield? _operationShield;

    /// <summary>Creates an interceptor that uses the supplied immutable shield at every safe boundary.</summary>
    public ShieldStreamingClientInterceptor(Shield shield)
    {
        _establishmentShield = shield ?? throw new ArgumentNullException(nameof(shield));
        _operationShield = shield.InvokesContinuationAtMostOnce ? shield : null;
    }

    /// <summary>
    /// Creates an interceptor with separate server-stream establishment and per-operation shields.
    /// </summary>
    /// <remarks>
    /// <paramref name="establishmentShield"/> may repeat a server-streaming call before progress.
    /// <paramref name="operationShield"/> protects individual reads and writes, and therefore must
    /// invoke each continuation at most once. Client-streaming response completion remains governed
    /// by the call lifetime so waiting for the response cannot occupy an operation concurrency slot.
    /// </remarks>
    public ShieldStreamingClientInterceptor(
        Shield establishmentShield,
        Shield operationShield)
    {
        _establishmentShield = establishmentShield
            ?? throw new ArgumentNullException(nameof(establishmentShield));
        if (operationShield is null) { throw new ArgumentNullException(nameof(operationShield)); }
        if (!operationShield.InvokesContinuationAtMostOnce)
        {
            throw new ArgumentException(
                "The operation shield cannot use retry, hedging, or another strategy that may " +
                "invoke an operation more than once.",
                nameof(operationShield));
        }

        _operationShield = operationShield;
    }

    /// <inheritdoc />
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        if (request is null) { throw new ArgumentNullException(nameof(request)); }
        if (continuation is null) { throw new ArgumentNullException(nameof(continuation)); }

        return new ServerStreamingState<TRequest, TResponse>(
            _establishmentShield,
            _operationShield,
            request,
            context,
            continuation).Start();
    }

    /// <inheritdoc />
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        if (continuation is null) { throw new ArgumentNullException(nameof(continuation)); }
        EnsureAtMostOnce("client-streaming");

        return new ClientStreamingState<TRequest, TResponse>(
            _operationShield!,
            context,
            continuation).Start();
    }

    /// <inheritdoc />
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        if (continuation is null) { throw new ArgumentNullException(nameof(continuation)); }
        EnsureAtMostOnce("duplex-streaming");

        return new DuplexStreamingState<TRequest, TResponse>(
            _operationShield!,
            context,
            continuation).Start();
    }

    private void EnsureAtMostOnce(string callShape)
    {
        if (_operationShield is null)
        {
            throw new NotSupportedException(
                $"The {callShape} interceptor cannot use retry, hedging, or another strategy that " +
                "may invoke an operation more than once because request messages are never replayed.");
        }
    }

    private sealed class ServerStreamingState<TRequest, TResponse> : IAsyncStreamReader<TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly object _gate = new();
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly Shield _establishmentShield;
        private readonly Shield? _operationShield;
        private readonly TRequest _request;
        private readonly ClientInterceptorContext<TRequest, TResponse> _context;
        private readonly AsyncServerStreamingCallContinuation<TRequest, TResponse> _continuation;
        private readonly CancellationTokenSource _lifetime;
        private readonly List<Attempt> _attempts = [];
        private ConditionalWeakTable<AttemptFailureException, FailureSelection>? _failedCalls;
        private TaskCompletionSource<Attempt> _activeAttemptReady = NewAttemptSource();
        private Exception? _deadlineFailure;

        private Attempt? _activeAttempt;
        private AsyncServerStreamingCall<TResponse>? _terminalCall;
        private AsyncServerStreamingCall<TResponse>? _selectedAttemptCall;
        private CancellationTokenSource? _terminalCancellation;
        private bool _selected;
        private bool _disposed;

        public ServerStreamingState(
            Shield establishmentShield,
            Shield? operationShield,
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            _establishmentShield = establishmentShield;
            _operationShield = operationShield;
            _request = request;
            _context = context;
            _continuation = continuation;
            _lifetime = context.Options.CancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(context.Options.CancellationToken)
                : new CancellationTokenSource();
        }

        public TResponse Current => TerminalCall().ResponseStream.Current;

        public AsyncServerStreamingCall<TResponse> Start() => new(
            this,
            static state => ((ServerStreamingState<TRequest, TResponse>)state).GetResponseHeadersAsync(),
            static state => ((ServerStreamingState<TRequest, TResponse>)state).TerminalCall().GetStatus(),
            static state => ((ServerStreamingState<TRequest, TResponse>)state).TerminalCall().GetTrailers(),
            static state => ((ServerStreamingState<TRequest, TResponse>)state).Dispose(),
            this);

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (TryGetSelected(out var selected))
                {
                    var operationToken = cancellationToken.CanBeCanceled
                        ? cancellationToken
                        : _lifetime.Token;
                    try
                    {
                        if (_operationShield is null)
                        {
                            return await AwaitGrpcOperationAsync(
                                selected.ResponseStream.MoveNext(operationToken),
                                operationToken).ConfigureAwait(false);
                        }

                        return await _operationShield.ExecuteAsync(
                            selected.ResponseStream,
                            static (reader, token) => AwaitGrpcOperationAsync(
                                reader.MoveNext(token),
                                token),
                            operationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is OperationCanceledException
                            or RpcException { StatusCode: StatusCode.Cancelled }
                        && _context.Options.CancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(
                            exception.Message,
                            exception,
                            _context.Options.CancellationToken);
                    }
                }

                using var operation = CreateOperationCancellation(cancellationToken);
                try
                {
                    var result = await _establishmentShield.ExecuteAsync(
                        this,
                        static (state, token) => state.StartAndReadAsync(token),
                        operation?.Token ?? _lifetime.Token).ConfigureAwait(false);
                    Select(result.Call, result.Call, result.Cancellation);
                    return result.HasNext;
                }
                catch (Exception exception)
                {
                    SelectFailure(exception);
                    SignalEstablishmentFailure(GetVisibleException(exception));
                    RethrowNormalized(exception);
                    throw;
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<Metadata> GetResponseHeadersAsync()
        {
            using var gateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token);
            var gateWait = _operationGate.WaitAsync(gateCancellation.Token);
            var activeAttempt = GetActiveAttemptAsync();
            await Task.WhenAny(gateWait, activeAttempt).ConfigureAwait(false);
            var observingActive = gateWait.Status != TaskStatus.RanToCompletion
                && activeAttempt.IsCompleted;
            if (observingActive)
            {
                CancelLifetime(gateCancellation);
                try
                {
                    await gateWait.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (observingActive)
                {
                    // The in-flight attempt owns establishment; observe its headers directly.
                }

                var attempt = await activeAttempt.ConfigureAwait(false);
                var headers = await attempt.Call.ResponseHeadersAsync.ConfigureAwait(false);
                Select(attempt.Call, attempt.Call, attempt.Cancellation);
                return headers;
            }

            await gateWait.ConfigureAwait(false);
            try
            {
                if (TryGetSelected(out var selected))
                {
                    return await selected.ResponseHeadersAsync.ConfigureAwait(false);
                }

                try
                {
                    var result = await _establishmentShield.ExecuteAsync(
                        this,
                        static (state, token) => state.StartAndReadHeadersAsync(token),
                        _lifetime.Token).ConfigureAwait(false);
                    Select(result.Call, result.Call, result.Cancellation);
                    return result.Headers;
                }
                catch (Exception exception)
                {
                    SelectFailure(exception);
                    SignalEstablishmentFailure(GetVisibleException(exception));
                    RethrowNormalized(exception);
                    throw;
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async ValueTask<ReadResult> StartAndReadAsync(CancellationToken cancellationToken)
        {
            ThrowIfDeadlineExpired();
            DisposeSupersededFailures();
            var attempt = CreateAttempt(cancellationToken);
            var attemptToken = attempt.Cancellation.Token;
            try
            {
                var hasNext = await attempt.Call.ResponseStream
                    .MoveNext(attemptToken).ConfigureAwait(false);
                CompleteAttempt(attempt.Call, exception: null, failureCall: null);
                return new ReadResult(attempt.Call, attempt.Cancellation, hasNext);
            }
            catch (Exception exception)
            {
                exception = NormalizeAttemptException(exception, cancellationToken);
                var deadlineExceeded = exception is ExpiredDeadlineRpcException;
                exception = await CompleteFailureAsync(attempt.Call, exception).ConfigureAwait(false);
                if (deadlineExceeded)
                {
                    Volatile.Write(ref _deadlineFailure, exception);
                }

                ExceptionDispatchInfo.Capture(exception).Throw();
                throw;
            }
        }

        private async ValueTask<HeadersResult> StartAndReadHeadersAsync(CancellationToken cancellationToken)
        {
            ThrowIfDeadlineExpired();
            DisposeSupersededFailures();
            var attempt = CreateAttempt(cancellationToken);
            try
            {
                var headers = await attempt.Call.ResponseHeadersAsync.ConfigureAwait(false);
                CompleteAttempt(attempt.Call, exception: null, failureCall: null);
                return new HeadersResult(attempt.Call, attempt.Cancellation, headers);
            }
            catch (Exception exception)
            {
                exception = NormalizeAttemptException(exception, cancellationToken);
                var deadlineExceeded = exception is ExpiredDeadlineRpcException;
                exception = await CompleteFailureAsync(attempt.Call, exception).ConfigureAwait(false);
                if (deadlineExceeded)
                {
                    Volatile.Write(ref _deadlineFailure, exception);
                }

                ExceptionDispatchInfo.Capture(exception).Throw();
                throw;
            }
        }

        private void ThrowIfDeadlineExpired()
        {
            if (_context.Options.Deadline is not { } deadline || deadline > DateTime.UtcNow)
            {
                return;
            }

            CancelLifetime(_lifetime);
            if (Volatile.Read(ref _deadlineFailure) is { } deadlineFailure)
            {
                ExceptionDispatchInfo.Capture(deadlineFailure).Throw();
            }

            throw new ExpiredDeadlineRpcException(
                new RpcException(new Status(StatusCode.DeadlineExceeded, "Deadline exceeded.")));
        }

        private Exception NormalizeAttemptException(
            Exception exception,
            CancellationToken cancellationToken)
        {
            if (exception is RpcException { StatusCode: StatusCode.Cancelled } rpcCancellation
                && cancellationToken.IsCancellationRequested
                && !IsExpiredDeadline())
            {
                return new OperationCanceledException(
                    rpcCancellation.Message,
                    rpcCancellation,
                    cancellationToken);
            }

            if (IsExpiredDeadline()
                && exception is RpcException
                {
                    StatusCode: StatusCode.DeadlineExceeded or StatusCode.Cancelled or StatusCode.Unknown
                } deadlineException)
            {
                CancelLifetime(_lifetime);
                var normalized = deadlineException.StatusCode == StatusCode.DeadlineExceeded
                    ? deadlineException
                    : new RpcException(
                        new Status(StatusCode.DeadlineExceeded, deadlineException.Status.Detail),
                        deadlineException.Trailers,
                        deadlineException.Message);
                return new ExpiredDeadlineRpcException(normalized);
            }

            if (IsExpiredDeadline()
                && exception is OperationCanceledException
                && _lifetime.IsCancellationRequested)
            {
                CancelLifetime(_lifetime);
                return new ExpiredDeadlineRpcException(
                    new RpcException(new Status(StatusCode.DeadlineExceeded, "Deadline exceeded.")));
            }

            return exception;
        }

        private bool IsExpiredDeadline() =>
            _context.Options.Deadline is { } deadline
            && deadline <= DateTime.UtcNow
            && !_context.Options.CancellationToken.IsCancellationRequested;

        private void RethrowNormalized(Exception exception)
        {
            ExceptionDispatchInfo.Capture(GetVisibleException(exception)).Throw();
        }

        private Exception GetVisibleException(Exception exception)
        {
            if (exception is AttemptFailureException attemptFailure)
            {
                exception = attemptFailure.OriginalException;
            }

            if (exception is OperationCanceledException cancellation
                && _context.Options.CancellationToken.IsCancellationRequested
                && cancellation.CancellationToken != _context.Options.CancellationToken)
            {
                return new OperationCanceledException(
                    cancellation.Message,
                    cancellation,
                    _context.Options.CancellationToken);
            }

            if (exception is ExpiredDeadlineRpcException deadlineException)
            {
                return deadlineException.Original;
            }

            return exception;
        }

        private Attempt CreateAttempt(CancellationToken cancellationToken)
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token,
                cancellationToken);
            AsyncServerStreamingCall<TResponse> call;
            try
            {
                var context = new ClientInterceptorContext<TRequest, TResponse>(
                    _context.Method,
                    _context.Host,
                    _context.Options.WithCancellationToken(cancellation.Token));
                call = _continuation(_request, context);
            }
            catch
            {
                cancellation.Dispose();
                throw;
            }

            var dispose = false;
            lock (_gate)
            {
                if (_disposed || _selected)
                {
                    dispose = true;
                }
                else
                {
                    var attempt = new Attempt(call, cancellation, Exception: null);
                    _attempts.Add(attempt);
                    _activeAttempt = attempt;
                    _activeAttemptReady.TrySetResult(attempt);
                }
            }

            if (dispose)
            {
                DisposeAttempt(call, cancellation);
                throw new ObjectDisposedException(nameof(ShieldStreamingClientInterceptor));
            }

            return new Attempt(call, cancellation, Exception: null);
        }

        private async ValueTask<Exception> CompleteFailureAsync(
            AsyncServerStreamingCall<TResponse> call,
            Exception exception)
        {
            var failureCall = await SnapshotFailureAsync(call, exception).ConfigureAwait(false);
            return CompleteAttempt(call, exception, failureCall) ?? exception;
        }

        private Exception? CompleteAttempt(
            AsyncServerStreamingCall<TResponse> call,
            Exception? exception,
            AsyncServerStreamingCall<TResponse>? failureCall)
        {
            lock (_gate)
            {
                for (var index = _attempts.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(_attempts[index].Call, call))
                    {
                        if (exception is not null)
                        {
                            var attemptFailure = new AttemptFailureException(exception);
                            _attempts[index] = new Attempt(
                                call,
                                _attempts[index].Cancellation,
                                attemptFailure);
                            (_failedCalls ??= new()).Add(
                                attemptFailure,
                                new FailureSelection(call, failureCall!));
                            ClearActiveAttempt(call);
                            return attemptFailure;
                        }

                        _attempts[index] = new Attempt(
                            call,
                            _attempts[index].Cancellation,
                            Exception: null);
                        return null;
                    }
                }
            }

            return exception;
        }

        private Task<Attempt> GetActiveAttemptAsync()
        {
            lock (_gate)
            {
                return _activeAttempt is { } attempt
                    ? Task.FromResult(attempt)
                    : _activeAttemptReady.Task;
            }
        }

        private void ClearActiveAttempt(AsyncServerStreamingCall<TResponse> call)
        {
            if (_activeAttempt is not { } active || !ReferenceEquals(active.Call, call))
            {
                return;
            }

            _activeAttempt = null;
            _activeAttemptReady = NewAttemptSource();
        }

        private void SignalEstablishmentFailure(Exception exception)
        {
            lock (_gate)
            {
                if (_activeAttemptReady.TrySetException(exception))
                {
                    _ = _activeAttemptReady.Task.Exception;
                }
            }
        }

        private static TaskCompletionSource<Attempt> NewAttemptSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async ValueTask<AsyncServerStreamingCall<TResponse>> SnapshotFailureAsync(
            AsyncServerStreamingCall<TResponse> call,
            Exception exception)
        {
            var responseHeaders = call.ResponseHeadersAsync;
            try
            {
                responseHeaders = Task.FromResult(await responseHeaders.ConfigureAwait(false));
            }
            catch (Exception headersException)
            {
                responseHeaders = Task.FromException<Metadata>(headersException);
                _ = responseHeaders.Exception;
            }

            var rpcException = exception as RpcException ?? exception.InnerException as RpcException;
            var status = rpcException?.Status ?? GetStatusOrUnknown(call, exception);
            var trailers = rpcException?.Trailers ?? GetTrailersOrEmpty(call);
            return new AsyncServerStreamingCall<TResponse>(
                call.ResponseStream,
                responseHeaders,
                () => status,
                () => trailers,
                static () => { });
        }

        private static Status GetStatusOrUnknown(
            AsyncServerStreamingCall<TResponse> call,
            Exception exception)
        {
            try
            {
                return call.GetStatus();
            }
            catch (InvalidOperationException)
            {
                return new Status(StatusCode.Unknown, exception.Message);
            }
        }

        private static Metadata GetTrailersOrEmpty(AsyncServerStreamingCall<TResponse> call)
        {
            try
            {
                return call.GetTrailers();
            }
            catch (InvalidOperationException)
            {
                return new Metadata();
            }
        }

        private void DisposeSupersededFailures()
        {
            List<Attempt>? superseded = null;
            lock (_gate)
            {
                for (var index = _attempts.Count - 1; index >= 0; index--)
                {
                    if (_attempts[index].Exception is null)
                    {
                        continue;
                    }

                    MarkFailureDisposed(_attempts[index]);
                    (superseded ??= []).Add(_attempts[index]);
                    _attempts.RemoveAt(index);
                }
            }

            DisposeAttempts(superseded);
        }

        private void SelectFailure(Exception exception)
        {
            AsyncServerStreamingCall<TResponse>? terminalCall = null;
            AsyncServerStreamingCall<TResponse>? selectedAttemptCall = null;
            CancellationTokenSource? selectedCancellation = null;
            lock (_gate)
            {
                if (exception is AttemptFailureException attemptFailure
                    && _failedCalls is not null
                    && _failedCalls.TryGetValue(attemptFailure, out var failure))
                {
                    terminalCall = failure.Call;
                    if (!failure.Disposed)
                    {
                        selectedAttemptCall = failure.AttemptCall;
                        for (var index = _attempts.Count - 1; index >= 0; index--)
                        {
                            if (ReferenceEquals(_attempts[index].Call, selectedAttemptCall))
                            {
                                selectedCancellation = _attempts[index].Cancellation;
                                break;
                            }
                        }
                    }
                }
            }

            if (terminalCall is not null)
            {
                Select(terminalCall, selectedAttemptCall, selectedCancellation);
            }
        }

        private void Select(
            AsyncServerStreamingCall<TResponse> terminalCall,
            AsyncServerStreamingCall<TResponse>? selectedAttemptCall,
            CancellationTokenSource? selectedCancellation)
        {
            List<Attempt>? discarded = null;
            var disposeSelectedLoser = false;
            lock (_gate)
            {
                if (_selected)
                {
                    disposeSelectedLoser = selectedAttemptCall is not null
                        && selectedCancellation is not null
                        && !ReferenceEquals(_selectedAttemptCall, selectedAttemptCall);
                }
                else
                {
                    _selected = true;
                    _terminalCall = terminalCall;
                    _selectedAttemptCall = selectedAttemptCall;
                    _terminalCancellation = selectedCancellation;
                    foreach (var attempt in _attempts)
                    {
                        if (!ReferenceEquals(attempt.Call, selectedAttemptCall))
                        {
                            MarkFailureDisposed(attempt);
                            (discarded ??= []).Add(attempt);
                        }
                    }

                    _attempts.Clear();
                }
            }

            if (disposeSelectedLoser)
            {
                DisposeAttempt(selectedAttemptCall!, selectedCancellation!);
            }
            DisposeAttempts(discarded);
        }

        private bool TryGetSelected(out AsyncServerStreamingCall<TResponse> call)
        {
            lock (_gate)
            {
                call = _terminalCall!;
                return _selected && call is not null;
            }
        }

        private AsyncServerStreamingCall<TResponse> TerminalCall()
        {
            lock (_gate)
            {
                return _terminalCall
                    ?? throw new InvalidOperationException(
                        "The streaming call has not selected an underlying attempt yet.");
            }
        }

        private CancellationTokenSource? CreateOperationCancellation(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken)
                : null;

        private void Dispose()
        {
            List<Attempt> attempts;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                attempts = [.. _attempts];
                foreach (var attempt in attempts)
                {
                    MarkFailureDisposed(attempt);
                }

                _attempts.Clear();
                if (_selectedAttemptCall is not null && _terminalCancellation is not null)
                {
                    attempts.Add(new Attempt(
                        _selectedAttemptCall,
                        _terminalCancellation,
                        Exception: null));
                }

                _terminalCall = null;
                _selectedAttemptCall = null;
                _terminalCancellation = null;
                _activeAttempt = null;
                _activeAttemptReady.TrySetException(
                    new ObjectDisposedException(nameof(ShieldStreamingClientInterceptor)));
            }

            CancelLifetime(_lifetime);
            DisposeAttempts(attempts);
            _lifetime.Dispose();
        }

        private void MarkFailureDisposed(Attempt attempt)
        {
            if (attempt.Exception is AttemptFailureException failure
                && _failedCalls is not null
                && _failedCalls.TryGetValue(failure, out var selection))
            {
                selection.MarkDisposed();
            }
        }

        private static void DisposeAttempts(List<Attempt>? attempts)
        {
            if (attempts is null)
            {
                return;
            }

            foreach (var attempt in attempts)
            {
                DisposeAttempt(attempt.Call, attempt.Cancellation);
            }
        }

        private static void DisposeAttempt(
            AsyncServerStreamingCall<TResponse> call,
            CancellationTokenSource cancellation)
        {
            CancelLifetime(cancellation);
            call.Dispose();
            cancellation.Dispose();
        }

        private readonly record struct Attempt(
            AsyncServerStreamingCall<TResponse> Call,
            CancellationTokenSource Cancellation,
            Exception? Exception);

        private readonly record struct ReadResult(
            AsyncServerStreamingCall<TResponse> Call,
            CancellationTokenSource Cancellation,
            bool HasNext);

        private readonly record struct HeadersResult(
            AsyncServerStreamingCall<TResponse> Call,
            CancellationTokenSource Cancellation,
            Metadata Headers);

        private sealed class FailureSelection(
            AsyncServerStreamingCall<TResponse> attemptCall,
            AsyncServerStreamingCall<TResponse> call)
        {
            public AsyncServerStreamingCall<TResponse> AttemptCall { get; } = attemptCall;

            public AsyncServerStreamingCall<TResponse> Call { get; } = call;

            public bool Disposed { get; private set; }

            public void MarkDisposed() => Disposed = true;
        }

        private sealed class AttemptFailureException : Exception
        {
            private const string ExceptionProxyDataKey =
                "Kevlar.Internal.ExceptionProxy.6b21d876-5f0c-45d4-a873-cd6d83e9158b";

            public AttemptFailureException(Exception original)
                : base(original.Message, original)
            {
                OriginalException = original;
                Data[ExceptionProxyDataKey] = original;
            }

            public Exception OriginalException { get; }
        }

        private sealed class ExpiredDeadlineRpcException(RpcException original)
            : Exception(original.Message, original)
        {
            public RpcException Original { get; } = original;
        }
    }

    private sealed class ClientStreamingState<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly Shield _shield;
        private readonly CancellationTokenSource _lifetime;
        private readonly AsyncClientStreamingCall<TRequest, TResponse> _call;
        private readonly CancellationToken _callerToken;
        private int _disposed;

        public ClientStreamingState(
            Shield shield,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            _shield = shield;
            _callerToken = context.Options.CancellationToken;
            _lifetime = CreateLifetime(context.Options.CancellationToken);
            var attemptContext = new ClientInterceptorContext<TRequest, TResponse>(
                context.Method,
                context.Host,
                context.Options.WithCancellationToken(_lifetime.Token));
            try
            {
                _call = continuation(attemptContext);
            }
            catch
            {
                _lifetime.Dispose();
                throw;
            }
        }

        public AsyncClientStreamingCall<TRequest, TResponse> Start() => new(
            new ShieldedClientStreamWriter<TRequest>(_call.RequestStream, _shield, _lifetime),
            AwaitCallLifetimeAsync(_call.ResponseAsync, _lifetime, _callerToken),
            static state => ((ClientStreamingState<TRequest, TResponse>)state).ExecuteHeadersAsync(),
            static state => ((ClientStreamingState<TRequest, TResponse>)state)._call.GetStatus(),
            static state => ((ClientStreamingState<TRequest, TResponse>)state)._call.GetTrailers(),
            static state => ((ClientStreamingState<TRequest, TResponse>)state).Dispose(),
            this);

        private Task<Metadata> ExecuteHeadersAsync() => _shield.ExecuteAsync(
            this,
            static (state, token) => AwaitWithLifetimeAsync(
                state._call.ResponseHeadersAsync,
                state._lifetime,
                token),
            _lifetime.Token).AsTask();

        private void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            CancelLifetime(_lifetime);
            _call.Dispose();
            _lifetime.Dispose();
        }
    }

    private sealed class DuplexStreamingState<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly Shield _shield;
        private readonly CancellationTokenSource _lifetime;
        private readonly AsyncDuplexStreamingCall<TRequest, TResponse> _call;
        private int _disposed;

        public DuplexStreamingState(
            Shield shield,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            _shield = shield;
            _lifetime = CreateLifetime(context.Options.CancellationToken);
            var attemptContext = new ClientInterceptorContext<TRequest, TResponse>(
                context.Method,
                context.Host,
                context.Options.WithCancellationToken(_lifetime.Token));
            try
            {
                _call = continuation(attemptContext);
            }
            catch
            {
                _lifetime.Dispose();
                throw;
            }
        }

        public AsyncDuplexStreamingCall<TRequest, TResponse> Start() => new(
            new ShieldedClientStreamWriter<TRequest>(_call.RequestStream, _shield, _lifetime),
            new ShieldedAsyncStreamReader<TResponse>(
                _call.ResponseStream,
                _shield,
                _lifetime.Token),
            static state => ((DuplexStreamingState<TRequest, TResponse>)state).ExecuteHeadersAsync(),
            static state => ((DuplexStreamingState<TRequest, TResponse>)state)._call.GetStatus(),
            static state => ((DuplexStreamingState<TRequest, TResponse>)state)._call.GetTrailers(),
            static state => ((DuplexStreamingState<TRequest, TResponse>)state).Dispose(),
            this);

        private Task<Metadata> ExecuteHeadersAsync() => _shield.ExecuteAsync(
            this,
            static (state, token) => AwaitWithLifetimeAsync(
                state._call.ResponseHeadersAsync,
                state._lifetime,
                token),
            _lifetime.Token).AsTask();

        private void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            CancelLifetime(_lifetime);
            _call.Dispose();
            _lifetime.Dispose();
        }
    }

    private sealed class ShieldedClientStreamWriter<T>(
        IClientStreamWriter<T> writer,
        Shield shield,
        CancellationTokenSource lifetime) : IClientStreamWriter<T>
    {
        public WriteOptions? WriteOptions
        {
            get => writer.WriteOptions;
            set => writer.WriteOptions = value;
        }

        public Task WriteAsync(T message) => ExecuteWriteAsync(message, lifetime.Token);

        public Task WriteAsync(T message, CancellationToken cancellationToken) =>
            ExecuteWriteAsync(message, cancellationToken);

        private async Task ExecuteWriteAsync(T message, CancellationToken cancellationToken)
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetime.Token,
                cancellationToken);
            try
            {
                await shield.ExecuteAsync(
                    (Writer: writer, Message: message, Lifetime: lifetime),
#if NET6_0_OR_GREATER
                    static (state, token) => AwaitGrpcOperationAsync(
                        state.Writer.WriteAsync(state.Message, token),
                        token),
#else
                    static (state, token) => AwaitWithCancellationAsync(
                        state.Writer.WriteAsync(state.Message),
                        state.Lifetime,
                        token),
#endif
                    operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                cancellationToken.IsCancellationRequested
                && exception.CancellationToken != cancellationToken)
            {
                throw new OperationCanceledException(
                    exception.Message,
                    exception,
                    cancellationToken);
            }
        }

        public Task CompleteAsync() => shield.ExecuteAsync(
            (Writer: writer, Lifetime: lifetime),
            static (state, token) => AwaitWithLifetimeAsync(
                state.Writer.CompleteAsync(),
                state.Lifetime,
                token),
            lifetime.Token).AsTask();
    }

    private sealed class ShieldedAsyncStreamReader<T>(
        IAsyncStreamReader<T> reader,
        Shield shield,
        CancellationToken lifetimeToken) : IAsyncStreamReader<T>
    {
        public T Current => reader.Current;

        public Task<bool> MoveNext(CancellationToken cancellationToken) => shield.ExecuteAsync(
            reader,
            static (state, token) => AwaitGrpcOperationAsync(state.MoveNext(token), token),
            cancellationToken.CanBeCanceled ? cancellationToken : lifetimeToken).AsTask();
    }

    private static CancellationTokenSource CreateLifetime(CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();

    private static async ValueTask<T> AwaitWithLifetimeAsync<T>(
        Task<T> task,
        CancellationTokenSource lifetime,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
            static state => CancelLifetime((CancellationTokenSource)state!),
            lifetime);
        return await AwaitGrpcOperationAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask AwaitWithLifetimeAsync(
        Task task,
        CancellationTokenSource lifetime,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
            static state => CancelLifetime((CancellationTokenSource)state!),
            lifetime);
        await AwaitGrpcOperationAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> AwaitCallLifetimeAsync<T>(
        Task<T> task,
        CancellationTokenSource lifetime,
        CancellationToken callerToken)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (RpcException exception) when (
            exception.StatusCode == StatusCode.Cancelled
            && lifetime.IsCancellationRequested)
        {
            var cancellationToken = callerToken.IsCancellationRequested
                ? callerToken
                : lifetime.Token;
            throw new OperationCanceledException(
                exception.Message,
                exception,
                cancellationToken);
        }
    }

    private static async ValueTask<T> AwaitGrpcOperationAsync<T>(
        Task<T> task,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (RpcException exception) when (
            exception.StatusCode == StatusCode.Cancelled
            && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                exception.Message,
                exception,
                cancellationToken);
        }
    }

    private static async ValueTask AwaitGrpcOperationAsync(
        Task task,
        CancellationToken cancellationToken)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (RpcException exception) when (
            exception.StatusCode == StatusCode.Cancelled
            && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                exception.Message,
                exception,
                cancellationToken);
        }
    }

    private static void CancelLifetime(CancellationTokenSource lifetime)
    {
        try
        {
            lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Disposal may race an in-flight operation's cancellation callback.
        }
    }

#if !NET6_0_OR_GREATER
    private static async ValueTask AwaitWithCancellationAsync(
        Task task,
        CancellationTokenSource lifetime,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
            static state => CancelLifetime((CancellationTokenSource)state!),
            lifetime);
        cancellationToken.ThrowIfCancellationRequested();
        await AwaitGrpcOperationAsync(task, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
#endif
}
