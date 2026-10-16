using ApiGateway.Abstractions.Errors;
using Yarp.ReverseProxy.Transforms;

namespace ApiGateway.Proxy.Transforms;

/// <summary>
/// Replaces a backend response that must not reach the client (unsafe redirect, over the size limit) with a gateway
/// problem, from inside a response transform: the backend's headers are dropped, its body is not copied, and the
/// response no longer counts as a backend response.
/// </summary>
public static class UpstreamResponseBlocker
{
    public static async ValueTask BlockAsync(ResponseTransformContext context, IProblemResponseWriter writer, GatewayError error)
    {
        context.SuppressResponseBody = true;

        var http = context.HttpContext;
        http.Response.Headers.Clear();
        http.Features.Set<IUpstreamResponseFeature>(null);
        await writer.WriteAsync(http, error);
    }
}
