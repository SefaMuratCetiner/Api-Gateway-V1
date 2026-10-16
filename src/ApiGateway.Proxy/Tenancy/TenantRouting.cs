using System.Text.RegularExpressions;
using ApiGateway.Abstractions.Context;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Proxy.Tenancy;

/// <summary>Per-route tenant routing, read from route metadata.</summary>
/// <param name="RouteValue">Route value that names the tenant when no identity has set one (e.g. <c>tenant</c> for <c>/api/t/{tenant}/**</c>).</param>
public sealed record TenantRoutingPolicy(string? RouteValue);

/// <summary>Route metadata key: <c>Tenancy.RouteValue</c>.</summary>
public sealed class TenantRoutingMetadataParser : IRouteMetadataParser<TenantRoutingPolicy>
{
    public const string RouteValueKey = "Tenancy.RouteValue";

    private static readonly IReadOnlySet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal) { RouteValueKey };

    public string Prefix => "Tenancy.";

    public TenantRoutingPolicy Parse(IReadOnlyDictionary<string, string>? metadata) =>
        new(RouteMetadata.Get(metadata, RouteValueKey));

    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        RouteMetadata.ValidateKnownKeys(metadata, Prefix, KnownKeys, problems);
        return problems;
    }
}

/// <summary>
/// Tenant-specific destinations: a destination whose <c>Tenant</c> metadata lists tenants (comma-separated) serves
/// only them; destinations without it form the shared pool. A tenant with dedicated destinations is sent only to
/// those (never to the shared pool or another tenant's); every other request goes to the shared pool.
/// </summary>
public static partial class TenantDestinations
{
    public const string TenantKey = "Tenant";

    public static IReadOnlyList<string> TenantsOf(DestinationState destination) =>
        RouteMetadata.GetList(destination.Model?.Config.Metadata, TenantKey);

    /// <summary>The destinations <paramref name="tenant"/> may use, out of <paramref name="available"/>.</summary>
    public static IReadOnlyList<DestinationState> Select(IReadOnlyList<DestinationState> all, IReadOnlyList<DestinationState> available, string? tenant)
    {
        var hasDedicated = tenant is not null && all.Any(destination => Serves(destination, tenant));
        return hasDedicated
            ? [.. available.Where(destination => Serves(destination, tenant!))]
            : [.. available.Where(static destination => TenantsOf(destination).Count == 0)];
    }

    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        if (RouteMetadata.Get(metadata, TenantKey) is not null)
        {
            problems.AddRange(RouteMetadata.GetList(metadata, TenantKey)
                .Where(static tenant => !TenantId().IsMatch(tenant))
                .Select(static tenant => $"'{TenantKey}' contains invalid tenant '{tenant}'"));
        }

        return problems;
    }

    private static bool Serves(DestinationState destination, string tenant) =>
        TenantsOf(destination).Contains(tenant, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 50)]
    private static partial Regex TenantId();
}

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
