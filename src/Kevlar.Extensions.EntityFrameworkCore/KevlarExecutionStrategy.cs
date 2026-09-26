using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Kevlar.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kevlar.Extensions.EntityFrameworkCore;

/// <summary>Executes EF Core operations through a sequential Kevlar shield.</summary>
/// <remarks>
/// The strategy belongs to one DbContext and must not be used concurrently. Transaction delegates
/// must recreate and dispose their transaction on each attempt. Hedging, timeout strategies that can
/// abandon active database work, and live-forwarding shields are not supported.
/// </remarks>
public sealed class KevlarExecutionStrategy : IExecutionStrategy
{
    private readonly DbContext _context;
    private readonly Shield _shield;
    private readonly ExecutionScope _scope;
    private readonly Func<Exception, bool> _shouldVerifySuccess;
    private readonly bool _retriesOnFailure;

    /// <summary>Creates a strategy using the installed SQL Server or Npgsql provider's retry policy.</summary>
    /// <param name="dependencies">EF Core execution strategy dependencies.</param>
    /// <remarks>Other providers require an explicit shield. Provider policy discovery requires untrimmed provider assemblies.</remarks>
    [RequiresUnreferencedCode(ProviderRetryPolicy.TrimmingMessage)]
    public KevlarExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : this(dependencies, ProviderRetryPolicy.Create(dependencies, factory: null))
    {
    }

    /// <summary>Creates a strategy using an explicit shield and optional commit-verification filter.</summary>
    /// <param name="dependencies">EF Core execution strategy dependencies.</param>
    /// <param name="shield">A sequential shield. Its retry predicates and delays determine retry behavior.</param>
    /// <param name="shouldVerifySuccessOn">Optional exception filter for verification. By default every non-cancellation failure may be verified.</param>
    /// <remarks>A successful verification result prevents replay even when the original operation threw.</remarks>
    public KevlarExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        Shield shield,
        Func<Exception, bool>? shouldVerifySuccessOn = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(shield);
        if (!ReferenceEquals(shield.CurrentSnapshot, shield))
        {
            throw new NotSupportedException("EF Core execution strategies require a fixed shield snapshot.");
        }

        foreach (var strategy in shield.Strategies)
        {
            if (strategy is HedgingStrategy or TimeoutStrategy
                || strategy is not RetryStrategy && !strategy.InvokesContinuationAtMostOnce)
            {
                throw new NotSupportedException(
                    "EF Core execution strategies require sequential operations that complete before another attempt starts. Hedging, timeouts, and unbounded custom repetition are not supported.");
            }
        }

