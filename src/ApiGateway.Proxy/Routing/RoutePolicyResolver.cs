using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// Resolves a feature's typed per-route policy for the current request. Policies are parsed once per
/// <see cref="RouteConfig"/> instance: YARP creates a new instance whenever a route changes, so the cache
/// never serves a stale policy and old entries are collected with their config.
/// </summary>
/// <typeparam name="TPolicy">The feature's per-route policy.</typeparam>
public sealed class RoutePolicyResolver<TPolicy>(IRouteMetadataParser<TPolicy> parser)
    where TPolicy : class
{
    private readonly ConditionalWeakTable<RouteConfig, TPolicy> _cache = [];
    private readonly Lazy<TPolicy> _default = new(() => parser.Parse(metadata: null));

    /// <summary>The policy that applies when no route matched (gateway endpoints, 404s).</summary>
    public TPolicy Default => _default.Value;

    /// <summary>The policy of the route serving <paramref name="context"/>, or <see cref="Default"/>.</summary>
    public TPolicy Resolve(HttpContext context)
    {
        var route = RouteContext.FindRoute(context);
        return route is null ? Default : Resolve(route.Config);
    }

    public TPolicy Resolve(RouteConfig route) =>
        _cache.GetValue(route, config => parser.Parse(config.Metadata));
}

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
