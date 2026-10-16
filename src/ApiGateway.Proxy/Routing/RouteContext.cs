using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Proxy.Routing;

/// <summary>Helpers to find the YARP route of a request, before or inside the proxy pipeline.</summary>
public static class RouteContext
{
    /// <summary>
    /// The matched YARP route. Available as soon as routing has selected the endpoint, i.e. also in
    /// middleware that runs after <c>UseRouting</c> but outside the proxy pipeline.
    /// </summary>
    public static RouteModel? FindRoute(HttpContext context) =>
        context.Features.Get<IReverseProxyFeature>()?.Route
        ?? context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>();
}
