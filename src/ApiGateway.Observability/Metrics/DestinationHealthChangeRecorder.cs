using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Observability.Metrics;

/// <summary>
/// Decorator around YARP's <see cref="IDestinationHealthUpdater"/>: every active or passive health transition is
/// counted (<c>gateway.destination.health_changes</c>) and logged. Behaviour is unchanged; the decorated
/// updater still applies the change.
/// </summary>
public sealed partial class DestinationHealthChangeRecorder(
    IDestinationHealthUpdater inner,
    GatewayMetrics metrics,
    ILogger<DestinationHealthChangeRecorder> logger) : IDestinationHealthUpdater
{
    public void SetActive(ClusterState cluster, IEnumerable<NewActiveDestinationHealth> newHealthStates)
    {
        var changes = newHealthStates as IReadOnlyCollection<NewActiveDestinationHealth> ?? [.. newHealthStates];
        var previous = changes.Select(static change => change.Destination.Health.Active).ToArray();

        inner.SetActive(cluster, changes);

        var index = 0;
        foreach (var change in changes)
        {
            Report(cluster, change.Destination, "active", previous[index++], change.NewActiveHealth);
        }
    }

    public void SetPassive(ClusterState cluster, DestinationState destination, DestinationHealth newHealth, TimeSpan reactivationPeriod)
    {
        var previous = destination.Health.Passive;
        inner.SetPassive(cluster, destination, newHealth, reactivationPeriod);
        Report(cluster, destination, "passive", previous, newHealth);
    }

    private void Report(ClusterState cluster, DestinationState destination, string probe, DestinationHealth previous, DestinationHealth current)
    {
        if (previous == current)
        {
            return;
        }

        var state = HealthName(current);
        var previousState = HealthName(previous);
        metrics.RecordHealthChange(cluster.ClusterId, state);
        if (current == DestinationHealth.Unhealthy)
        {
            LogUnhealthy(logger, cluster.ClusterId, destination.DestinationId, probe, previousState);
        }
        else
        {
            LogChanged(logger, cluster.ClusterId, destination.DestinationId, probe, previousState, state);
        }
    }

    public static string HealthName(DestinationHealth health) => health switch
    {
        DestinationHealth.Healthy => "healthy",
        DestinationHealth.Unhealthy => "unhealthy",
        _ => "unknown",
    };

    [LoggerMessage(EventId = 3100, Level = LogLevel.Warning, Message = "Destination {ClusterId}/{DestinationId} became unhealthy ({Probe} check, was {PreviousHealth})")]
    private static partial void LogUnhealthy(ILogger logger, string clusterId, string destinationId, string probe, string previousHealth);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "Destination {ClusterId}/{DestinationId} {Probe} health changed from {PreviousHealth} to {Health}")]
    private static partial void LogChanged(ILogger logger, string clusterId, string destinationId, string probe, string previousHealth, string health);
}
