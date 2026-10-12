using System.Diagnostics;
using OpenTelemetry.Trace;

namespace ApiGateway.Observability.Tracing.Sampling;

/// <summary>
/// Head sampler with a second chance:
/// <list type="bullet">
/// <item>A span with a (trusted) parent follows the parent's sampled flag.</item>
/// <item>A new trace is sampled with the configured ratio (by trace ID, so every instance decides the same).</item>
/// <item>Anything not sampled is still <em>recorded</em> (<see cref="SamplingDecision.RecordOnly"/>), so
/// <see cref="TailSamplingProcessor"/> can export it after all if the request fails or is slow.</item>
/// </list>
/// Untrusted parents never reach this sampler: <see cref="TrustAwareContextPropagator"/> removes them first.
/// </summary>
public sealed class GatewaySampler : Sampler
{
    private static readonly SamplingResult Sample = new(SamplingDecision.RecordAndSample);
    private static readonly SamplingResult RecordOnly = new(SamplingDecision.RecordOnly);

    private readonly TraceIdRatioBasedSampler _ratio;

    public GatewaySampler(double ratio)
    {
        Ratio = ratio;
        _ratio = new TraceIdRatioBasedSampler(ratio);
        Description = $"GatewaySampler{{ratio={ratio:0.####},tail=errors+slow}}";
    }

    public double Ratio { get; }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        var parent = samplingParameters.ParentContext;
        if (parent.TraceId != default)
        {
            return parent.TraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? Sample : RecordOnly;
        }

        return _ratio.ShouldSample(samplingParameters).Decision == SamplingDecision.RecordAndSample ? Sample : RecordOnly;
    }
}
