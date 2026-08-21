using Grpc.Core;
using Grpc.Core.Interceptors;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

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
        private readonly object _gate = new();
        private readonly Shield _shield;
        private readonly TRequest _request;
        private readonly ClientInterceptorContext<TRequest, TResponse> _context;
        private readonly AsyncUnaryCallContinuation<TRequest, TResponse> _continuation;
        private readonly bool _forwardHeadersEarly;
        private readonly CancellationTokenSource _lifetime;
        private readonly List<AttemptRecord> _attempts = [];
        private readonly TaskCompletionSource<AsyncUnaryCall<TResponse>?> _responseHeadersCall =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ConditionalWeakTable<Exception, FailureSelection>? _failedCalls;
        private Exception? _deadlineFailure;
        private Task<TResponse> _response = null!;
        private AsyncUnaryCall<TResponse>? _terminalCall;
        private AsyncUnaryCall<TResponse>? _selectedAttemptCall;
        private int _lifetimeCompleted;
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
            _forwardHeadersEarly = shield.InvokesContinuationAtMostOnce;
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

                if (exception is AttemptRpcException attemptException)
                {
                    ExceptionDispatchInfo.Capture(attemptException.Original).Throw();
                }

                if (exception is ExpiredDeadlineRpcException deadlineException)
                {
                    ExceptionDispatchInfo.Capture(deadlineException.Original).Throw();
                }

                throw;
            }
            finally
            {
                CompleteLifetime();
            }
        }

        private async ValueTask<AttemptResult> InvokeAsync(CancellationToken cancellationToken)
        {
            if (_context.Options.Deadline is { } admissionDeadline
                && admissionDeadline <= DateTime.UtcNow)
            {
                CancelLifetime();
                if (Volatile.Read(ref _deadlineFailure) is { } deadlineFailure)
                {
                    ExceptionDispatchInfo.Capture(deadlineFailure).Throw();
                }

                throw new ExpiredDeadlineRpcException(
                    new RpcException(new Status(StatusCode.DeadlineExceeded, "Deadline exceeded.")));
            }

            DisposeSupersededFailures();
            var options = _context.Options.WithCancellationToken(cancellationToken);
            var context = new ClientInterceptorContext<TRequest, TResponse>(
                _context.Method,
                _context.Host,
                options);
            var call = _continuation(_request, context);
            Track(call);

            try
            {
                var response = await call.ResponseAsync.ConfigureAwait(false);
                CompleteAttempt(call, exception: null, failureCall: null);
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
                _ = await CompleteFailureAsync(call, cancellation).ConfigureAwait(false);
                throw cancellation;
            }
            catch (Exception exception)
            {
                var deadlineException = exception as RpcException;
                var deadlineExceeded = deadlineException?.StatusCode == StatusCode.DeadlineExceeded;
                if (deadlineExceeded)
                {
                    if (_context.Options.Deadline is { } deadline
                        && deadline <= DateTime.UtcNow)
                    {
                        exception = new ExpiredDeadlineRpcException(deadlineException!);
                    }
                }

                var attemptException = await CompleteFailureAsync(call, exception).ConfigureAwait(false);
                if (deadlineExceeded)
                {
                    Volatile.Write(ref _deadlineFailure, attemptException);
                }

                if (ReferenceEquals(attemptException, exception))
                {
                    throw;
                }

                throw attemptException ?? exception;
            }
        }

        private void DisposeSupersededFailures()
        {
            List<AsyncUnaryCall<TResponse>>? superseded = null;
            lock (_gate)
            {
                for (var index = _attempts.Count - 1; index >= 0; index--)
                {
                    if (_attempts[index].Exception is null)
                    {
                        continue;
                    }

                    (superseded ??= []).Add(_attempts[index].Call);
                    _attempts.RemoveAt(index);
                }
            }

            DisposeCalls(superseded);
        }

        private void Track(AsyncUnaryCall<TResponse> call)
        {
            var dispose = false;
            lock (_gate)
            {
                if (_selected || _disposed)
                {
                    dispose = !ReferenceEquals(call, _selectedAttemptCall);
                }
                else
                {
                    _attempts.Add(new AttemptRecord(call, exception: null));
                    if (_forwardHeadersEarly)
                    {
                        _responseHeadersCall.TrySetResult(call);
                    }
                }
            }

            if (dispose)
            {
                call.Dispose();
            }
        }

        private async ValueTask<Exception> CompleteFailureAsync(
            AsyncUnaryCall<TResponse> call,
            Exception exception)
        {
            var failureCall = await SnapshotFailureAsync(call, exception).ConfigureAwait(false);
            return CompleteAttempt(call, exception, failureCall) ?? exception;
        }

        private Exception? CompleteAttempt(
            AsyncUnaryCall<TResponse> call,
            Exception? exception,
            AsyncUnaryCall<TResponse>? failureCall)
        {
            lock (_gate)
            {
                for (var index = _attempts.Count - 1; index >= 0; index--)
                {
                    if (!ReferenceEquals(_attempts[index].Call, call))
                    {
                        continue;
                    }

                    if (exception is not null
                        && _failedCalls is not null
                        && _failedCalls.TryGetValue(exception, out _)
                        && exception is RpcException rpcException)
                    {
                        exception = new AttemptRpcException(rpcException);
                    }

                    _attempts[index] = new AttemptRecord(call, exception);
                    if (exception is not null)
                    {
                        (_failedCalls ??= new())
                            .GetValue(exception, static _ => new FailureSelection())
                            .Set(call, failureCall);
                    }

                    return exception;
                }
            }

            return exception;
        }

        private static async ValueTask<AsyncUnaryCall<TResponse>> SnapshotFailureAsync(
            AsyncUnaryCall<TResponse> call,
            Exception exception)
        {
            var responseHeaders = call.ResponseHeadersAsync;
            try
            {
                responseHeaders = Task.FromResult(
                    await responseHeaders.ConfigureAwait(false));
            }
            catch (Exception headersException)
            {
                responseHeaders = Task.FromException<Metadata>(headersException);
                _ = responseHeaders.Exception;
            }

            var rpcException = exception as RpcException ?? exception.InnerException as RpcException;
            var status = rpcException?.Status ?? GetStatusOrUnknown(call, exception);
            var trailers = rpcException?.Trailers ?? GetTrailersOrEmpty(call);
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(default(TResponse)!),
                responseHeaders,
                () => status,
                () => trailers,
                static () => { });
        }

        private static Status GetStatusOrUnknown(
            AsyncUnaryCall<TResponse> call,
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

        private static Metadata GetTrailersOrEmpty(AsyncUnaryCall<TResponse> call)
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

        private void SelectFailure(Exception exception)
        {
            AsyncUnaryCall<TResponse>? call = null;
            AsyncUnaryCall<TResponse>? selectedAttemptCall = null;
            lock (_gate)
            {
                for (var index = _attempts.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(_attempts[index].Exception, exception))
                    {
                        call = _failedCalls is not null
                            && _failedCalls.TryGetValue(exception, out var matchingFailure)
                            ? matchingFailure.Call
                            : _attempts[index].Call;
                        selectedAttemptCall = _attempts[index].Call;
                        break;
                    }
                }

                if (call is null
                    && _failedCalls is not null
                    && _failedCalls.TryGetValue(exception, out var failure))
                {
                    call = failure.Call;
                    selectedAttemptCall = failure.AttemptCall;
                }

                if (call is null && exception is TimeoutExceededException)
                {
                    for (var index = _attempts.Count - 1; index >= 0; index--)
                    {
                        var attemptException = _attempts[index].Exception;
                        if (attemptException is null)
                        {
                            continue;
                        }

                        call = _failedCalls is not null
                            && _failedCalls.TryGetValue(attemptException, out failure)
                            ? failure.Call
                            : _attempts[index].Call;
                        selectedAttemptCall = _attempts[index].Call;
                        break;
                    }
                }
            }

            SelectCore(call, selectedAttemptCall);
        }

        private void SelectCore(
            AsyncUnaryCall<TResponse>? terminalCall,
            AsyncUnaryCall<TResponse>? selectedAttemptCall = null)
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
                _selectedAttemptCall = selectedAttemptCall ?? terminalCall;
                foreach (var attempt in _attempts)
                {
                    if (!ReferenceEquals(attempt.Call, _selectedAttemptCall))
                    {
                        (discarded ??= []).Add(attempt.Call);
                    }
                }

                _attempts.Clear();
            }

            DisposeCalls(discarded);
            _responseHeadersCall.TrySetResult(terminalCall);
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
            var call = await _responseHeadersCall.Task.ConfigureAwait(false);
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
                if (_selectedAttemptCall is not null)
                {
                    calls.Add(_selectedAttemptCall);
                }
            }

            CancelLifetime();
            DisposeCalls(calls);
        }

        private void CompleteLifetime()
        {
            if (Interlocked.Exchange(ref _lifetimeCompleted, 1) == 0)
            {
                _lifetime.Dispose();
            }
        }

        private void CancelLifetime()
        {
            try
            {
                _lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The execution completed and released the source concurrently.
            }
        }

        private sealed class FailureSelection
        {
            public AsyncUnaryCall<TResponse>? AttemptCall { get; private set; }

            public AsyncUnaryCall<TResponse>? Call { get; set; }

            public void Set(
                AsyncUnaryCall<TResponse> attemptCall,
                AsyncUnaryCall<TResponse>? failureCall)
            {
                AttemptCall = attemptCall;
                Call = failureCall;
            }
        }

        private sealed class AttemptRpcException(RpcException original)
            : RpcException(original.Status, original.Trailers, original.Message)
        {
            public RpcException Original { get; } = original;
        }

        private sealed class ExpiredDeadlineRpcException(RpcException original)
            : Exception(original.Message, original)
        {
            public RpcException Original { get; } = original;
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
