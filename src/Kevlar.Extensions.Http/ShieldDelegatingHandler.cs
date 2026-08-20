namespace Kevlar.Extensions.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> that sends requests through a Kevlar shield.
/// </summary>
/// <remarks>
/// Retried and hedged requests resend the same <see cref="HttpRequestMessage"/>. This is safe for
/// requests without content and for rewindable content (for example <see cref="StringContent"/> or
/// <see cref="ByteArrayContent"/>), but streamed one-shot content cannot be resent.
/// </remarks>
public sealed class ShieldDelegatingHandler : DelegatingHandler
{
    private readonly Shield<HttpResponseMessage> _policy;

    /// <summary>Creates the handler with the shield every request flows through.</summary>
    public ShieldDelegatingHandler(Shield<HttpResponseMessage> shield)
        => _policy = shield ?? throw new ArgumentNullException(nameof(shield));

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _policy.ExecuteAsync(
            (Handler: this, Request: request),
            static (state, token) => new ValueTask<HttpResponseMessage>(state.Handler.BaseSendAsync(state.Request, token)),
            cancellationToken).AsTask();

    private Task<HttpResponseMessage> BaseSendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        base.SendAsync(request, cancellationToken);
}
