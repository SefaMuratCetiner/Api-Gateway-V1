using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Export;

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
