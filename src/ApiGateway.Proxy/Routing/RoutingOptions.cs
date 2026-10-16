using System.ComponentModel.DataAnnotations;
using ApiGateway.Proxy.Normalization;
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
