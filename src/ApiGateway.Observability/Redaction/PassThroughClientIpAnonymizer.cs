using System.Net;

namespace ApiGateway.Observability.Redaction;

/// <summary>Logs the address as is (development only).</summary>
public sealed class PassThroughClientIpAnonymizer : IClientIpAnonymizer
{
    public string? Anonymize(IPAddress? address) => address is null ? null : Normalize(address).ToString();

    internal static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
