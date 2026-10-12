namespace ApiGateway.Abstractions.Telemetry;

/// <summary>
/// Names shared by every library that emits telemetry. All meters and activity sources start with
/// <see cref="Prefix"/>, so the composition root subscribes with one wildcard.
/// </summary>
public static class GatewayTelemetry
{
    public const string Prefix = "ApiGateway";
    public const string Wildcard = Prefix + ".*";

    /// <summary>Main meter and activity source.</summary>
    public const string Name = Prefix;

    /// <summary>Meter of the error library (<c>gateway.errors</c>).</summary>
    public const string ErrorsMeter = Prefix + ".Errors";

    /// <summary>Meter of the synthetic checks.</summary>
    public const string SyntheticMeter = Prefix + ".Synthetic";

    /// <summary>Log source context of audit events; routed to their own sink with their own retention.</summary>
    public const string AuditLogCategory = Prefix + ".Audit";

    /// <summary>Log source context of usage-analytics flushes.</summary>
    public const string UsageLogCategory = Prefix + ".Usage";

    /// <summary>Log source context of opt-in body logging.</summary>
    public const string BodyLogCategory = Prefix + ".Body";

    /// <summary>Tag / label names. Only bounded values may be used as metric labels.</summary>
    public static class Tags
    {
        public const string RouteId = "gateway.route_id";
        public const string ClusterId = "gateway.cluster_id";
        public const string DestinationId = "gateway.destination_id";
        public const string ClientId = "gateway.client_id";
        public const string ClientType = "gateway.client_type";
        public const string TenantId = "gateway.tenant_id";
        public const string Consumer = "gateway.consumer";
        public const string CorrelationId = "gateway.correlation_id";
        public const string ErrorCode = "error.code";
        public const string ErrorType = "error.type";
        public const string Reason = "reason";
        public const string Method = "http.request.method";
        public const string StatusCode = "http.response.status_code";
        public const string StatusClass = "http.response.status_class";
        public const string Health = "gateway.health";
        public const string Probe = "gateway.probe";
        public const string Outcome = "outcome";
        public const string Synthetic = "gateway.synthetic";
    }

    /// <summary>Label value used when a label is unknown (e.g. no route matched).</summary>
    public const string Unknown = "unknown";

    /// <summary>Label value that replaces values beyond a cardinality cap.</summary>
    public const string Overflow = "_other";
}
