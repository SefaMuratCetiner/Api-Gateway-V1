using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ApiGateway.Observability.Options;

namespace ApiGateway.Observability.Redaction;

/// <summary>Turns a client address into what may be written to logs in this environment.</summary>
public interface IClientIpAnonymizer
{
    /// <summary>The loggable form, or <c>null</c> when the address must not be logged at all.</summary>
    string? Anonymize(IPAddress? address);
}

/// <summary>Logs the address as is (development only).</summary>
public sealed class PassThroughClientIpAnonymizer : IClientIpAnonymizer
{
    public string? Anonymize(IPAddress? address) => address is null ? null : Normalize(address).ToString();

    internal static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

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

/// <summary>
/// Keyed HMAC-SHA256 of the address, shortened to 16 hex characters. The same address always maps to the same
/// value (abuse can still be correlated across log lines) but cannot be reversed without the key.
/// </summary>
public sealed class HashingClientIpAnonymizer(string key) : IClientIpAnonymizer
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(key);

    public string? Anonymize(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        Span<byte> input = stackalloc byte[16];
        PassThroughClientIpAnonymizer.Normalize(address).TryWriteBytes(input, out var written);
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(_key, input[..written], hash);
        return "h:" + Convert.ToHexStringLower(hash[..8]);
    }
}

/// <summary>Never logs the address.</summary>
public sealed class ErasingClientIpAnonymizer : IClientIpAnonymizer
{
    public string? Anonymize(IPAddress? address) => null;
}

/// <summary>Creates the anonymizer configured for this environment.</summary>
public static class ClientIpAnonymizerFactory
{
    public static IClientIpAnonymizer Create(ClientIpOptions options) => options.Mode switch
    {
        ClientIpMode.None => new PassThroughClientIpAnonymizer(),
        ClientIpMode.Truncate => new TruncatingClientIpAnonymizer(options.Ipv4PrefixLength, options.Ipv6PrefixLength),
        ClientIpMode.Hash => new HashingClientIpAnonymizer(options.HashKey ?? throw new InvalidOperationException("Observability:ClientIp:HashKey is required for Hash mode.")),
        ClientIpMode.Erase => new ErasingClientIpAnonymizer(),
        _ => throw new InvalidOperationException($"Unknown client IP mode '{options.Mode}'."),
    };
}
