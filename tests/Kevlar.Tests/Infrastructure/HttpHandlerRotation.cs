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
/// later <c>Microsoft.Extensions.Http</c> changes those internals, <see cref="Expire"/> throws, and
/// every test that rotates handlers fails until this helper is updated.
/// </remarks>
internal static class HttpHandlerRotation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Runs the factory's own expiry for <paramref name="name"/>'s active handler, so the next
    /// <see cref="IHttpClientFactory.CreateClient(string)"/> builds a new one.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="name"/> has no active handler yet, or the factory's internals no longer match.
    /// </exception>
    internal static void Expire(IHttpClientFactory factory, string name)
    {
        if (factory.GetType().GetField("_activeHandlers", Instance)?.GetValue(factory) is not IDictionary active)
        {
            throw InternalsChanged($"{factory.GetType().Name} has no _activeHandlers dictionary");
        }

        if (active[name] is not { } entry)
        {
            throw new InvalidOperationException($"'{name}' has no active handler to expire. Create a client first.");
        }

        var handlerEntry = entry.GetType().GetProperty("Value")?.GetValue(entry);
        var tick = handlerEntry?.GetType().GetMethod("Timer_Tick", Instance, Type.EmptyTypes)
            ?? throw InternalsChanged("the active handler entry has no Timer_Tick method");

        // Let an exception from the factory's own expiry surface as itself, not as a reflection wrapper.
        tick.Invoke(handlerEntry, BindingFlags.DoNotWrapExceptions, binder: null, parameters: null, culture: null);
        if (ReferenceEquals(active[name], entry))
        {
            throw InternalsChanged("Timer_Tick left the active handler in place");
        }
    }

    private static InvalidOperationException InternalsChanged(string detail) =>
        new($"Microsoft.Extensions.Http's internals changed: {detail}. Update {nameof(HttpHandlerRotation)}.");
}
