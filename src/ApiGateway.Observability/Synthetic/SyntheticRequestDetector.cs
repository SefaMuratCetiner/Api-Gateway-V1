using System.Diagnostics;
using ApiGateway.Abstractions.Features;
using ApiGateway.Abstractions.Http;
using ApiGateway.Abstractions.Telemetry;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Observability.Synthetic;

/// <summary>
/// Marks requests carrying a valid <c>X-Synthetic-Check</c> signature as synthetic (excluded from usage
/// analytics, labeled in metrics and traces). The header is always removed before forwarding, valid or not.
/// </summary>
public sealed class SyntheticRequestDetector(RequestDelegate next, SyntheticSignature? signature = null)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Request.Headers;
        if (headers.TryGetValue(GatewayHeaders.SyntheticCheck, out var value))
        {
            headers.Remove(GatewayHeaders.SyntheticCheck);
            if (signature?.Verify(value.ToString()) is { } probe)
            {
                context.GetGatewayRequest().IsSynthetic = true;
                Activity.Current?.SetTag(GatewayTelemetry.Tags.Synthetic, true);
                Activity.Current?.SetTag(GatewayTelemetry.Tags.Probe, probe);
            }
        }

        return next(context);
    }
}
