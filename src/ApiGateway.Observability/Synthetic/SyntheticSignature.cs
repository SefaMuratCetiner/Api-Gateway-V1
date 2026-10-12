using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ApiGateway.Observability.Synthetic;

/// <summary>
/// Signs and verifies the <c>X-Synthetic-Check</c> header: <c>{probe}.{unix-seconds}.{hmac}</c>, HMAC-SHA256 over
/// <c>{probe}.{unix-seconds}</c>. A client cannot mark its own traffic as synthetic (to escape usage analytics)
/// without the key, and a captured header expires after <c>MaxSignatureAge</c>.
/// </summary>
public sealed class SyntheticSignature(string key, TimeProvider time, TimeSpan maxAge)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(key);

    public string Create(string probe)
    {
        var timestamp = time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"{probe}.{timestamp}.{Sign(probe, timestamp)}";
    }

    /// <summary>Returns the probe name when the header is genuine and fresh; otherwise <c>null</c>.</summary>
    public string? Verify(string? header)
    {
        if (string.IsNullOrEmpty(header) || header.Length > 256)
        {
            return null;
        }

        var parts = header.Split('.');
        if (parts.Length != 3 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        var age = time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age > maxAge || age < -maxAge)
        {
            return null;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(parts[0], parts[1]));
        var actual = Encoding.ASCII.GetBytes(parts[2]);
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? parts[0] : null;
    }

    private string Sign(string probe, string timestamp)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(probe + "." + timestamp));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
