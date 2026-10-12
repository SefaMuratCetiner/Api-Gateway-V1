using ApiGateway.Abstractions.Audit;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Observability.Audit;

/// <summary>
/// Audits every proxy configuration YARP loads or rejects: one event per added, removed or changed route and
/// cluster (with before/after fingerprints) plus a summary event. Configuration comes from files or a config
/// service, not a person, so the actor is <c>system:configuration</c>; the admin API audits its own changes
/// with the real principal.
/// </summary>
public sealed class ProxyConfigAuditListener(IAuditLog audit) : IConfigChangeListener
{
    public const string Actor = "system:configuration";

    private readonly Lock _lock = new();
    private Dictionary<string, string> _routes = new(StringComparer.Ordinal);
    private Dictionary<string, string> _clusters = new(StringComparer.Ordinal);
    private string? _overall;

    public void ConfigurationLoaded(IReadOnlyList<IProxyConfig> proxyConfigs)
    {
        // Applied (or failed) follows; auditing there avoids recording configs that never took effect.
    }

    public void ConfigurationLoadingFailed(IProxyConfigProvider configProvider, Exception exception) =>
        audit.Write(new AuditEvent(Actor, "proxy-config.load-failed", "proxy-config:" + configProvider.GetType().Name, Details: exception.GetType().Name));

    public void ConfigurationApplyingFailed(IReadOnlyList<IProxyConfig> proxyConfigs, Exception exception) =>
        audit.Write(new AuditEvent(Actor, "proxy-config.rejected", "proxy-config", BeforeHash: _overall, AfterHash: Fingerprint(proxyConfigs).Overall, Details: exception.GetType().Name + "; running configuration kept"));

    public void ConfigurationApplied(IReadOnlyList<IProxyConfig> proxyConfigs)
    {
        var next = Fingerprint(proxyConfigs);
        lock (_lock)
        {
            var isInitial = _overall is null;
            if (!isInitial)
            {
                AuditDiff("route", _routes, next.Routes);
                AuditDiff("cluster", _clusters, next.Clusters);
            }

            if (!string.Equals(_overall, next.Overall, StringComparison.Ordinal))
            {
                audit.Write(new AuditEvent(
                    Actor,
                    isInitial ? "proxy-config.loaded" : "proxy-config.applied",
                    "proxy-config",
                    BeforeHash: _overall,
                    AfterHash: next.Overall,
                    Details: $"{next.Routes.Count} routes, {next.Clusters.Count} clusters"));
            }

            _routes = next.Routes;
            _clusters = next.Clusters;
            _overall = next.Overall;
        }
    }

    private void AuditDiff(string kind, Dictionary<string, string> before, Dictionary<string, string> after)
    {
        foreach (var (id, hash) in after)
        {
            if (!before.TryGetValue(id, out var previous))
            {
                audit.Write(new AuditEvent(Actor, kind + ".added", $"{kind}:{id}", AfterHash: hash));
            }
            else if (!string.Equals(previous, hash, StringComparison.Ordinal))
            {
                audit.Write(new AuditEvent(Actor, kind + ".changed", $"{kind}:{id}", BeforeHash: previous, AfterHash: hash));
            }
        }

        foreach (var (id, hash) in before)
        {
            if (!after.ContainsKey(id))
            {
                audit.Write(new AuditEvent(Actor, kind + ".removed", $"{kind}:{id}", BeforeHash: hash));
            }
        }
    }

    private static Snapshot Fingerprint(IReadOnlyList<IProxyConfig> configs)
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        var clusters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var config in configs)
        {
            foreach (var route in config.Routes)
            {
                routes[route.RouteId] = ConfigFingerprint.Of(route);
            }

            foreach (var cluster in config.Clusters)
            {
                clusters[cluster.ClusterId] = ConfigFingerprint.Of(cluster);
            }
        }

        var overall = ConfigFingerprint.Combine(
            routes.Select(static pair => "route:" + pair.Key + "=" + pair.Value)
                .Concat(clusters.Select(static pair => "cluster:" + pair.Key + "=" + pair.Value)));
        return new Snapshot(routes, clusters, overall);
    }

    private sealed record Snapshot(Dictionary<string, string> Routes, Dictionary<string, string> Clusters, string Overall);
}
