using System.Diagnostics;
using ApiGateway.Abstractions.Context;
using ApiGateway.Abstractions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace ApiGateway.Observability.Correlation;

/// <summary>
/// Puts <c>X-Trace-Id</c>, <c>traceresponse</c> (W3C Trace Context level 2) and <c>X-Correlation-Id</c> on every
/// response, including the gateway's own errors and backend responses, just before the headers are sent.
/// The gateway's values replace any a backend sent: the gateway span is the root the client can quote.
/// </summary>
public sealed class TraceResponseHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            Apply(ctx, ResolveActivity(ctx));
            return Task.CompletedTask;
        }, context);

        return next(context);
    }

    internal static void Apply(HttpContext context, Activity? activity)
    {
        var headers = context.Response.Headers;
        if (activity is not null)
        {
            headers[GatewayHeaders.TraceId] = activity.TraceId.ToHexString();
            headers[GatewayHeaders.TraceResponse] = FormatTraceResponse(activity);
        }

        if (context.GetCorrelationId() is { } correlationId)
        {
            headers[GatewayHeaders.CorrelationId] = correlationId;
        }
    }

    /// <summary><c>00-{trace-id}-{span-id}-{flags}</c> of the gateway's server span.</summary>
    internal static string FormatTraceResponse(Activity activity) =>
        string.Create(55, activity, static (span, act) =>
        {
            "00-".CopyTo(span);
            act.TraceId.ToHexString().CopyTo(span[3..]);
            span[35] = '-';
            act.SpanId.ToHexString().CopyTo(span[36..]);
            span[52] = '-';
            span[53] = '0';
            span[54] = act.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? '1' : '0';
        });

    // OnStarting may run while the forwarder's client span is current; report the request's server span.
    private static Activity? ResolveActivity(HttpContext context) =>
        context.Features.Get<IHttpActivityFeature>()?.Activity ?? Activity.Current;
}
