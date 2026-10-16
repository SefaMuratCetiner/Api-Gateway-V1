using ApiGateway.Proxy.Aggregation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Proxy.Listeners;

/// <summary>
/// Runs after routing. A request on the internal listener that matched a proxy route or a composite endpoint is
/// turned into a plain 404 (no endpoint), so API traffic can never enter through the operational port, even for
/// routes whose <c>Hosts</c> match any port.
/// </summary>
public sealed class InternalListenerGuardMiddleware(RequestDelegate next, IOptions<ListenerOptions> options)
{
    private readonly ListenerOptions _listeners = options.Value;

    public Task InvokeAsync(HttpContext context)
    {
        if (_listeners.IsInternal(context) && context.GetEndpoint()?.Metadata is { } metadata
            && (metadata.GetMetadata<RouteModel>() is not null || metadata.GetMetadata<AggregateRouteMetadata>() is not null))
        {
            context.SetEndpoint(null);
        }

        return next(context);
    }
}
