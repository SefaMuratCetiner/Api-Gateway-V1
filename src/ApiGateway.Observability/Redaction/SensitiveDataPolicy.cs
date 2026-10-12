using System.Collections.Frozen;
using System.Text.RegularExpressions;
using ApiGateway.Observability.Options;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Redaction;

/// <summary>
/// What counts as sensitive in logs: names (headers, query parameters, JSON properties, log properties) whose
/// values are always masked, and value patterns (bearer tokens, JWTs, <c>key=value</c> secrets) scrubbed from
/// any text. Shared by every redactor and by the logging provider's last-chance scrubber.
/// </summary>
public sealed partial class SensitiveDataPolicy
{
    /// <summary>Names masked by exact (case-insensitive, separator-insensitive) match.</summary>
    public static readonly IReadOnlyList<string> BuiltInNames =
    [
        "authorization", "proxy-authorization", "cookie", "set-cookie", "x-api-key", "api-key", "apikey",
        "idempotency-key", "x-csrf-token", "x-xsrf-token", "password", "passwd", "pwd", "secret", "client_secret",
        "token", "access_token", "refresh_token", "id_token", "session", "sessionid", "otp", "pin", "cvv", "iban",
        "card_number", "ssn", "private_key", "signature", "x-synthetic-check",
    ];

    /// <summary>A name containing one of these fragments is masked too (e.g. <c>X-Partner-Token</c>).</summary>
    private static readonly string[] SensitiveFragments = ["password", "secret", "token", "apikey", "authorization", "cookie", "credential", "privatekey"];

    private readonly FrozenSet<string> _names;

    public SensitiveDataPolicy(IOptions<ObservabilityOptions> options)
        : this(options.Value.Redaction)
    {
    }

    public SensitiveDataPolicy(RedactionOptions options)
    {
        Mask = options.Mask;
        _names = BuiltInNames.Concat(options.AdditionalSensitiveNames)
            .Select(Normalize)
            .ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>Replacement for masked values.</summary>
    public string Mask { get; }

    public bool IsSensitiveName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var normalized = Normalize(name);
        if (_names.Contains(normalized))
        {
            return true;
        }

        foreach (var fragment in SensitiveFragments)
        {
            if (normalized.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Masks secrets embedded in free text. Returns the input instance when nothing matched.</summary>
    public string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text) || !MayContainSecret(text))
        {
            return text;
        }

        var result = AuthorizationScheme().Replace(text, match => match.Groups["scheme"].Value + " " + Mask);
        result = Jwt().Replace(result, Mask);
        result = KeyValueSecret().Replace(result, match => match.Groups["key"].Value + match.Groups["sep"].Value + Mask);
        return result;
    }

    /// <summary>Cheap pre-check so the regexes only run on text that could match.</summary>
    private static bool MayContainSecret(string text) =>
        text.Contains("eyJ", StringComparison.Ordinal)
        || text.Contains("earer", StringComparison.OrdinalIgnoreCase)
        || text.Contains("asic ", StringComparison.OrdinalIgnoreCase)
        || text.Contains('=', StringComparison.Ordinal)
        || text.Contains(':', StringComparison.Ordinal);

    /// <summary>Lower-case, separators removed: <c>X-Api_Key</c> → <c>xapikey</c>, so variants match one entry.</summary>
    private static string Normalize(string name)
    {
        Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
        var length = 0;
        foreach (var c in name)
        {
            if (c is '-' or '_' or '.' or ' ')
            {
                continue;
            }

            buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..length]);
    }

    [GeneratedRegex(@"(?<scheme>Bearer|Basic|Digest|Negotiate|ApiKey)\s+[A-Za-z0-9\-._~+/=]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 100)]
    private static partial Regex AuthorizationScheme();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?<key>\b(password|passwd|pwd|secret|client_secret|token|access_token|refresh_token|id_token|api[_-]?key|apikey|code_verifier|signature|sig))(?<sep>\s*[=:]\s*""?)[^&\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 100)]
    private static partial Regex KeyValueSecret();
}
