using System.Collections;
using System.Reflection;

namespace Kevlar.Tests;

/// <summary>
/// Rotates a named client's primary handler in <see cref="IHttpClientFactory"/> without waiting out
/// its lifetime.
/// </summary>
/// <remarks>
/// The factory has no clock seam, and its minimum handler lifetime is one second. Each active entry
/// owns the timer that expires it, and that timer's callback, <c>Timer_Tick</c>, disposes the timer
/// and hands the entry to the factory's own expiry path. Calling it runs the same rotation now. If a
/// later <c>Microsoft.Extensions.Http</c> changes those internals, the caller falls back to waiting.
/// </remarks>
internal static class HttpHandlerRotation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Expires the active handler for <paramref name="name"/>, or waits briefly when that isn't possible.</summary>
    public static async Task ExpireOrDelayAsync(IHttpClientFactory factory, string name)
    {
        if (!TryExpire(factory, name))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static bool TryExpire(IHttpClientFactory factory, string name)
    {
        if (factory.GetType().GetField("_activeHandlers", Instance)?.GetValue(factory) is not IDictionary active
            || active[name] is not { } entry)
        {
            return false;
        }

        var handlerEntry = entry.GetType().GetProperty("Value")?.GetValue(entry);
        var tick = handlerEntry?.GetType().GetMethod("Timer_Tick", Instance, Type.EmptyTypes);
        if (tick is null)
        {
            return false;
        }

        tick.Invoke(handlerEntry, null);
        return !ReferenceEquals(active[name], entry);
    }
}
