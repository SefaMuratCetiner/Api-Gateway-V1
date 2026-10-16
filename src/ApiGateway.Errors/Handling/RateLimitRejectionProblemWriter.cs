using System.Threading.RateLimiting;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Features;
using ApiGateway.Abstractions.Telemetry;
using Microsoft.AspNetCore.RateLimiting;

namespace ApiGateway.Errors.Handling;

/// <summary>
/// <c>RateLimiterOptions.OnRejected</c> handler: 429 problem details with <c>Retry-After</c> taken from the
/// limiter's lease, and a <c>rate_limit</c> rejection metric. Ready for the traffic-management limiters.
/// </summary>
public sealed class RateLimitRejectionProblemWriter(IProblemResponseWriter writer, IGatewayMetrics metrics)
{
    /// <summary>Used when the limiter does not say when to come back.</summary>
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(1);

    public async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var fromLease)
            ? fromLease
            : DefaultRetryAfter;

        metrics.RecordRejection(RejectionReason.RateLimit, context.HttpContext.FindGatewayRequest()?.RouteId);
        await writer.WriteAsync(context.HttpContext, GatewayErrors.RateLimited, new ProblemWriteArgs { RetryAfter = retryAfter });
    }
}
