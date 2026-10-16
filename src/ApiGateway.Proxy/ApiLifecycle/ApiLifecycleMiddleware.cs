using System.Diagnostics;
using System.Globalization;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Http;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Proxy.ApiLifecycle;

/// <summary>
/// Proxy-pipeline middleware applying <see cref="ApiLifecyclePolicy"/>: a retired version is answered at the
/// gateway with 410 <c>gateway.api_version_retired</c> and a link to the migration notes (header and
/// <c>migration</c> member); a deprecated one gets <c>Deprecation</c>, <c>Sunset</c> and <c>Link</c> on every
/// response, the backend's and the gateway's own.
/// </summary>
public sealed class ApiLifecycleMiddleware(
    RequestDelegate next,
    RoutePolicyResolver<ApiLifecyclePolicy> policies,
    IProblemResponseWriter writer,
    TimeProvider time)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var policy = policies.Resolve(context);
        if (ReferenceEquals(policy, ApiLifecyclePolicy.None))
        {
            await next(context);
            return;
        }

        if (policy.Version is not null)
        {
            Activity.Current?.SetTag(GatewayTelemetry.Tags.ApiVersion, policy.Version);
        }

        if (policy.IsRetired(time.GetUtcNow()))
        {
            AddLink(context.Response.Headers, policy);
            await writer.WriteAsync(context, GatewayErrors.ApiVersionRetired, new ProblemWriteArgs
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["migration"] = policy.Link?.AbsoluteUri },
            });
            return;
        }

        if (policy.HasHeaders)
        {
            context.Response.OnStarting(static state =>
            {
                var (ctx, lifecycle) = ((HttpContext, ApiLifecyclePolicy))state;
                Apply(ctx.Response.Headers, lifecycle);
                return Task.CompletedTask;
            }, (context, policy));
        }

        await next(context);
    }

    internal static void Apply(IHeaderDictionary headers, ApiLifecyclePolicy policy)
    {
        if (policy.Deprecation is { } deprecation)
        {
            // RFC 9745: structured-field date, seconds since the epoch.
            headers[GatewayHeaders.Deprecation] = "@" + deprecation.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        if (policy.Sunset is { } sunset)
        {
            headers[GatewayHeaders.Sunset] = sunset.ToString("R", CultureInfo.InvariantCulture);
        }

        AddLink(headers, policy);
    }

    private static void AddLink(IHeaderDictionary headers, ApiLifecyclePolicy policy)
    {
        if (policy.Link is not { } link)
        {
            return;
        }

        var rel = policy.Sunset is not null ? "\"deprecation sunset\"" : "\"deprecation\"";
        headers.Append(GatewayHeaders.Link, $"<{link.AbsoluteUri}>; rel={rel}");
    }
}
