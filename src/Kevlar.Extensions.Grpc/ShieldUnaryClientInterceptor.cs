using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kevlar.Extensions.Grpc;

/// <summary>
/// Runs asynchronous unary gRPC client calls through a shared <see cref="Shield"/>. Streaming,
/// blocking unary, and server calls pass through unchanged.
/// </summary>
/// <remarks>
/// Each retry or hedge invokes the underlying call continuation again. Only use strategies that
/// can make multiple attempts for idempotent RPC methods. The original absolute gRPC deadline is
/// preserved on every attempt; Kevlar timeout strategies additionally cancel their attempt token,
/// and whichever limit expires first determines the surfaced exception.
/// </remarks>
public sealed class ShieldUnaryClientInterceptor : Interceptor
{
    private readonly Shield _shield;

    /// <summary>Creates an interceptor that shares the supplied immutable shield.</summary>
    public ShieldUnaryClientInterceptor(Shield shield) =>
        _shield = shield ?? throw new ArgumentNullException(nameof(shield));

    /// <inheritdoc />
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        if (request is null) { throw new ArgumentNullException(nameof(request)); }
        if (continuation is null) { throw new ArgumentNullException(nameof(continuation)); }

        return new UnaryCallState<TRequest, TResponse>(
            _shield,
            request,
            context,
            continuation).Start();
    }

    private sealed class UnaryCallState<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly Lock _gate = new();
        private readonly Shield _shield;
        private readonly TRequest _request;
        private readonly ClientInterceptorContext<TRequest, TResponse> _context;
        private readonly AsyncUnaryCallContinuation<TRequest, TResponse> _continuation;
        private readonly CancellationTokenSource _lifetime;
        private readonly List<AttemptRecord> _attempts = [];
        private readonly TaskCompletionSource<AsyncUnaryCall<TResponse>?> _selectedCall =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task<TResponse> _response = null!;
        private AsyncUnaryCall<TResponse>? _terminalCall;
        private bool _selected;
        private bool _disposed;

        public UnaryCallState(
            Shield shield,
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            _shield = shield;
            _request = request;
            _context = context;
            _continuation = continuation;
            _lifetime = context.Options.CancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(context.Options.CancellationToken)
                : new CancellationTokenSource();
        }

        public AsyncUnaryCall<TResponse> Start()
        {
            _response = ExecuteAsync();
            return new AsyncUnaryCall<TResponse>(
                _response,
                static state => ((UnaryCallState<TRequest, TResponse>)state).GetResponseHeadersAsync(),
                static state => ((UnaryCallState<TRequest, TResponse>)state).GetStatus(),
                static state => ((UnaryCallState<TRequest, TResponse>)state).GetTrailers(),
                static state => ((UnaryCallState<TRequest, TResponse>)state).Dispose(),
                this);
        }

        private async Task<TResponse> ExecuteAsync()
        {
            try
            {
                var result = await _shield.ExecuteAsync(
                    this,
                    static (state, cancellationToken) => state.InvokeAsync(cancellationToken),
                    _lifetime.Token).ConfigureAwait(false);
                SelectCore(result.Call);
                return result.Response;
            }
            catch (Exception exception)
            {
                SelectFailure(exception);
                if (exception is OperationCanceledException cancellation
                    && _context.Options.CancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        cancellation.Message,
                        cancellation,
                        _context.Options.CancellationToken);
                }

                throw;
            }
        }

        private async ValueTask<AttemptResult> InvokeAsync(CancellationToken cancellationToken)
        {
            var options = _context.Options.WithCancellationToken(cancellationToken);
            var context = new ClientInterceptorContext<TRequest, TResponse>(
                _context.Method,
                _context.Host,
                options);
            var call = _continuation(_request, context);

            try
            {
                var response = await call.ResponseAsync.ConfigureAwait(false);
                Record(call, exception: null);
                return new AttemptResult(call, response);
            }
            catch (RpcException exception) when (
                exception.StatusCode == StatusCode.Cancelled
                && cancellationToken.IsCancellationRequested)
            {
                var cancellation = new OperationCanceledException(
                    exception.Message,
                    exception,
                    cancellationToken);
                Record(call, cancellation);
                throw cancellation;
            }
            catch (Exception exception)
            {
                Record(call, exception);
                throw;
            }
        }

        private void Record(AsyncUnaryCall<TResponse> call, Exception? exception)
        {
            var dispose = false;
            lock (_gate)
            {
                if (_selected || _disposed)
                {
                    dispose = !ReferenceEquals(call, _terminalCall);
                }
                else
                {
                    _attempts.Add(new AttemptRecord(call, exception));
                }
            }

            if (dispose)
            {
                call.Dispose();
            }
        }

        private void SelectFailure(Exception exception)
        {
            AsyncUnaryCall<TResponse>? call = null;
            lock (_gate)
            {
                for (var index = _attempts.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(_attempts[index].Exception, exception))
                    {
                        call = _attempts[index].Call;
                        break;
                    }
                }
            }

            SelectCore(call);
        }

        private void SelectCore(AsyncUnaryCall<TResponse>? terminalCall)
        {
            List<AsyncUnaryCall<TResponse>>? discarded = null;
            lock (_gate)
            {
                if (_selected)
                {
                    return;
                }

                _selected = true;
                _terminalCall = terminalCall;
                foreach (var attempt in _attempts)
                {
                    if (!ReferenceEquals(attempt.Call, terminalCall))
                    {
                        (discarded ??= []).Add(attempt.Call);
                    }
                }

                _attempts.Clear();
            }

            DisposeCalls(discarded);
            _selectedCall.TrySetResult(terminalCall);
        }

        private static void DisposeCalls(List<AsyncUnaryCall<TResponse>>? calls)
        {
            if (calls is null)
            {
                return;
            }

            foreach (var call in calls)
            {
                call.Dispose();
            }
        }

        private async Task<Metadata> GetResponseHeadersAsync()
        {
            var call = await _selectedCall.Task.ConfigureAwait(false);
            if (call is not null)
            {
                return await call.ResponseHeadersAsync.ConfigureAwait(false);
            }

            await _response.ConfigureAwait(false);
            throw new InvalidOperationException("The gRPC call has no selected attempt.");
        }

        private Status GetStatus() => TerminalCall().GetStatus();

        private Metadata GetTrailers() => TerminalCall().GetTrailers();

        private AsyncUnaryCall<TResponse> TerminalCall()
        {
            lock (_gate)
            {
                return _terminalCall
                    ?? throw new InvalidOperationException("The gRPC call has not completed with an underlying attempt.");
            }
        }

        private void Dispose()
        {
            List<AsyncUnaryCall<TResponse>> calls;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                calls = new List<AsyncUnaryCall<TResponse>>(_attempts.Count + 1);
                foreach (var attempt in _attempts)
                {
                    calls.Add(attempt.Call);
                }

                _attempts.Clear();
                if (_terminalCall is not null)
                {
                    calls.Add(_terminalCall);
                }
            }

            _lifetime.Cancel();
            DisposeCalls(calls);
        }

        private readonly struct AttemptRecord(
            AsyncUnaryCall<TResponse> call,
            Exception? exception)
        {
            public AsyncUnaryCall<TResponse> Call { get; } = call;

            public Exception? Exception { get; } = exception;
        }

        private readonly struct AttemptResult(
            AsyncUnaryCall<TResponse> call,
            TResponse response)
        {
            public AsyncUnaryCall<TResponse> Call { get; } = call;

            public TResponse Response { get; } = response;
        }
    }
}
