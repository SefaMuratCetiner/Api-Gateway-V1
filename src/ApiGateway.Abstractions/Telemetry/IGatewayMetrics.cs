namespace ApiGateway.Abstractions.Telemetry;

/// <summary>Why the gateway turned a request away. Bounded; used as a metric label.</summary>
public enum RejectionReason
{
    RateLimit,
    Quota,
    Token,
    Shed,
    Concurrency,
    Blocked,
    Maintenance,
}

/// <summary>
/// Metrics port for components that reject requests (authentication, rate limiting, load shedding, ...).
/// They report through this interface instead of owning instruments, so every rejection lands in one
/// consistently labelled counter.
/// </summary>
public interface IGatewayMetrics
{
    /// <summary>Counts one rejected request.</summary>
    /// <param name="reason">Why the request was rejected.</param>
    /// <param name="routeId">Route ID, or <c>null</c> if no route matched yet.</param>
    void RecordRejection(RejectionReason reason, string? routeId);
}
