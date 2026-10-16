using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// Destination discovery through DNS: destination host names (Kubernetes services, Consul DNS, plain DNS) are
/// resolved to one destination per address and refreshed periodically, so the same configuration works locally
/// and deployed.
/// </summary>
public sealed class DnsDiscoveryOptions
{
    public bool Enabled { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan RefreshPeriod { get; set; } = TimeSpan.FromSeconds(30);
}
