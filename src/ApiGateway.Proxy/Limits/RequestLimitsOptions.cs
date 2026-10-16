using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Limits;

/// <summary>
/// Configuration section <c>Limits</c>: body and response limits, and Kestrel server limits. URL and request-header
/// limits that produce 414/431 problem bodies are <c>Errors:ProtocolLimits</c>.
/// </summary>
public sealed class RequestLimitsOptions
{
    public const string SectionName = "Limits";

    /// <summary>
    /// Global request body cap in bytes (Kestrel <c>MaxRequestBodySize</c>); <c>null</c> = unlimited. Routes override
    /// it with YARP's <c>MaxRequestBodySize</c> (<c>-1</c> = unlimited, e.g. uploads and streaming).
    /// </summary>
    [Range(1L, long.MaxValue)]
    public long? MaxRequestBodySize { get; set; } = 30L * 1024 * 1024;

    /// <summary>
    /// Largest backend response body forwarded, in bytes; <c>null</c> = unlimited. Routes override it with the
    /// <c>Limits.MaxResponseBodySize</c> metadata (<c>-1</c> = unlimited).
    /// </summary>
    [Range(1L, long.MaxValue)]
    public long? MaxResponseBodySize { get; set; }

    /// <summary>Largest backend response header block, in KiB (<c>SocketsHttpHandler.MaxResponseHeadersLength</c>).</summary>
    [Range(1, 1024)]
    public int MaxResponseHeadersSizeKb { get; set; } = 64;

    [ValidateObjectMembers]
    public ServerLimitsOptions Server { get; set; } = new();
}

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

[OptionsValidator]
public sealed partial class RequestLimitsOptionsValidator : IValidateOptions<RequestLimitsOptions>;
