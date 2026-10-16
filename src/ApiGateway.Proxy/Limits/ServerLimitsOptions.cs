using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Proxy.Limits;

/// <summary>Kestrel limits. <c>null</c> keeps Kestrel's default.</summary>
public sealed class ServerLimitsOptions
{
    /// <summary>Open connections per instance; <c>null</c> = unlimited.</summary>
    [Range(1L, long.MaxValue)]
    public long? MaxConcurrentConnections { get; set; }

    /// <summary>Upgraded (WebSocket) connections per instance; <c>null</c> = unlimited.</summary>
    [Range(1L, long.MaxValue)]
    public long? MaxConcurrentUpgradedConnections { get; set; }

    /// <summary>Concurrent HTTP/2 streams per connection.</summary>
    [Range(1, 10_000)]
    public int Http2MaxStreamsPerConnection { get; set; } = 100;

    /// <summary>Minimum request body rate in bytes/second; slower clients are disconnected. <c>null</c> disables the check.</summary>
    [Range(1.0, 1_000_000_000.0)]
    public double? MinRequestBodyDataRate { get; set; } = 240;

    /// <summary>Time a client may stay below <see cref="MinRequestBodyDataRate"/> before it is disconnected.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
