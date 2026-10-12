using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ApiGateway.Observability.Audit;

/// <summary>
/// SHA-256 fingerprints of configuration objects for the audit log. Only the fingerprint is logged, never the
/// content, so secrets inside configuration cannot leak through the audit trail.
/// </summary>
public static class ConfigFingerprint
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        IncludeFields = false,
    };

    public static string Of<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }

    /// <summary>Fingerprint of a set of fingerprints, independent of order.</summary>
    public static string Combine(IEnumerable<string> fingerprints)
    {
        var ordered = string.Join('\n', fingerprints.Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ordered)));
    }
}
