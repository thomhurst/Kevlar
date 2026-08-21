using Grpc.Core;
using Grpc.Core.Interceptors;
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
    /// <paramref name="operationShield"/> protects individual reads, writes, and client-streaming
    /// response completion, and therefore must invoke each continuation at most once.
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

        private AsyncServerStreamingCall<TResponse>? _terminalCall;
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
                    if (_operationShield is null)
                    {
                        return await selected.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);
                    }

                    return await _operationShield.ExecuteAsync(
                        selected.ResponseStream,
                        static (reader, token) => new ValueTask<bool>(reader.MoveNext(token)),
                        cancellationToken.CanBeCanceled
                            ? cancellationToken
                            : _lifetime.Token).ConfigureAwait(false);
                }

                using var operation = CreateOperationCancellation(cancellationToken);
                try
                {
                    var result = await _establishmentShield.ExecuteAsync(
                        this,
                        static (state, token) => state.StartAndReadAsync(token),
                        operation?.Token ?? _lifetime.Token).ConfigureAwait(false);
                    Select(result.Call, result.Cancellation);
                    return result.HasNext;
                }
                catch (Exception exception)
                {
                    SelectFailure(exception);
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
            await _operationGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
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
                    Select(result.Call, result.Cancellation);
                    return result.Headers;
                }
                catch (Exception exception)
                {
                    SelectFailure(exception);
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
                CompleteAttempt(attempt.Call, exception: null);
                return new ReadResult(attempt.Call, attempt.Cancellation, hasNext);
            }
            catch (Exception exception)
            {
                exception = NormalizeAttemptException(exception, cancellationToken);
                CompleteAttempt(attempt.Call, exception);
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
                CompleteAttempt(attempt.Call, exception: null);
                return new HeadersResult(attempt.Call, attempt.Cancellation, headers);
            }
            catch (Exception exception)
            {
                exception = NormalizeAttemptException(exception, cancellationToken);
                CompleteAttempt(attempt.Call, exception);
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
            throw new ExpiredDeadlineRpcException(
                new RpcException(new Status(StatusCode.DeadlineExceeded, "Deadline exceeded.")));
        }

        private Exception NormalizeAttemptException(
            Exception exception,
            CancellationToken cancellationToken)
        {
            if (exception is RpcException { StatusCode: StatusCode.Cancelled } rpcCancellation
                && cancellationToken.IsCancellationRequested)
            {
                return new OperationCanceledException(
                    rpcCancellation.Message,
                    rpcCancellation,
                    cancellationToken);
            }

            if (exception is RpcException { StatusCode: StatusCode.DeadlineExceeded } deadlineException
                && _context.Options.Deadline is { } deadline
                && deadline <= DateTime.UtcNow)
            {
                CancelLifetime(_lifetime);
                return new ExpiredDeadlineRpcException(deadlineException);
            }

            return exception;
        }

        private void RethrowNormalized(Exception exception)
        {
            if (exception is OperationCanceledException cancellation
                && _context.Options.CancellationToken.IsCancellationRequested
                && cancellation.CancellationToken != _context.Options.CancellationToken)
            {
                throw new OperationCanceledException(
                    cancellation.Message,
                    cancellation,
                    _context.Options.CancellationToken);
            }

            if (exception is ExpiredDeadlineRpcException deadlineException)
            {
                ExceptionDispatchInfo.Capture(deadlineException.Original).Throw();
            }
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
                    _attempts.Add(new Attempt(call, cancellation, Exception: null));
                }
            }

            if (dispose)
            {
                DisposeAttempt(call, cancellation);
                throw new ObjectDisposedException(nameof(ShieldStreamingClientInterceptor));
            }

            return new Attempt(call, cancellation, Exception: null);
        }

        private void CompleteAttempt(AsyncServerStreamingCall<TResponse> call, Exception? exception)
        {
            lock (_gate)
            {
                for (var index = _attempts.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(_attempts[index].Call, call))
                    {
                        _attempts[index] = new Attempt(
                            call,
                            _attempts[index].Cancellation,
                            exception);
                        return;
                    }
                }
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

                    (superseded ??= []).Add(_attempts[index]);
                    _attempts.RemoveAt(index);
                }
            }

            DisposeAttempts(superseded);
        }

        private void SelectFailure(Exception exception)
        {
            Attempt? selected = null;
            lock (_gate)
            {
                for (var index = _attempts.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(_attempts[index].Exception, exception))
                    {
                        selected = _attempts[index];
                        break;
                    }
                }

                if (selected is null)
                {
                    for (var index = _attempts.Count - 1; index >= 0; index--)
                    {
                        if (_attempts[index].Exception is not null)
                        {
                            selected = _attempts[index];
                            break;
                        }
                    }
                }
            }

            if (selected is { } failure)
            {
                Select(failure.Call, failure.Cancellation);
            }
        }

        private void Select(
            AsyncServerStreamingCall<TResponse> call,
            CancellationTokenSource cancellation)
        {
            List<Attempt>? discarded = null;
            var disposeSelectedLoser = false;
            lock (_gate)
            {
                if (_selected)
                {
                    disposeSelectedLoser = !ReferenceEquals(_terminalCall, call);
                }
                else
                {
                    _selected = true;
                    _terminalCall = call;
                    _terminalCancellation = cancellation;
                    foreach (var attempt in _attempts)
                    {
                        if (!ReferenceEquals(attempt.Call, call))
                        {
                            (discarded ??= []).Add(attempt);
                        }
                    }

                    _attempts.Clear();
                }
            }

            if (disposeSelectedLoser)
            {
                DisposeAttempt(call, cancellation);
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
                _attempts.Clear();
                if (_terminalCall is not null && _terminalCancellation is not null)
                {
                    attempts.Add(new Attempt(
                        _terminalCall,
                        _terminalCancellation,
                        Exception: null));
                    _terminalCall = null;
                    _terminalCancellation = null;
                }
            }

            CancelLifetime(_lifetime);
            DisposeAttempts(attempts);
            _lifetime.Dispose();
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
        private int _disposed;

        public ClientStreamingState(
            Shield shield,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
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

        public AsyncClientStreamingCall<TRequest, TResponse> Start() => new(
            new ShieldedClientStreamWriter<TRequest>(_call.RequestStream, _shield, _lifetime),
            ExecuteResponseAsync(),
            static state => ((ClientStreamingState<TRequest, TResponse>)state).ExecuteHeadersAsync(),
            static state => ((ClientStreamingState<TRequest, TResponse>)state)._call.GetStatus(),
            static state => ((ClientStreamingState<TRequest, TResponse>)state)._call.GetTrailers(),
            static state => ((ClientStreamingState<TRequest, TResponse>)state).Dispose(),
            this);

        private Task<TResponse> ExecuteResponseAsync() => _shield.ExecuteAsync(
            this,
            static (state, token) => AwaitWithLifetimeAsync(
                state._call.ResponseAsync,
                state._lifetime,
                token),
            _lifetime.Token).AsTask();

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

        public Task WriteAsync(T message) => WriteAsync(message, lifetime.Token);

        public Task WriteAsync(T message, CancellationToken cancellationToken) => shield.ExecuteAsync(
            (Writer: writer, Message: message),
#if NET6_0_OR_GREATER
            static (state, token) => new ValueTask(state.Writer.WriteAsync(state.Message, token)),
#else
            static (state, token) => AwaitWithCancellationAsync(
                state.Writer.WriteAsync(state.Message),
                token),
#endif
            cancellationToken.CanBeCanceled ? cancellationToken : lifetime.Token).AsTask();

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
            static (state, token) => new ValueTask<bool>(state.MoveNext(token)),
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
        return await task.ConfigureAwait(false);
    }

    private static async ValueTask AwaitWithLifetimeAsync(
        Task task,
        CancellationTokenSource lifetime,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
            static state => CancelLifetime((CancellationTokenSource)state!),
            lifetime);
        await task.ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await task.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
#endif
}
