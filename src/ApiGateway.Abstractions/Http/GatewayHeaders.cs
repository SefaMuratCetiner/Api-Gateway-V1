namespace ApiGateway.Abstractions.Http;

/// <summary>Header names used across the gateway.</summary>
public static class GatewayHeaders
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string RequestId = "X-Request-ID";
    public const string TraceId = "X-Trace-Id";
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";
    public const string TraceResponse = "traceresponse";
    public const string Baggage = "baggage";

    public const string RetryAfter = "Retry-After";
    public const string IdempotencyKey = "Idempotency-Key";
    public const string ApiKey = "X-Api-Key";

    /// <summary>HMAC-signed marker on the gateway's own synthetic-check requests.</summary>
    public const string SyntheticCheck = "X-Synthetic-Check";
}
