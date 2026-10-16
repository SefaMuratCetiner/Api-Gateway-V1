using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Redaction;

/// <summary>How the client IP appears in logs.</summary>
public enum ClientIpMode
{
    /// <summary>Logged as is (development).</summary>
    None,

    /// <summary>Last IPv4 octet / last 80 IPv6 bits zeroed (192.0.2.17 → 192.0.2.0).</summary>
    Truncate,

    /// <summary>Keyed HMAC: stable per address (so abuse can still be correlated) but not reversible.</summary>
    Hash,

    /// <summary>Not logged.</summary>
    Erase,
}

public sealed class ClientIpOptions
{
    public ClientIpMode Mode { get; set; } = ClientIpMode.Truncate;

    /// <summary>HMAC key for <see cref="ClientIpMode.Hash"/>. Supply from the secret store, never from a config file.</summary>
    public string? HashKey { get; set; }

    [Range(8, 32)]
    public int Ipv4PrefixLength { get; set; } = 24;

    [Range(16, 128)]
    public int Ipv6PrefixLength { get; set; } = 48;
}
