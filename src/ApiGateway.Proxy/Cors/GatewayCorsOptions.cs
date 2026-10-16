using ApiGateway.Proxy.Routing;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Cors;

/// <summary>
/// Configuration section <c>Cors</c>: named policies routes select with YARP's <c>CorsPolicy</c>. The gateway answers
/// preflights itself and owns the CORS headers of those routes.
/// </summary>
public sealed class GatewayCorsOptions
{
    public const string SectionName = "Cors";

    public Dictionary<string, CorsPolicyOptions> Policies { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Rejects <c>*</c> with credentials, malformed origins and empty policies.</summary>
public sealed class GatewayCorsOptionsConsistencyValidator : IValidateOptions<GatewayCorsOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayCorsOptions options)
    {
        var failures = new List<string>();
        foreach (var (policyName, policy) in options.Policies)
        {
            var prefix = $"Cors:Policies:{policyName}";
            if (policy.Origins.Length == 0)
            {
                failures.Add($"{prefix}:Origins must list at least one origin.");
            }

            if (policy.Methods.Length == 0)
            {
                failures.Add($"{prefix}:Methods must list the allowed methods.");
            }

            foreach (var origin in policy.Origins)
            {
                if (string.Equals(origin, "*", StringComparison.Ordinal))
                {
                    if (policy.AllowCredentials)
                    {
                        failures.Add($"{prefix}: '*' cannot be combined with AllowCredentials; list the origins.");
                    }
                }
                else if (!IsOrigin(origin))
                {
                    failures.Add($"{prefix}:Origins: '{origin}' is not an origin (scheme://host[:port], optionally with a '*.' subdomain wildcard).");
                }
            }

            if (policy.PreflightMaxAge < TimeSpan.Zero || policy.PreflightMaxAge > TimeSpan.FromDays(1))
            {
                failures.Add($"{prefix}:PreflightMaxAge must be between 0 and 1 day.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsOrigin(string origin)
    {
        var candidate = origin.Replace("://*.", "://wildcard.", StringComparison.Ordinal);
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && HttpUrl.IsHttp(uri)
            && string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal)
            && !candidate.EndsWith('/')
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && string.IsNullOrEmpty(uri.UserInfo);
    }
}
