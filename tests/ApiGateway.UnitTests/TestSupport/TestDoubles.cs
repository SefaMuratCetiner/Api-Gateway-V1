using System.Diagnostics;
using System.Diagnostics.Metrics;
using ApiGateway.Errors;
using ApiGateway.Observability;
using ApiGateway.Observability.Tracing;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace ApiGateway.UnitTests.TestSupport;

/// <summary>Meter factory for tests: creates real meters, disposes them with the factory.</summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
        {
            meter.Dispose();
        }

        _meters.Clear();
    }
}

/// <summary>Processor that records what reached it.</summary>
internal sealed class CollectingProcessor : BaseProcessor<Activity>
{
    public List<Activity> Ended { get; } = [];

    public override void OnEnd(Activity data) => Ended.Add(data);
}

internal static class TestOptions
{
    public static IOptions<ErrorsOptions> Errors(Action<ErrorsOptions>? configure = null)
    {
        var options = new ErrorsOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }

    public static IOptions<ObservabilityOptions> Observability(Action<ObservabilityOptions>? configure = null)
    {
        var options = new ObservabilityOptions();
        options.Tracing.TrustMode = TraceTrustMode.Always;
        configure?.Invoke(options);
        return Options.Create(options);
    }
}
