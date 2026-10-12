using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Options;

/// <summary>
/// Configuration section <c>Observability</c>. Each concern has its own nested section so environments can
/// override one area (e.g. <c>Observability:ClientIp:Mode</c>) without restating the rest.
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary><c>service.name</c> resource attribute.</summary>
    [Required]
    public string ServiceName { get; set; } = "api-gateway";

    /// <summary><c>service.namespace</c> resource attribute.</summary>
    public string? ServiceNamespace { get; set; }

    [ValidateObjectMembers]
    public CorrelationOptions Correlation { get; set; } = new();

    [ValidateObjectMembers]
    public TracingOptions Tracing { get; set; } = new();

    [ValidateObjectMembers]
    public MetricsOptions Metrics { get; set; } = new();

    public AccessLogOptions AccessLog { get; set; } = new();

    [ValidateObjectMembers]
    public BodyLoggingOptions BodyLogging { get; set; } = new();

    [ValidateObjectMembers]
    public RedactionOptions Redaction { get; set; } = new();

    [ValidateObjectMembers]
    public ClientIpOptions ClientIp { get; set; } = new();

    [ValidateObjectMembers]
    public UsageOptions Usage { get; set; } = new();

    [ValidateObjectMembers]
    public SyntheticOptions Synthetic { get; set; } = new();

    [ValidateObjectMembers]
    public ExportOptions Export { get; set; } = new();
}

public sealed class CorrelationOptions
{
    /// <summary>Longest inbound <c>X-Correlation-Id</c> that is accepted; longer values are replaced.</summary>
    [Range(8, 256)]
    public int MaxLength { get; set; } = 64;

    /// <summary>Accept a well-formed inbound ID at all. When false the gateway always creates its own.</summary>
    public bool AcceptInbound { get; set; } = true;
}

/// <summary>Whether an inbound <c>traceparent</c> may become the parent of the gateway span.</summary>
public enum TraceTrustMode
{
    /// <summary>Every inbound trace context is trusted (development, fully internal deployments).</summary>
    Always,

    /// <summary>Inbound trace context is never trusted; each request starts a new trace with a link to the inbound one.</summary>
    Never,

    /// <summary>Trusted only when the TCP peer is in <see cref="TracingOptions.TrustedNetworks"/>.</summary>
    TrustedNetworks,
}

public sealed class TracingOptions
{
    public TraceTrustMode TrustMode { get; set; } = TraceTrustMode.TrustedNetworks;

    /// <summary>CIDR ranges whose inbound trace context is trusted in <see cref="TraceTrustMode.TrustedNetworks"/> mode.</summary>
    public string[] TrustedNetworks { get; set; } = [];

    /// <summary>Share of new traces sampled up front (0..1). Errors and slow requests are kept regardless.</summary>
    [Range(0.0, 1.0)]
    public double SamplingRatio { get; set; } = 0.1;

    /// <summary>Requests at least this slow are always exported, even when not sampled up front.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:10:00")]
    public TimeSpan SlowThreshold { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Maximum traces held while waiting for the error/slow decision; the oldest is dropped beyond it.</summary>
    [Range(16, 1_000_000)]
    public int TailBufferMaxTraces { get; set; } = 4096;

    /// <summary>Maximum spans held per undecided trace.</summary>
    [Range(1, 10_000)]
    public int TailBufferMaxSpansPerTrace { get; set; } = 128;
}

public sealed class MetricsOptions
{
    /// <summary>Distinct consumer IDs used as a metric label before further ones collapse into <c>_other</c>.</summary>
    [Range(0, 10_000)]
    public int MaxConsumerLabelValues { get; set; } = 100;

    /// <summary>Distinct values per other label (routes, clusters, client types) before collapsing into <c>_other</c>.</summary>
    [Range(1, 10_000)]
    public int MaxLabelValues { get; set; } = 500;

    /// <summary>OpenTelemetry cardinality limit per metric stream (backstop for every instrument).</summary>
    [Range(100, 100_000)]
    public int CardinalityLimit { get; set; } = 2000;

    /// <summary>Histogram bucket boundaries for latency instruments, in seconds.</summary>
    [Required]
    [MinLength(2)]
    public double[] LatencyBucketsSeconds { get; set; } =
        [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10, 30, 60];

    /// <summary>Count request and response body bytes by wrapping the body streams.</summary>
    public bool CountBodyBytes { get; set; } = true;
}

public sealed class AccessLogOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Log the query string (with sensitive parameters redacted). Off by default: queries often carry personal data.</summary>
    public bool IncludeQueryString { get; set; }

