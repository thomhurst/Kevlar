namespace Kevlar.Extensions.Http;

internal sealed class HttpShieldPipeline
{
    private readonly object _endpointGate = new();
    private readonly Dictionary<string, Lazy<Shield<HttpResponseMessage>>> _endpointShields = [];
    private long _routingSequence;

    public HttpShieldPipeline(
        Shield<HttpResponseMessage> policy,
        ShieldHttpHandlerOptions options)
    {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        Options = new HttpShieldPipelineOptions(options);
        ValidateOptions(Options);
    }

    public Shield<HttpResponseMessage> Policy { get; }

    public HttpShieldPipelineOptions Options { get; }

    public Shield<HttpResponseMessage>? GetEndpointShield(
        Uri endpoint,
        Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>>? decorator = null)
    {
        var factory = Options.Routing?.ShieldFactory;
        if (factory is null)
        {
            return null;
        }

        var authority = endpoint.GetLeftPart(UriPartial.Authority);
        Lazy<Shield<HttpResponseMessage>> creation;
        lock (_endpointGate)
        {
            if (!_endpointShields.TryGetValue(authority, out creation!))
            {
                creation = new Lazy<Shield<HttpResponseMessage>>(
                    () =>
                    {
                        var shield = factory(endpoint)
                            ?? throw new InvalidOperationException("The endpoint shield factory returned null.");
                        return decorator is null ? shield : decorator(shield);
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _endpointShields.Add(authority, creation);
            }
        }

        try
        {
            return creation.Value;
        }
        catch
        {
            lock (_endpointGate)
            {
                if (_endpointShields.TryGetValue(authority, out var current)
                    && ReferenceEquals(current, creation))
                {
                    _endpointShields.Remove(authority);
                }
            }

            throw;
        }
    }

    public Uri[]? CreateEndpointOrder()
    {
        var routing = Options.Routing;
        if (routing is null)
        {
            return null;
        }

        if (routing.SelectionMode == HttpEndpointSelectionMode.Ordered)
        {
            return routing.Endpoints.Select(static endpoint => endpoint.Uri).ToArray();
        }

        var sequence = Interlocked.Increment(ref _routingSequence) - 1;
        var seed = unchecked(routing.Seed + ((int)sequence * 0x61C88647));
        var random = new DeterministicRandom(seed);
        return routing.Endpoints
            .Select(endpoint => (
                endpoint.Uri,
                Priority: -Math.Log(random.NextExclusiveDouble()) / endpoint.Weight))
            .OrderBy(static endpoint => endpoint.Priority)
            .Select(static endpoint => endpoint.Uri)
            .ToArray();
    }

    private static void ValidateOptions(HttpShieldPipelineOptions options)
    {
        if (!Enum.IsDefined(typeof(HttpContentReplayPolicy), options.ContentReplayPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ContentReplayPolicy is invalid.");
        }

        if (options.MaximumBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumBufferSize must be positive.");
        }

        if (options.Routing is not { } routing)
        {
            return;
        }

        if (!Enum.IsDefined(typeof(HttpEndpointSelectionMode), routing.SelectionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The endpoint selection mode is invalid.");
        }

        if (routing.Endpoints.Length == 0)
        {
            throw new ArgumentException("Routing requires at least one endpoint.", nameof(options));
        }

        if (routing.Endpoints.Any(static endpoint => endpoint is null))
        {
            throw new ArgumentException("Routing endpoints cannot contain null.", nameof(options));
        }
    }

    private struct DeterministicRandom(int seed)
    {
        private uint _state = unchecked((uint)seed) + 0x9E3779B9u;

        public double NextExclusiveDouble()
        {
            var value = NextUInt32();
            return (value + 1d) / (uint.MaxValue + 2d);
        }

        private uint NextUInt32()
        {
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value == 0 ? 0xA341316Cu : value;
            return _state;
        }
    }
}
