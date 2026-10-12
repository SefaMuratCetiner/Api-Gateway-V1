using System.Diagnostics.Metrics;
using ApiGateway.Abstractions.Telemetry;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Observability.Metrics;

/// <summary>
/// <c>gateway.cluster.destinations</c>: destinations per cluster by effective health, observed from YARP's live
/// state at each collection. A hosted service so the instrument exists from startup.
/// </summary>
public sealed class ClusterHealthGauge(IMeterFactory meterFactory, IProxyStateLookup proxyState) : IHostedService
{
    private ObservableGauge<int>? _gauge;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var meter = meterFactory.Create(GatewayTelemetry.Name);
        _gauge = meter.CreateObservableGauge(
            "gateway.cluster.destinations",
            Observe,
            unit: "{destination}",
            description: "Destinations per cluster by effective health (active and passive checks combined).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal bool IsStarted => _gauge is not null;

    private IEnumerable<Measurement<int>> Observe()
    {
        foreach (var cluster in proxyState.GetClusters())
        {
            int healthy = 0, unhealthy = 0, unknown = 0;
            foreach (var destination in cluster.Destinations.Values)
            {
                switch (EffectiveHealth(destination.Health))
                {
                    case DestinationHealth.Healthy:
                        healthy++;
                        break;
                    case DestinationHealth.Unhealthy:
                        unhealthy++;
                        break;
                    default:
                        unknown++;
                        break;
                }
            }

            yield return Measure(cluster.ClusterId, "healthy", healthy);
            yield return Measure(cluster.ClusterId, "unhealthy", unhealthy);
            yield return Measure(cluster.ClusterId, "unknown", unknown);
        }
    }

    /// <summary>Unhealthy if either check says so; healthy if either says healthy; unknown otherwise.</summary>
    public static DestinationHealth EffectiveHealth(DestinationHealthState state)
    {
        if (state.Active == DestinationHealth.Unhealthy || state.Passive == DestinationHealth.Unhealthy)
        {
            return DestinationHealth.Unhealthy;
        }

        return state.Active == DestinationHealth.Healthy || state.Passive == DestinationHealth.Healthy
            ? DestinationHealth.Healthy
            : DestinationHealth.Unknown;
    }

    private static Measurement<int> Measure(string clusterId, string health, int count) =>
        new(count, new KeyValuePair<string, object?>(GatewayTelemetry.Tags.ClusterId, clusterId), new KeyValuePair<string, object?>(GatewayTelemetry.Tags.Health, health));
}
