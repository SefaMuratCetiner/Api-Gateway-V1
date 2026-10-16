using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ApiGateway.Observability.Redaction;

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
