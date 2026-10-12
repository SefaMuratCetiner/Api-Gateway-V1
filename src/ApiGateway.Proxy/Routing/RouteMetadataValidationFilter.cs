using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// YARP config filter that runs every <see cref="IRouteMetadataValidator"/> against every route. Throwing
/// makes YARP reject the configuration: at startup the gateway does not start, on reload the running
/// configuration stays in place.
/// </summary>
public sealed class RouteMetadataValidationFilter(IEnumerable<IRouteMetadataValidator> validators) : IProxyConfigFilter
{
    private readonly IRouteMetadataValidator[] _validators = [.. validators];

    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel) =>
        ValueTask.FromResult(cluster);

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        var problems = new List<string>();
        foreach (var validator in _validators)
        {
            problems.AddRange(validator.Validate(route.Metadata).Select(problem => $"{validator.Prefix}*: {problem}"));
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Route '{route.RouteId}' has invalid metadata: {string.Join("; ", problems)}");
        }

        return ValueTask.FromResult(route);
    }
}
