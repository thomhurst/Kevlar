namespace Kevlar.Extensions.Http;

/// <summary>Controls how request content is made replayable for retries and hedges.</summary>
public enum HttpContentReplayPolicy
{
    /// <summary>Do not buffer content. A second send fails before reaching the transport.</summary>
    NoBuffer,

    /// <summary>Buffer content once, up to <see cref="ShieldHttpHandlerOptions.MaximumBufferSize"/>.</summary>
    Buffer,
}
