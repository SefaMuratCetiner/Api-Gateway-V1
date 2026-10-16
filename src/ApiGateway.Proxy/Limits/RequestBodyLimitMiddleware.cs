using ApiGateway.Abstractions.Errors;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace ApiGateway.Proxy.Limits;

/// <summary>
/// Proxy-pipeline middleware: a request whose <c>Content-Length</c> exceeds the route's body limit (YARP
/// <c>MaxRequestBodySize</c>, else the global cap) is answered with 413 before the body is read and before the
/// backend is contacted, so with <c>Expect: 100-continue</c> the client never uploads it. Chunked bodies are cut off by
/// Kestrel at the same limit while streaming (413 as well, via the forwarder error mapping).
/// </summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next, IProblemResponseWriter writer)
{
    public Task InvokeAsync(HttpContext context)
    {
        var limit = EffectiveLimit(context);
        return limit is { } max && context.Request.ContentLength > max
            ? writer.WriteAsync(context, GatewayErrors.PayloadTooLarge).AsTask()
            : next(context);
    }

    /// <summary>The body limit for this request, or <c>null</c> when unlimited.</summary>
    internal static long? EffectiveLimit(HttpContext context)
    {
        if (RouteContext.FindRoute(context)?.Config.MaxRequestBodySize is { } routeLimit)
        {
            return routeLimit < 0 ? null : routeLimit;
        }

        return context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
    }
}
