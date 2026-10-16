using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Serilog;

public sealed class OtlpSinkOptions
{
    /// <summary>Ship logs over OTLP to <c>Observability:Export:OtlpEndpoint</c>.</summary>
    public bool Enabled { get; set; }

    [Range(100, 1_000_000)]
    public int QueueLimit { get; set; } = 10_000;
}