        _context = dependencies.CurrentContext.Context;
        _shield = shield;
        _shouldVerifySuccess = shouldVerifySuccessOn ?? (static exception => exception is not OperationCanceledException);
        _retriesOnFailure = shield.Strategies.OfType<RetryStrategy>().Any(static retry => retry.MaxRetries > 0);
        _scope = new ExecutionScope(dependencies, this);
    }

    private KevlarExecutionStrategy(ExecutionStrategyDependencies dependencies, ProviderRetryPolicy policy)
        : this(dependencies, policy.CreateShield(), policy.ShouldVerifySuccess)
    {
    }

    [RequiresUnreferencedCode(ProviderRetryPolicy.TrimmingMessage)]
    internal static KevlarExecutionStrategy CreateDefault(
        ExecutionStrategyDependencies dependencies,
        Func<ExecutionStrategyDependencies, IExecutionStrategy>? factory) =>
        new(dependencies, ProviderRetryPolicy.Create(dependencies, factory));

    /// <summary>Gets whether this strategy can retry outside another EF Core execution scope.</summary>
    public bool RetriesOnFailure => _retriesOnFailure
        && (ExecutionStrategy.Current is null || ReferenceEquals(ExecutionStrategy.Current, _scope));

    /// <summary>Executes an operation, retrying according to the shield and verifying ambiguous failures when requested.</summary>
    /// <typeparam name="TState">The caller state type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="state">Caller state passed to both delegates.</param>
    /// <param name="operation">The database operation.</param>
    /// <param name="verifySucceeded">Optional verification returning the committed result when successful.</param>
    /// <returns>The operation result or a successfully verified result.</returns>
    public TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (ExecutionStrategy.Current is not null)
        {
            return operation(_context, state);
        }

        _scope.ValidateFirstExecution();
        using var verification = verifySucceeded is null ? null : new VerificationState(default);
        TResult result;
        try
        {
            result = _shield.Execute(_ =>
            {
                try
                {
                    return _scope.Run(() => operation(_context, state));
                }
                catch (Exception exception) when (CanVerify(exception, verifySucceeded is not null))
                {
                    ExecutionResult<TResult> verified;
                    try
                    {
                        verified = _shield.Execute(_ => _scope.Run(() => verifySucceeded!(_context, state)), verification!.Token);
                    }
                    catch (Exception verificationException)
                    {
                        verification!.Fail(verificationException);
                        throw;
                    }

                    if (verified.IsSuccessful)
                    {
                        return verified.Result;
                    }

                    throw;
                }
            }, verification?.Token ?? default);
        }
        catch
        {
            verification?.ThrowIfFailed();
            throw;
        }

        verification?.ThrowIfFailed();
        return result;
    }

    /// <summary>Asynchronously executes an operation and preserves transaction verification and cancellation.</summary>
    /// <typeparam name="TState">The caller state type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="state">Caller state passed to both delegates.</param>
    /// <param name="operation">The database operation.</param>
    /// <param name="verifySucceeded">Optional verification returning the committed result when successful.</param>
    /// <param name="cancellationToken">Cancels execution, retry delays, and verification.</param>
    /// <returns>The operation result or a successfully verified result.</returns>
    public async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (ExecutionStrategy.Current is not null)
        {
            return await operation(_context, state, cancellationToken).ConfigureAwait(false);
        }

        _scope.ValidateFirstExecution();
        using var verification = verifySucceeded is null ? null : new VerificationState(cancellationToken);
        TResult result;
        try
        {
            result = await _shield.ExecuteAsync(async token =>
            {
                try
                {
                    return await _scope.RunAsync(() => operation(_context, state, token)).ConfigureAwait(false);
                }
                catch (Exception exception) when (CanVerify(exception, verifySucceeded is not null))
                {
                    ExecutionResult<TResult> verified;
                    try
                    {
                        verified = await _shield.ExecuteAsync(
                            verifyToken => new ValueTask<ExecutionResult<TResult>>(
                                _scope.RunAsync(() => verifySucceeded!(_context, state, verifyToken))),
                            token).ConfigureAwait(false);
                    }
                    catch (Exception verificationException)
                    {
                        verification!.Fail(verificationException);
                        throw;
                    }

                    if (verified.IsSuccessful)
                    {
                        return verified.Result;
                    }

                    throw;
                }
            }, verification?.Token ?? cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            verification?.ThrowIfFailed();
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        verification?.ThrowIfFailed();
        return result;
    }

    private sealed class VerificationState : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private ExceptionDispatchInfo? _failure;

        internal VerificationState(CancellationToken cancellationToken) => _cancellation = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();

        internal CancellationToken Token => _cancellation.Token;

        internal void Fail(Exception exception)
        {
            _failure = ExceptionDispatchInfo.Capture(exception);
            // A failed verification must terminate the outer shield instead of replaying an ambiguous commit.
            _cancellation.Cancel();
        }

        internal void ThrowIfFailed() => _failure?.Throw();

        public void Dispose() => _cancellation.Dispose();
    }

    private bool CanVerify(Exception exception, bool hasVerifier) => hasVerifier
        && exception is not OperationCanceledException
        && ExecutionStrategy.CallOnWrappedException(exception, _shouldVerifySuccess);

    private sealed class ExecutionScope(ExecutionStrategyDependencies dependencies, KevlarExecutionStrategy owner)
        : ExecutionStrategy(dependencies, maxRetryCount: 0, maxRetryDelay: TimeSpan.Zero)
    {
        public override bool RetriesOnFailure => owner.RetriesOnFailure;

        internal void ValidateFirstExecution() => OnFirstExecution();

        internal TResult Run<TResult>(Func<TResult> operation)
        {
            var previous = Current;
            Current = this;
            try
            {
                return operation();
            }
            finally
            {
                Current = previous;
            }
        }

        internal async Task<TResult> RunAsync<TResult>(Func<Task<TResult>> operation)
        {
            var previous = Current;
            Current = this;
            try
            {
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                Current = previous;
            }
        }

        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
