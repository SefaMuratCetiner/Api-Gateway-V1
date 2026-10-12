using System.Diagnostics;
using ApiGateway.Abstractions.Context;
using ApiGateway.Abstractions.Telemetry;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// First middleware of the proxy pipeline. Records the matched route and cluster on
/// <see cref="IGatewayRequestFeature"/> and, once the forwarder has picked one, the destination.
/// </summary>
public sealed class RouteContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var proxy = context.GetReverseProxyFeature();
        var gateway = context.GetGatewayRequest();
        gateway.RouteId = proxy.Route.Config.RouteId;
        gateway.ClusterId = proxy.Cluster?.Config.ClusterId;

        var activity = Activity.Current;
        activity?.SetTag(GatewayTelemetry.Tags.RouteId, gateway.RouteId);
        activity?.SetTag(GatewayTelemetry.Tags.ClusterId, gateway.ClusterId);

        try
        {
            await next(context);
        }
        finally
        {
            gateway.DestinationId = proxy.ProxiedDestination?.DestinationId;
            activity?.SetTag(GatewayTelemetry.Tags.DestinationId, gateway.DestinationId);
        }
    }
}
