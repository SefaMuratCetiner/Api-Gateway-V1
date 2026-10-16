using ApiGateway.Abstractions.Http;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Cors;

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
