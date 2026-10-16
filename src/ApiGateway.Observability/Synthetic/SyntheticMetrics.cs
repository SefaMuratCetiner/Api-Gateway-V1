using System.Diagnostics;
using System.Diagnostics.Metrics;
using ApiGateway.Abstractions.Telemetry;

namespace ApiGateway.Observability.Synthetic;

/// <summary><c>gateway.synthetic.checks</c> and <c>gateway.synthetic.duration</c>: results of the synthetic probes.</summary>
public sealed class SyntheticMetrics
{
    private readonly Counter<long> _checks;
    private readonly Histogram<double> _duration;

    public SyntheticMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(GatewayTelemetry.SyntheticMeter);
        _checks = meter.CreateCounter<long>("gateway.synthetic.checks", unit: "{check}", description: "Synthetic checks through the public listener, by probe and outcome.");
        _duration = meter.CreateHistogram<double>("gateway.synthetic.duration", unit: "s", description: "Duration of synthetic checks.");
    }

    public void RecordCheck(string probe, string outcome, TimeSpan elapsed)
    {
        _checks.Add(1, new TagList { { GatewayTelemetry.Tags.Probe, probe }, { GatewayTelemetry.Tags.Outcome, outcome } });
        _duration.Record(elapsed.TotalSeconds, new TagList { { GatewayTelemetry.Tags.Probe, probe } });
    }
}
