using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Diagnostics;
#endif

namespace Kevlar.Internal;

internal static class KevlarActivities
{
#if NET8_0_OR_GREATER
    private static readonly ActivitySource? Source = CreateSource();
    public static bool Enabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Source?.HasListeners() == true;
    }
    public static bool EventsEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Enabled && FindActivity() is { IsAllDataRequested: true };
    }
#else
    public static bool Enabled => false;
    public static bool EventsEnabled => false;
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<Outcome<T>> InvokeAttemptAsync<T, TState>(
        in Continuation<T, TState> next, KevlarContext context, string strategyName)
    {
#if NET8_0_OR_GREATER
        if (Enabled)
        {
            return InvokeTracedAttemptAsync(next, context, strategyName);
        }
#endif
        return next.InvokeAsync(context);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<Outcome<T>> InvokeTracedAttemptAsync<T, TState>(
        Continuation<T, TState> next, KevlarContext context, string strategyName) =>
        AttemptAsync(context, strategyName, (next, context),
            static state => state.next.InvokeAsync(state.context));
#endif

    public static void RecordEvent(in KevlarTelemetryEvent item)
    {
#if NET8_0_OR_GREATER
        if (!Enabled || item.EventName == "execution_attempt"
            || FindActivity() is not { IsAllDataRequested: true } activity)
        {
            return;
        }

        try
        {
            var tags = new ActivityTagsCollection
            {
                { "kevlar.strategy.name", Bound(item.StrategyName) },
                { "kevlar.strategy.index", item.StrategyIndex },
                { "kevlar.attempt.number", item.AttemptNumber },
                { "kevlar.outcome.success", item.IsSuccess },
                { "kevlar.duration.seconds", item.Duration.TotalSeconds },
                { "kevlar.delay.seconds", item.Delay.TotalSeconds },
            };
            if (item.Exception is { } exception)
            {
                tags.Add("exception.type", Bound(exception.GetType().FullName));
            }
            if (item.FromState is { } from)
            {
                tags.Add("kevlar.circuit.from", from.ToString());
            }
            if (item.ToState is { } to)
            {
                tags.Add("kevlar.circuit.to", to.ToString());
            }
            if (item.RejectionKind is { } rejection)
            {
                tags.Add("kevlar.rejection.kind", Bound(rejection));
            }
            if (item.RetryAfter is { } retryAfter)
            {
                tags.Add("kevlar.retry_after.seconds", retryAfter.TotalSeconds);
            }
            if (item.SuppressionReason is { } reason)
            {
                tags.Add("kevlar.suppression.reason", Bound(reason));
            }
            if (item.EventName == "hedge_attempt")
            {
                tags.Add("kevlar.hedge.winner", item.IsWinner);
                tags.Add("kevlar.hedge.cancelled", item.IsCancelled);
            }
            activity.AddEvent(new ActivityEvent("kevlar." + Bound(item.EventName), tags: tags));
        }
        catch
        {
            // Diagnostics must not change protected outcomes.
        }
#endif
    }

#if NET8_0_OR_GREATER
    public static async ValueTask<T> ExecuteAsync<T, TState>(
        string? shieldName, TState state, Func<TState, ValueTask<T>> action)
    {
        using var scope = Start("kevlar.execute", shieldName);
        try
        {
            var result = await action(state).ConfigureAwait(false);
            scope.Complete(success: true, exception: null, "kevlar.execution.outcome");
            return result;
        }
        catch (Exception exception)
        {
            scope.Complete(success: false, exception, "kevlar.execution.outcome");
            throw;
        }
    }

    public static T Execute<T, TState>(string? shieldName, TState state, Func<TState, T> action)
    {
        using var scope = Start("kevlar.execute", shieldName);
        try
        {
            var result = action(state);
            scope.Complete(success: true, exception: null, "kevlar.execution.outcome");
            return result;
        }
        catch (Exception exception)
        {
            scope.Complete(success: false, exception, "kevlar.execution.outcome");
            throw;
        }
    }

    public static async ValueTask<Outcome<T>> ExecuteOutcomeAsync<T, TState>(
        string? shieldName, TState state, Func<TState, ValueTask<Outcome<T>>> action)
    {
        using var scope = Start("kevlar.execute", shieldName);
        try
        {
            var outcome = await action(state).ConfigureAwait(false);
            scope.Complete(outcome.IsSuccess, outcome.Exception, "kevlar.execution.outcome");
            return outcome;
        }
        catch (Exception exception)
        {
            scope.Complete(success: false, exception, "kevlar.execution.outcome");
            throw;
        }
    }

    public static Outcome<T> ExecuteOutcome<T, TState>(
        string? shieldName, TState state, Func<TState, Outcome<T>> action)
    {
        using var scope = Start("kevlar.execute", shieldName);
        try
        {
            var outcome = action(state);
            scope.Complete(outcome.IsSuccess, outcome.Exception, "kevlar.execution.outcome");
            return outcome;
        }
        catch (Exception exception)
        {
            scope.Complete(success: false, exception, "kevlar.execution.outcome");
            throw;
        }
    }

    public static async ValueTask<Outcome<T>> AttemptAsync<T, TState>(
        KevlarContext context, string strategyName, TState state, Func<TState, ValueTask<Outcome<T>>> action)
    {
        // Starting inside the async method contains Activity.Current changes even when the work suspends.
        using var scope = Start("kevlar.attempt", context.ShieldName, strategyName,
            context.StrategyIndex, context.AttemptNumber);
        try
        {
            var outcome = await action(state).ConfigureAwait(false);
            scope.Complete(outcome.IsSuccess, outcome.Exception, "kevlar.attempt.outcome");
            return outcome;
        }
        catch (Exception exception)
        {
            scope.Complete(success: false, exception, "kevlar.attempt.outcome");
            throw;
        }
    }

    private static ActivitySource? CreateSource()
    {
        try
        {
            return new ActivitySource(KevlarDiagnostics.ActivitySourceName);
        }
        catch
        {
            // A throwing global ShouldListenTo callback must not poison execution via a type initializer.
            return null;
        }
    }

    private static Activity? FindActivity()
    {
        for (var activity = Activity.Current; activity is not null; activity = activity.Parent)
        {
            if (ReferenceEquals(activity.Source, Source) && activity.Duration == TimeSpan.Zero)
            {
                return activity;
            }
        }
        return null;
    }

    private static Scope Start(string name, string? shieldName, string? strategyName = null,
        int strategyIndex = -1, int attemptNumber = 0)
    {
        var parent = Activity.Current;
        try
        {
            var activity = Source?.StartActivity(name, ActivityKind.Internal);
            if (activity is { IsAllDataRequested: true })
            {
                if (shieldName is not null)
                {
                    activity.SetTag("kevlar.shield.name", Bound(shieldName));
                }
                if (strategyName is not null)
                {
                    activity.SetTag("kevlar.strategy.name", Bound(strategyName));
                    activity.SetTag("kevlar.strategy.index", strategyIndex);
                    activity.SetTag("kevlar.attempt.number", attemptNumber);
                }
            }
            return new Scope(activity, parent);
        }
        catch
        {
            // ActivityStarted may throw after setting Activity.Current. Clean up only our new activity.
            var activity = Activity.Current;
            if (!ReferenceEquals(activity, parent) && ReferenceEquals(activity?.Source, Source))
            {
                try
                {
                    activity?.Stop();
                }
                catch
                {
                    // The listener can also fail while stopping its partially started activity.
                }
            }
            Activity.Current = parent;
            return default;
        }
    }

    private static string? Bound(string? value)
    {
        const int maximumLength = 256;
        if (value is null || value.Length <= maximumLength)
        {
            return value;
        }
        var length = char.IsHighSurrogate(value[maximumLength - 1]) && char.IsLowSurrogate(value[maximumLength])
            ? maximumLength - 1 : maximumLength;
        return value.Substring(0, length);
    }

    private readonly struct Scope(Activity? activity, Activity? parent) : IDisposable
    {
        public void Complete(bool success, Exception? exception, string outcomeTag)
        {
            if (activity is not { IsAllDataRequested: true })
            {
                return;
            }
            try
            {
                var outcome = success ? "success" : "failure";
                if (!success && exception is OperationCanceledException)
                {
                    outcome = "cancelled";
                }
                activity.SetTag(outcomeTag, outcome);
                if (!success)
                {
                    activity.SetStatus(ActivityStatusCode.Error);
                }
                if (exception is not null)
                {
                    activity.SetTag("exception.type", Bound(exception.GetType().FullName));
                }
            }
            catch
            {
                // Never allow diagnostic enrichment to replace the result or exception.
            }
        }

        public void Dispose()
        {
            if (activity is null)
            {
                return;
            }
            try
            {
                activity.Stop();
            }
            catch
            {
                // Listener failures cannot change protected outcomes or leak ambient state.
            }
            finally
            {
                Activity.Current = parent;
            }
        }
    }
#endif
}
