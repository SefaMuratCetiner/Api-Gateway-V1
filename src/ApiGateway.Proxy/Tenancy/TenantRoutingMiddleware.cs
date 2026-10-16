using ApiGateway.Abstractions.Features;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Proxy.Tenancy;

/// <summary>
/// Proxy-pipeline middleware, before load balancing: narrows the available destinations to the request's tenant.
/// The tenant is the one identity set on <see cref="IGatewayRequestFeature.TenantId"/>, otherwise the route value
/// named by <c>Tenancy.RouteValue</c>. Clusters without tenant-specific destinations are left alone.
/// </summary>
public sealed class TenantRoutingMiddleware(RequestDelegate next, RoutePolicyResolver<TenantRoutingPolicy> policies)
{
    public Task InvokeAsync(HttpContext context)
    {
        var proxy = context.GetReverseProxyFeature();
        if (proxy.AllDestinations.Any(static destination => TenantDestinations.TenantsOf(destination).Count > 0))
        {
            proxy.AvailableDestinations = TenantDestinations.Select(proxy.AllDestinations, proxy.AvailableDestinations, ResolveTenant(context, policies.Resolve(context)));
        }

        return next(context);
    }

    internal static string? ResolveTenant(HttpContext context, TenantRoutingPolicy policy)
    {
        if (context.FindGatewayRequest()?.TenantId is { Length: > 0 } identityTenant)
        {
            return identityTenant;
        }

        return policy.RouteValue is { } name && context.Request.RouteValues.TryGetValue(name, out var value) && value?.ToString() is { Length: > 0 } fromRoute
            ? fromRoute
            : null;
    }
}
