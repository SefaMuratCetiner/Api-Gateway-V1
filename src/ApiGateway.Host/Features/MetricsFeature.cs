using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.Metrics;
using Yarp.ReverseProxy.Health;

namespace ApiGateway.Host.Features;

/// <summary>Gateway metrics: requests, upstream timings, cluster and destination health.</summary>
internal sealed class MetricsFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var services = gateway.Services;
        services.AddSingleton<CardinalityGuard>();
        services.AddSingleton<GatewayMetrics>();
        services.AddSingleton<IGatewayMetrics>(provider => provider.GetRequiredService<GatewayMetrics>());
        services.AddHostedService<ClusterHealthGauge>();

        // Upstream timings from YARP's forwarder events.
        services.AddTelemetryConsumer<UpstreamTimingTelemetryConsumer>();

        // Destination health transitions become metrics and log events.
        gateway.Decorate<IDestinationHealthUpdater>(static (provider, inner) => new DestinationHealthChangeRecorder(
            inner,
            provider.GetRequiredService<GatewayMetrics>(),
            provider.GetRequiredService<ILogger<DestinationHealthChangeRecorder>>()));
    }
}
