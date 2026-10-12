using ApiGateway.Observability.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Synthetic;

/// <summary>
/// Reports the synthetic checks. Deliberately <em>not</em> part of readiness: a failing backend route must not
/// take the gateway out of the load balancer. Exposed for dashboards and alerting only.
/// </summary>
public sealed class SyntheticChecksHealthCheck(SyntheticCheckState state, IOptions<ObservabilityOptions> options) : IHealthCheck
{
    public const string Tag = "synthetic";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var synthetic = options.Value.Synthetic;
        if (!synthetic.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Synthetic checks are disabled."));
        }

        var results = state.Results;
        var data = results.ToDictionary(
            static result => result.Probe,
            static result => (object)new { result.Success, result.StatusCode, DurationMs = result.Duration.TotalMilliseconds, result.At, result.ConsecutiveFailures },
            StringComparer.Ordinal);

        var failing = results.Where(result => result.ConsecutiveFailures >= synthetic.FailureThreshold).Select(static result => result.Probe).ToArray();
        if (failing.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Failing probes: {string.Join(", ", failing)}", data: data));
        }

        var degraded = results.Where(static result => !result.Success).Select(static result => result.Probe).ToArray();
        return Task.FromResult(degraded.Length > 0
            ? HealthCheckResult.Degraded($"Probes with recent failures: {string.Join(", ", degraded)}", data: data)
            : HealthCheckResult.Healthy($"{results.Count} probes passing.", data));
    }
}
