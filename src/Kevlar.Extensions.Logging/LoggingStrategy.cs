using System.Runtime.ExceptionServices;
using Kevlar.Strategies;
using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal sealed class LoggingStrategy(LoggingRegistration registration)
    : Strategy, ITransparentStrategy, IStrategyAppendObserver, IShieldNameObserver
{
    private readonly LoggingTelemetryListener _listener = new(registration);

    protected internal override bool InvokesContinuationAtMostOnce => true;

    internal LoggingRegistration Registration { get; } = registration;

    internal IKevlarTelemetryListener Listener => _listener;

    void IStrategyAppendObserver.OnStrategyAppended(
        Strategy strategy,
        string? shieldName,
        int strategyIndex)
    {
        if (strategy is CircuitBreakerStrategy circuitBreaker)
        {
            circuitBreaker.Core.AttachTelemetryListener(
                previous: null,
                _listener,
                shieldName,
                strategyIndex);
        }
    }

    void IShieldNameObserver.OnShieldNamed(Strategy[] strategies, string shieldName) =>
        ShieldLoggingExtensions.AttachCircuitListeners(
            strategies,
            _listener,
            _listener,
            shieldName);

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var previous = context.TelemetryListener;
        context.TelemetryListener = _listener;
        var scopes = Registration.BeginScopes(context);

        try
        {
            var execution = next.InvokeAsync(context);
            if (execution.IsCompletedSuccessfully)
            {
                DisposeScopes(context, scopes);
                context.TelemetryListener = previous;
                return execution;
            }

            return AwaitAsync(execution, context, scopes, previous);
        }
        catch
        {
            DisposeScopes(context, scopes);
            context.TelemetryListener = previous;
            throw;
        }
    }

    private static async ValueTask<Outcome<T>> AwaitAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        IDisposable? scopes,
        IKevlarTelemetryListener? previous)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            DisposeScopes(context, scopes);
            context.TelemetryListener = previous;
        }
    }

    private static void DisposeScopes(KevlarContext context, IDisposable? scopes)
    {
        try
        {
            scopes?.Dispose();
        }
        catch (Exception exception)
        {
            KevlarDiagnostics.ReportCallbackError(CallbackErrorKind.Logging, context, exception);
        }
    }
}

internal sealed class LoggingRegistration(
    ILogger logger,
    LoggingOptionsSnapshot options,
    LoggingRegistration? next = null)
{
    public ILogger Logger { get; } = logger;

    public LoggingOptionsSnapshot Options { get; } = options;

    public LoggingRegistration? Next { get; } = next;

    public IDisposable? BeginScopes(KevlarContext context)
    {
        List<IDisposable>? scopes = null;
        for (var current = this; current is not null; current = current.Next)
        {
            if (!current.Options.IncludeScopes)
            {
                continue;
            }

            try
            {
                if (LoggerMessages.BeginShieldScope(current.Logger, context.ShieldName) is { } scope)
                {
                    (scopes ??= []).Add(scope);
                }
            }
            catch (Exception exception)
            {
                KevlarDiagnostics.ReportCallbackError(CallbackErrorKind.Logging, context, exception);
            }
        }

        return scopes is null ? null : new LoggingScopeCollection(scopes);
    }

    public LoggingRegistration WithNext(LoggingRegistration next) =>
        new(Logger, Options, next);
}

internal sealed class LoggingScopeCollection(List<IDisposable> scopes) : IDisposable
{
    public void Dispose()
    {
        Exception? failure = null;
        for (var index = scopes.Count - 1; index >= 0; index--)
        {
            try
            {
                scopes[index].Dispose();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
