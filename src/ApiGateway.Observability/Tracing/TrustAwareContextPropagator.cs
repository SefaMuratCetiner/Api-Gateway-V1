using System.Diagnostics;
using ApiGateway.Abstractions.Features;
using ApiGateway.Abstractions.Http;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Observability.Tracing;

/// <summary>
/// The propagator ASP.NET Core hosting uses to extract the inbound trace context when it creates the request
/// span. For untrusted peers it extracts nothing, so the gateway span starts a new trace, and drops inbound
/// baggage; the inbound <c>traceparent</c> is parked on the request for <see cref="InboundTraceLinkMiddleware"/>.
/// Injection (outbound) is delegated unchanged.
/// </summary>
/// <remarks>
/// Hosting resolves <see cref="DistributedContextPropagator"/> from DI, so the composition root registers this
/// instance there. The static <see cref="DistributedContextPropagator.Current"/> used by outbound HTTP is not
/// replaced.
/// </remarks>
public sealed class TrustAwareContextPropagator(
    DistributedContextPropagator inner,
    TraceTrustEvaluator trust,
    IHttpContextAccessor accessor) : DistributedContextPropagator
{
    public override IReadOnlyCollection<string> Fields => inner.Fields;

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter) =>
        inner.Inject(activity, carrier, setter);

    public override void ExtractTraceIdAndState(object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState)
    {
        if (trust.IsTrusted(accessor.HttpContext))
        {
            inner.ExtractTraceIdAndState(carrier, getter, out traceId, out traceState);
            return;
        }

        traceId = null;
        traceState = null;

        if (getter is null || accessor.HttpContext is not { } context)
        {
            return;
        }

        getter(carrier, GatewayHeaders.TraceParent, out var inboundParent, out _);
        if (!string.IsNullOrEmpty(inboundParent))
        {
            getter(carrier, GatewayHeaders.TraceState, out var inboundState, out _);
            context.Features.Set<IUntrustedTraceContextFeature>(new UntrustedTraceContextFeature(inboundParent, inboundState));
        }
    }

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) =>
        trust.IsTrusted(accessor.HttpContext) ? inner.ExtractBaggage(carrier, getter) : null;
}
