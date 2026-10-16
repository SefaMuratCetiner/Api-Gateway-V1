using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Proxy.LoadBalancing;

/// <summary>
/// Load-balancing policy <c>Weighted</c> for canary and weighted traffic splitting: each available destination
/// receives a share of requests proportional to its <c>Weight</c> metadata (0..10000, default 1; 0 takes no
/// traffic). Example: stable destinations with weight 95 and a canary with weight 5. Combine with session affinity
/// to keep a client on one side.
/// </summary>
public sealed class WeightedLoadBalancingPolicy : ILoadBalancingPolicy
{
    public const string PolicyName = "Weighted";
    public const string WeightKey = "Weight";
    public const int MaxWeight = 10_000;

    public string Name => PolicyName;

    public DestinationState? PickDestination(HttpContext context, ClusterState cluster, IReadOnlyList<DestinationState> availableDestinations) =>
        Pick(availableDestinations, Random.Shared.NextDouble());

    /// <summary>Picks the destination at <paramref name="sample"/> (0..1) of the cumulative weight.</summary>
    internal static DestinationState? Pick(IReadOnlyList<DestinationState> destinations, double sample)
    {
        long total = 0;
        foreach (var destination in destinations)
        {
            total += WeightOf(destination);
        }

        if (total == 0)
        {
            return null;
        }

        var target = sample * total;
        long cumulative = 0;
        foreach (var destination in destinations)
        {
            cumulative += WeightOf(destination);
            if (target < cumulative)
            {
                return destination;
            }
        }

        // Unreachable for samples in [0, 1).
        return null;
    }

    internal static int WeightOf(DestinationState destination) =>
        RouteMetadata.GetInt(destination.Model?.Config.Metadata, WeightKey, fallback: 1);

    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        RouteMetadata.ValidateInt(metadata, WeightKey, 0, MaxWeight, problems);
        return problems;
    }
}
