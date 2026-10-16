using System.Text.RegularExpressions;
using ApiGateway.Abstractions.Routing;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Proxy.Tenancy;

/// <summary>
/// Tenant-specific destinations: a destination whose <c>Tenancy.Tenants</c> metadata lists tenants (comma-separated) serves
/// only them; destinations without it form the shared pool. A tenant with dedicated destinations is sent only to
/// those (never to the shared pool or another tenant's); every other request goes to the shared pool.
/// </summary>
public static partial class TenantDestinations
{
    public const string MetadataPrefix = "Tenancy.";
    public const string TenantsKey = "Tenancy.Tenants";

    private static readonly IReadOnlySet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal) { TenantsKey };

    /// <summary>Key used before the <c>Tenancy.</c> prefix; rejected so old configuration fails loudly.</summary>
    private static readonly Dictionary<string, string> RenamedKeys = new(StringComparer.Ordinal) { ["Tenant"] = TenantsKey };

    public static IReadOnlyList<string> TenantsOf(DestinationState destination) =>
        RouteMetadata.GetList(destination.Model?.Config.Metadata, TenantsKey);

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
        RouteMetadata.ValidateKnownKeys(metadata, MetadataPrefix, KnownKeys, problems);
        RouteMetadata.ValidateRenamedKeys(metadata, RenamedKeys, problems);
        if (RouteMetadata.Get(metadata, TenantsKey) is not null)
        {
            problems.AddRange(RouteMetadata.GetList(metadata, TenantsKey)
                .Where(static tenant => !TenantId().IsMatch(tenant))
                .Select(static tenant => $"'{TenantsKey}' contains invalid tenant '{tenant}'"));
        }

        return problems;
    }

    private static bool Serves(DestinationState destination, string tenant) =>
        TenantsOf(destination).Contains(tenant, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 50)]
    private static partial Regex TenantId();
}
