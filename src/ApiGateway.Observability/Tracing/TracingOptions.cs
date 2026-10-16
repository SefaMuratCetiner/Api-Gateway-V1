using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Tracing;

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
