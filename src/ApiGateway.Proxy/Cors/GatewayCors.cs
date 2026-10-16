using ApiGateway.Abstractions.Http;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

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

public sealed class CorsPolicyOptions
{
    /// <summary>
    /// Explicit origins (<c>https://app.example.com</c>), wildcard subdomains (<c>https://*.example.com</c>), or
    /// <c>*</c> (any origin; not allowed together with credentials).
    /// </summary>
    public string[] Origins { get; set; } = [];

    public string[] Methods { get; set; } = [];

    /// <summary>Request headers allowed beyond the CORS-safelisted ones.</summary>
    public string[] Headers { get; set; } = [];

    /// <summary>Response headers exposed to scripts, in addition to the gateway's own (correlation, trace, lifecycle, Retry-After).</summary>
    public string[] ExposedHeaders { get; set; } = [];

    public bool AllowCredentials { get; set; }

    /// <summary>How long browsers may cache a preflight answer (<c>Access-Control-Max-Age</c>).</summary>
    public TimeSpan PreflightMaxAge { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Rejects <c>*</c> with credentials, malformed origins and empty policies.</summary>
public sealed class GatewayCorsOptionsValidator : IValidateOptions<GatewayCorsOptions>
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

/// <summary>Builds ASP.NET Core CORS policies from <see cref="GatewayCorsOptions"/>.</summary>
public sealed class GatewayCorsSetup(IOptions<GatewayCorsOptions> gateway) : IConfigureOptions<CorsOptions>
{
    /// <summary>Gateway headers scripts may always read.</summary>
    public static readonly string[] GatewayExposedHeaders =
    [
        GatewayHeaders.CorrelationId, GatewayHeaders.TraceId, GatewayHeaders.RetryAfter,
        GatewayHeaders.Deprecation, GatewayHeaders.Sunset, GatewayHeaders.Link,
    ];

    public void Configure(CorsOptions options)
    {
        foreach (var (name, policy) in gateway.Value.Policies)
        {
            options.AddPolicy(name, builder =>
            {
                if (policy.Origins.Contains("*"))
                {
                    builder.AllowAnyOrigin();
                }
                else
                {
                    builder.WithOrigins(policy.Origins);
                    if (policy.Origins.Any(static origin => origin.Contains("://*.", StringComparison.Ordinal)))
                    {
                        builder.SetIsOriginAllowedToAllowWildcardSubdomains();
                    }
                }

                builder.WithMethods(policy.Methods)
                    .WithExposedHeaders([.. GatewayExposedHeaders, .. policy.ExposedHeaders])
                    .SetPreflightMaxAge(policy.PreflightMaxAge);

                if (policy.Headers.Length > 0)
                {
                    builder.WithHeaders(policy.Headers);
                }

                if (policy.AllowCredentials)
                {
                    builder.AllowCredentials();
                }
                else
                {
                    builder.DisallowCredentials();
                }
            });
        }
    }
}

/// <summary>
/// On routes with a gateway CORS policy, removes <c>Access-Control-*</c> headers a backend sent, so clients see only
/// the gateway's (set when the response starts) instead of conflicting duplicates.
/// </summary>
public sealed class UpstreamCorsHeaderTransformProvider : ITransformProvider
{
    private const string Prefix = "Access-Control-";

    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        var policy = context.Route.CorsPolicy;
        if (string.IsNullOrEmpty(policy) || string.Equals(policy, "disable", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        context.AddResponseTransform(static transformContext =>
        {
            if (transformContext.ProxyResponse is not null)
            {
                var headers = transformContext.HttpContext.Response.Headers;
                foreach (var name in headers.Keys.Where(static key => key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    headers.Remove(name);
                }
            }

            return ValueTask.CompletedTask;
        });
    }
}
