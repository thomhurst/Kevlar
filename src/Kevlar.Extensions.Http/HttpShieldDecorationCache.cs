using System.Runtime.CompilerServices;

namespace Kevlar.Extensions.Http;

// Each handler owns its cache, keeping decorator services and client names in their original scope.
internal sealed class HttpShieldDecorationCache(
    Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>> decorate)
{
    private readonly ConditionalWeakTable<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>> _shields = new();
    private readonly object _gate = new();

    public Shield<HttpResponseMessage> Decorate(Shield<HttpResponseMessage> shield)
    {
        // Reloading wrappers keep their identity when their underlying snapshot changes.
        var snapshot = shield.CurrentSnapshot;
        if (_shields.TryGetValue(snapshot, out var cached))
        {
            return cached;
        }

        // ConditionalWeakTable factories may run concurrently. Serialize misses so decorators
        // with side effects (such as attaching telemetry listeners) run only once per snapshot.
        lock (_gate)
        {
            if (_shields.TryGetValue(snapshot, out cached))
            {
                return cached;
            }

            var decorated = decorate(snapshot);
            _shields.Add(snapshot, decorated);
            return decorated;
        }
    }
}
