using ApiGateway.Proxy.LoadBalancing;
using ApiGateway.Proxy.Tenancy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// YARP config filter enforcing the gateway's routing conventions. Every route needs a path under
/// <see cref="RoutingOptions.PublicPathPrefix"/>, explicit methods (others get 405) and explicit hosts; routes
/// without hosts and wildcard hosts are accepted in Development only. Destination metadata used by the gateway
/// (<c>Weight</c>, <c>Tenant</c>) is validated too. Throwing rejects the configuration: at startup the gateway does
/// not start, on reload the running configuration stays.
/// </summary>
public sealed class ProxyConventionsFilter(IOptions<RoutingOptions> options, IHostEnvironment environment) : IProxyConfigFilter
{
    private readonly string _prefix = options.Value.PublicPathPrefix;
    private readonly bool _allowWildcardHosts = environment.IsDevelopment();

    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
    {
        var problems = new List<string>();
        foreach (var (id, destination) in cluster.Destinations ?? new Dictionary<string, DestinationConfig>(StringComparer.Ordinal))
        {
            problems.AddRange(WeightedLoadBalancingPolicy.Validate(destination.Metadata).Select(problem => $"destination '{id}': {problem}"));
            problems.AddRange(TenantDestinations.Validate(destination.Metadata).Select(problem => $"destination '{id}': {problem}"));
        }

        return problems.Count == 0
            ? ValueTask.FromResult(cluster)
            : throw new InvalidOperationException($"Cluster '{cluster.ClusterId}' is invalid: {string.Join("; ", problems)}");
    }

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        var problems = Validate(route.Match.Path, route.Match.Methods, route.Match.Hosts, _prefix, _allowWildcardHosts);
        return problems.Count == 0
            ? ValueTask.FromResult(route)
            : throw new InvalidOperationException($"Route '{route.RouteId}' breaks the routing conventions: {string.Join("; ", problems)}");
    }

    /// <summary>The checks, shared with aggregation routes (which are not YARP routes).</summary>
    public static IReadOnlyList<string> Validate(
        string? path,
        IReadOnlyList<string>? methods,
        IReadOnlyList<string>? hosts,
        string publicPrefix,
        bool allowWildcardHosts)
    {
        var problems = new List<string>();

        if (string.IsNullOrEmpty(path) || !IsUnderPrefix(path, publicPrefix))
        {
            problems.Add($"Match.Path must start with the public prefix '{publicPrefix}/' (got '{path}')");
        }

        if (methods is not { Count: > 0 })
        {
            problems.Add("Match.Methods must list the allowed methods explicitly");
        }

        if (hosts is not { Count: > 0 })
        {
            if (!allowWildcardHosts)
            {
                problems.Add("Match.Hosts must list the route's hosts explicitly (routes without hosts are allowed in Development only)");
            }
        }
        else
        {
            problems.AddRange(hosts.Select(host => HostPatterns.Problem(host, allowWildcardHosts)).OfType<string>().Select(static problem => "Match.Hosts: " + problem));
        }

        return problems;
    }

    /// <summary>True when <paramref name="path"/> is the prefix itself or below it (segment boundary).</summary>
    public static bool IsUnderPrefix(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && (path.Length == prefix.Length || path[prefix.Length] == '/');
}
