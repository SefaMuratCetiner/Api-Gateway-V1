using ApiGateway.Abstractions.Routing;

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
