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

    public const string Forwarded = "Forwarded";
    public const string XForwardedFor = "X-Forwarded-For";
    public const string XForwardedProto = "X-Forwarded-Proto";
    public const string XForwardedHost = "X-Forwarded-Host";
    public const string XForwardedPrefix = "X-Forwarded-Prefix";

    /// <summary>RFC 9745: when a resource was (or will be) deprecated.</summary>
    public const string Deprecation = "Deprecation";

    /// <summary>RFC 8594: when a resource stops responding.</summary>
    public const string Sunset = "Sunset";

    public const string Link = "Link";

    public const string RetryAfter = "Retry-After";
    public const string IdempotencyKey = "Idempotency-Key";
    public const string ApiKey = "X-Api-Key";

    /// <summary>HMAC-signed marker on the gateway's own synthetic-check requests.</summary>
    public const string SyntheticCheck = "X-Synthetic-Check";
}