    /// <summary>Request headers logged by name (values of sensitive headers are always redacted).</summary>
    public string[] LoggedRequestHeaders { get; set; } = ["User-Agent", "Content-Type", "Accept"];

    /// <summary>Also log requests to the internal listener (metrics scrapes, health checks).</summary>
    public bool IncludeInternalListener { get; set; }
}

public sealed class BodyLoggingOptions
{
    /// <summary>Bytes captured per body when a route enables body logging without its own limit.</summary>
    [Range(64, 1_048_576)]
    public int DefaultMaxBytes { get; set; } = 4096;

    /// <summary>No route may capture more than this per body.</summary>
    [Range(64, 1_048_576)]
    public int HardMaxBytes { get; set; } = 65_536;

    /// <summary>Content types logged when a route does not list its own.</summary>
    [Required]
    public string[] DefaultContentTypes { get; set; } = ["application/json", "application/problem+json", "text/plain"];
}

public sealed class RedactionOptions
{
    /// <summary>Extra field/header/parameter names treated as sensitive, in addition to the built-in list.</summary>
    public string[] AdditionalSensitiveNames { get; set; } = [];

    /// <summary>Replacement text for redacted values.</summary>
    [Required]
    public string Mask { get; set; } = "***";
}

/// <summary>How the client IP appears in logs.</summary>
public enum ClientIpMode
{
    /// <summary>Logged as is (development).</summary>
    None,

    /// <summary>Last IPv4 octet / last 80 IPv6 bits zeroed (192.0.2.17 → 192.0.2.0).</summary>
    Truncate,

    /// <summary>Keyed HMAC: stable per address (so abuse can still be correlated) but not reversible.</summary>
    Hash,

    /// <summary>Not logged.</summary>
    Erase,
}

public sealed class ClientIpOptions
{
    public ClientIpMode Mode { get; set; } = ClientIpMode.Truncate;

    /// <summary>HMAC key for <see cref="ClientIpMode.Hash"/>. Supply from the secret store, never from a config file.</summary>
    public string? HashKey { get; set; }

    [Range(8, 32)]
    public int Ipv4PrefixLength { get; set; } = 24;

    [Range(16, 128)]
    public int Ipv6PrefixLength { get; set; } = 48;
}

public sealed class UsageOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Distinct consumer/route pairs held between flushes; further ones are counted under <c>_other</c>.</summary>
    [Range(10, 1_000_000)]
    public int MaxEntries { get; set; } = 50_000;
}

public sealed class SyntheticOptions
{
    public bool Enabled { get; set; }

    /// <summary>HMAC key signing <c>X-Synthetic-Check</c>. Supply from the secret store.</summary>
    public string? SigningKey { get; set; }

    /// <summary>How old a synthetic signature may be.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:30:00")]
    public TimeSpan MaxSignatureAge { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Consecutive failures before a probe reports unhealthy.</summary>
    [Range(1, 100)]
    public int FailureThreshold { get; set; } = 3;

    [ValidateEnumeratedItems]
    public SyntheticProbeOptions[] Probes { get; set; } = [];
}

public sealed class SyntheticProbeOptions
{
    /// <summary>Short, bounded name; used as a metric label.</summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9-]{0,40}$")]
    public string Name { get; set; } = "";

    /// <summary>Absolute URL through the gateway's public listener, e.g. <c>http://localhost:8080/api/orders/health</c>.</summary>
    [Required]
    [Url]
    public string Url { get; set; } = "";

    [Required]
    public string Method { get; set; } = "GET";

    [Range(100, 599)]
    public int ExpectedStatus { get; set; } = 200;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

public enum OtlpTransport
{
    Grpc,
    HttpProtobuf,
}

public sealed class ExportOptions
{
    /// <summary>OTLP collector endpoint. When empty, nothing is exported over OTLP.</summary>
    public Uri? OtlpEndpoint { get; set; }

    public OtlpTransport OtlpProtocol { get; set; } = OtlpTransport.Grpc;

    /// <summary>Expose <c>/metrics</c> for Prometheus on the internal listener.</summary>
    public bool PrometheusEnabled { get; set; } = true;

    /// <summary>
    /// Spans queued for export. When the collector is down the queue fills and new spans are dropped;
    /// requests are never blocked.
    /// </summary>
    [Range(128, 1_000_000)]
    public int MaxExportQueueSize { get; set; } = 8192;

    [Range(16, 100_000)]
    public int MaxExportBatchSize { get; set; } = 512;
}

[OptionsValidator]
public sealed partial class ObservabilityOptionsValidator : IValidateOptions<ObservabilityOptions>;
