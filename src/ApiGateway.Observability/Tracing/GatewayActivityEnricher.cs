using System.Diagnostics;
using ApiGateway.Abstractions.Features;
using ApiGateway.Abstractions.Telemetry;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Observability.Tracing;

/// <summary>
/// Adds gateway context to the request's server span when the response is complete: route, cluster, destination,
/// caller, tenant, correlation ID and the gateway error code. Plugged into the ASP.NET Core instrumentation's
/// <c>EnrichWithHttpResponse</c> by the composition root. Span attributes may carry IDs; metric labels may not.
/// </summary>
public static class GatewayActivityEnricher
{
    public static void EnrichWithResponse(Activity activity, HttpResponse response)
    {
        var context = response.HttpContext;

        if (context.FindGatewayRequest() is { } gateway)
        {
            SetIfPresent(activity, GatewayTelemetry.Tags.RouteId, gateway.RouteId);
            SetIfPresent(activity, GatewayTelemetry.Tags.ClusterId, gateway.ClusterId);
            SetIfPresent(activity, GatewayTelemetry.Tags.DestinationId, gateway.DestinationId);
            SetIfPresent(activity, GatewayTelemetry.Tags.ClientId, gateway.ClientId);
            SetIfPresent(activity, GatewayTelemetry.Tags.ClientType, gateway.ClientType);
            SetIfPresent(activity, GatewayTelemetry.Tags.TenantId, gateway.TenantId);
            if (gateway.IsSynthetic)
            {
                activity.SetTag(GatewayTelemetry.Tags.Synthetic, value: true);
            }
        }

        SetIfPresent(activity, GatewayTelemetry.Tags.CorrelationId, context.GetCorrelationId());

        if (context.GetGatewayError() is { } error)
        {
            activity.SetTag(GatewayTelemetry.Tags.ErrorCode, error.Code);
        }

        if (context.Features.Get<IUpstreamTimingFeature>().UpstreamWait() is { } wait)
        {
            activity.SetTag("gateway.upstream.wait_ms", wait.TotalMilliseconds);
        }
    }

    private static void SetIfPresent(Activity activity, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            activity.SetTag(name, value);
        }
    }
}
