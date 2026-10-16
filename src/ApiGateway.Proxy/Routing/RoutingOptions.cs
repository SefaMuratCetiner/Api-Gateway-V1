using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// Configuration section <c>Routing</c>: the conventions every route must follow, the hosts the public listener
/// answers for, path canonicalization and destination discovery.
/// </summary>
public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    /// <summary>Every proxied route's path must start with this prefix; nothing else is routed on the public listener.</summary>
    [Required]
    [RegularExpression("^/[A-Za-z0-9._~-]+(/[A-Za-z0-9._~-]+)*$")]
    public string PublicPathPrefix { get; set; } = "/api";

    /// <summary>
    /// Hosts the public listener accepts in <c>Host</c> and <c>X-Forwarded-Host</c>: exact names
    /// (<c>api.example.com</c>) or wildcard subdomains (<c>*.example.com</c>). <c>*</c> (any host) and wildcard
    /// patterns are accepted in Development only. Ports are ignored.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>
    /// Hosts outside <see cref="AllowedHosts"/> a backend redirect (3xx <c>Location</c>) may point to, e.g. an identity
    /// provider. Redirects anywhere else are blocked (open-redirect protection).
    /// </summary>
    public string[] ExternalRedirectHosts { get; set; } = [];

    public PathNormalizationOptions Normalization { get; set; } = new();

    [ValidateObjectMembers]
    public DnsDiscoveryOptions DnsDiscovery { get; set; } = new();
}

/// <summary>How a non-canonical path is handled once it has been canonicalized.</summary>
public enum NonCanonicalPathHandling
{
    /// <summary>The canonical path replaces the request path; routing, authorization and the backend see only it.</summary>
    Rewrite,

    /// <summary>The client is redirected (308) to the canonical path.</summary>
    Redirect,
}

public sealed class PathNormalizationOptions
{
    public NonCanonicalPathHandling Handling { get; set; } = NonCanonicalPathHandling.Rewrite;

    /// <summary>Remove a trailing slash (except on <c>/</c>), so <c>/api/orders/</c> and <c>/api/orders</c> are one resource.</summary>
    public bool RemoveTrailingSlash { get; set; } = true;

    /// <summary>
    /// Lower-case the path. Off by default: route matching is already case-insensitive, and backends may treat
    /// path segments (IDs) case-sensitively.
    /// </summary>
    public bool LowercasePath { get; set; }

    /// <summary>
    /// Accept <c>%2F</c> and <c>%5C</c> inside path segments. Off by default: a backend that decodes them sees a
    /// different path than the one the gateway routed and authorized.
    /// </summary>
    public bool AllowEncodedSlashes { get; set; }
}

/// <summary>
/// Destination discovery through DNS: destination host names (Kubernetes services, Consul DNS, plain DNS) are
/// resolved to one destination per address and refreshed periodically, so the same configuration works locally
/// and deployed.
/// </summary>
public sealed class DnsDiscoveryOptions
{
    public bool Enabled { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan RefreshPeriod { get; set; } = TimeSpan.FromSeconds(30);
}

[OptionsValidator]
public sealed partial class RoutingOptionsValidator : IValidateOptions<RoutingOptions>;

/// <summary>Host patterns must be well-formed; wildcards only where <paramref name="allowWildcardHosts"/> (Development).</summary>
public sealed class RoutingOptionsConsistencyValidator(bool allowWildcardHosts) : IValidateOptions<RoutingOptions>
{
    public ValidateOptionsResult Validate(string? name, RoutingOptions options)
    {
        var failures = new List<string>();
        foreach (var host in options.AllowedHosts)
        {
            if (HostPatterns.Problem(host, allowWildcardHosts) is { } problem)
            {
                failures.Add($"Routing:AllowedHosts: {problem}");
            }
        }

        foreach (var host in options.ExternalRedirectHosts)
        {
            if (HostPatterns.Problem(host, allowWildcard: false) is { } problem)
            {
                failures.Add($"Routing:ExternalRedirectHosts: {problem}");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
