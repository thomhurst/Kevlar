namespace Kevlar.Extensions.Http;

internal sealed class HttpShieldPipelineRegistry(IServiceProvider services) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<HttpShieldPipelineRegistration, object> _pipelines = [];
    private bool _disposed;

    public IServiceProvider Services { get; } = services;

    public T GetOrAdd<T>(HttpShieldPipelineRegistration registration, Func<T> factory)
        where T : class
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HttpShieldPipelineRegistry));
            }
            if (_pipelines.TryGetValue(registration, out var existing))
            {
                return (T)existing;
            }

            var pipeline = factory();
            _pipelines.Add(registration, pipeline);
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

internal sealed class HttpShieldPipelineRegistration(string clientName)
{
    public string ClientName { get; } = clientName;
}
