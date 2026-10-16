using System.Diagnostics.Metrics;
using ApiGateway.Abstractions.Telemetry;

namespace ApiGateway.Observability.Tracing.Sampling;

/// <summary>
/// <c>gateway.tracing.tail_decisions</c> and <c>gateway.tracing.tail_evictions</c>: what
/// <see cref="TailSamplingProcessor"/> did with traces that were not sampled up front.
/// </summary>
public sealed class TailSamplingMetrics
{
    private readonly Counter<long> _decisions;
    private readonly Counter<long> _evictions;

    public TailSamplingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(GatewayTelemetry.Name);
        _decisions = meter.CreateCounter<long>("gateway.tracing.tail_decisions", unit: "{trace}", description: "Unsampled traces kept or dropped at the end of the request.");
        _evictions = meter.CreateCounter<long>("gateway.tracing.tail_evictions", unit: "{trace}", description: "Undecided traces dropped because the tail buffer was full.");
    }

    public void RecordDecision(bool kept) =>
        _decisions.Add(1, new KeyValuePair<string, object?>(GatewayTelemetry.Tags.Outcome, kept ? "kept" : "dropped"));

    public void RecordEviction() => _evictions.Add(1);
}
