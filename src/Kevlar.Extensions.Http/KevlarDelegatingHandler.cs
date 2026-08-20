namespace Kevlar.Extensions.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> that sends requests through a Kevlar policy.
/// </summary>
/// <remarks>
/// Retried and hedged requests resend the same <see cref="HttpRequestMessage"/>. This is safe for
/// requests without content and for rewindable content (for example <see cref="StringContent"/> or
/// <see cref="ByteArrayContent"/>), but streamed one-shot content cannot be resent.
/// </remarks>
public sealed class KevlarDelegatingHandler : DelegatingHandler
{
    private readonly Policy<HttpResponseMessage> _policy;

    /// <summary>Creates the handler with the policy every request flows through.</summary>
    public KevlarDelegatingHandler(Policy<HttpResponseMessage> policy)
        => _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _policy.ExecuteAsync(
            (Handler: this, Request: request),
            static (state, token) => new ValueTask<HttpResponseMessage>(state.Handler.BaseSendAsync(state.Request, token)),
            cancellationToken).AsTask();

    private Task<HttpResponseMessage> BaseSendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        base.SendAsync(request, cancellationToken);
}
