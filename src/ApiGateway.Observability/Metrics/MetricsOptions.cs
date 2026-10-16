using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Metrics;

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
