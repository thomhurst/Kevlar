namespace Kevlar.Internal;

internal static class EventContext
{
    internal static KevlarContext Required(KevlarContext? context) =>
        context ?? throw new InvalidOperationException(
            "A default strategy event has no execution context.");
}
