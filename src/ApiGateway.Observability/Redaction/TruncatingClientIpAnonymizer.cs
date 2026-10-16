using System.Net;
using System.Net.Sockets;

namespace ApiGateway.Observability.Redaction;

/// <summary>Keeps the network prefix only: 192.0.2.17 → 192.0.2.0 (/24), 2001:db8:1:2::7 → 2001:db8:1:: (/48).</summary>
public sealed class TruncatingClientIpAnonymizer(int ipv4PrefixLength, int ipv6PrefixLength) : IClientIpAnonymizer
{
    public string? Anonymize(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        address = PassThroughClientIpAnonymizer.Normalize(address);
        var prefix = address.AddressFamily == AddressFamily.InterNetwork ? ipv4PrefixLength : ipv6PrefixLength;
        return Truncate(address, prefix).ToString();
    }

    public static IPAddress Truncate(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        for (var bit = prefixLength; bit < bytes.Length * 8; bit++)
        {
            bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }

        return new IPAddress(bytes);
    }
}
