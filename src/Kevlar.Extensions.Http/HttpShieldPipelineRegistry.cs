namespace Kevlar.Extensions.Http;

internal sealed class HttpShieldPipelineRegistry(IServiceProvider services) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<(HttpShieldPipelineRegistration Registration, string? ClientName), object> _pipelines = [];
    private bool _disposed;

    public IServiceProvider Services { get; } = services;

    public T GetOrAdd<T>(
        HttpShieldPipelineRegistration registration,
        string? clientName,
        Func<T> factory)
        where T : class
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HttpShieldPipelineRegistry));
            }
            if (_pipelines.TryGetValue((registration, clientName), out var existing))
            {
                return (T)existing;
            }

            var pipeline = factory();
            _pipelines.Add((registration, clientName), pipeline);
            return pipeline;
        }
    }

    public void Dispose()
    {
        IDisposable[] pipelines;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pipelines = _pipelines.Values.OfType<IDisposable>().ToArray();
            _pipelines.Clear();
        }

        foreach (var pipeline in pipelines)
        {
            pipeline.Dispose();
        }
    }
}

// Reference identity keeps repeated shield registrations on the same client independent.
internal sealed class HttpShieldPipelineRegistration
{
}
