using ApiGateway.Abstractions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Listeners;

/// <summary><see cref="IListenerClassifier"/> backed by <see cref="ListenerOptions"/>.</summary>
public sealed class ListenerClassifier(IOptions<ListenerOptions> options) : IListenerClassifier
{
    private readonly ListenerOptions _listeners = options.Value;

    public bool IsInternal(HttpContext context) => _listeners.IsInternal(context);
}
