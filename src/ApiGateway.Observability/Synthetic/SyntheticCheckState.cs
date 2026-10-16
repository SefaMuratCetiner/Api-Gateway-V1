using System.Collections.Concurrent;

namespace ApiGateway.Observability.Synthetic;

/// <summary>Latest result of one probe.</summary>
public sealed record SyntheticProbeResult(string Probe, bool Success, int? StatusCode, TimeSpan Duration, DateTimeOffset At, int ConsecutiveFailures);

/// <summary>Shared, thread-safe store of the latest probe results (read by the health check).</summary>
public sealed class SyntheticCheckState
{
    private readonly ConcurrentDictionary<string, SyntheticProbeResult> _results = new(StringComparer.Ordinal);

    public IReadOnlyCollection<SyntheticProbeResult> Results => [.. _results.Values];

    public SyntheticProbeResult Update(string probe, bool success, int? statusCode, TimeSpan duration, DateTimeOffset at) =>
        _results.AddOrUpdate(
            probe,
            static (name, state) => new SyntheticProbeResult(name, state.success, state.statusCode, state.duration, state.at, state.success ? 0 : 1),
            static (name, previous, state) => new SyntheticProbeResult(name, state.success, state.statusCode, state.duration, state.at, state.success ? 0 : previous.ConsecutiveFailures + 1),
            (success, statusCode, duration, at));
}
