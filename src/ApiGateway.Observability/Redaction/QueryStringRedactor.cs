using System.Text;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Observability.Redaction;

/// <summary>
/// Makes a query string safe to log: values of sensitive parameters are masked, everything else is kept in
/// order (still URL-encoded), and the result is length-capped.
/// </summary>
public sealed class QueryStringRedactor(SensitiveDataPolicy policy)
{
    public const int MaxLength = 1024;

    public string Redact(QueryString query) => query.HasValue ? Redact(query.Value!) : string.Empty;

    public string Redact(string query)
    {
        if (string.IsNullOrEmpty(query) || string.Equals(query, "?", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var span = query.AsSpan(query[0] == '?' ? 1 : 0);
        var builder = new StringBuilder(Math.Min(query.Length, MaxLength) + 8).Append('?');
        var first = true;

        foreach (var range in span.Split('&'))
        {
            var pair = span[range];
            if (pair.IsEmpty)
            {
                continue;
            }

            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            var equals = pair.IndexOf('=');
            var name = equals < 0 ? pair : pair[..equals];
            var decodedName = Uri.UnescapeDataString(name.ToString());

            builder.Append(name);
            if (equals >= 0)
            {
                builder.Append('=');
                builder.Append(policy.IsSensitiveName(decodedName) ? policy.Mask : policy.Scrub(pair[(equals + 1)..].ToString()));
            }

            if (builder.Length > MaxLength)
            {
                builder.Length = MaxLength;
                builder.Append("...");
                break;
            }
        }

        return builder.ToString();
    }
}
