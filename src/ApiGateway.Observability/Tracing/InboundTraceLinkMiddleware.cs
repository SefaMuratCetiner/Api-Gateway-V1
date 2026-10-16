using System.Diagnostics;
using ApiGateway.Abstractions.Features;
using ApiGateway.Abstractions.Http;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Observability.Tracing;

/// <summary>
/// For requests whose trace context was not trusted: links the gateway's new trace to the inbound one (so the
/// caller's trace can still be followed) and strips the inbound trace headers, so nothing untrusted reaches the
/// backend. YARP injects the gateway's own context on the outbound request.
/// </summary>
public sealed class InboundTraceLinkMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Features.Get<IUntrustedTraceContextFeature>() is { } untrusted)
        {
            if (Activity.Current is { } activity
                && ActivityContext.TryParse(untrusted.TraceParent, untrusted.TraceState, isRemote: true, out var inbound))
            {
                activity.AddLink(new ActivityLink(inbound, new ActivityTagsCollection { ["gateway.link.reason"] = "untrusted_inbound_context" }));
            }

            var headers = context.Request.Headers;
            headers.Remove(GatewayHeaders.TraceParent);
            headers.Remove(GatewayHeaders.TraceState);
            headers.Remove(GatewayHeaders.Baggage);
        }

        return next(context);
    }
}
