using System.Runtime.CompilerServices;
using ApiGateway.Abstractions.Routing;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// Resolves a feature's typed per-route policy for the current request. Policies are parsed once per
/// <see cref="RouteConfig"/> instance: YARP creates a new instance whenever a route changes, so the cache
/// never serves a stale policy and old entries are collected with their config.
/// </summary>
/// <typeparam name="TPolicy">The feature's per-route policy.</typeparam>
public sealed class RoutePolicyResolver<TPolicy>(IRouteMetadataParser<TPolicy> parser) : IRoutePolicyResolver<TPolicy>
    where TPolicy : class
{
    private readonly ConditionalWeakTable<RouteConfig, TPolicy> _cache = [];
    private readonly Lazy<TPolicy> _default = new(() => parser.Parse(metadata: null));

    public TPolicy Default => _default.Value;

    public TPolicy Resolve(HttpContext context)
    {
        var route = RouteContext.FindRoute(context);
        return route is null ? Default : Resolve(route.Config);
    }

    public TPolicy Resolve(RouteConfig route) =>
        _cache.GetValue(route, config => parser.Parse(config.Metadata));
}
