using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// Host patterns as used by <see cref="RoutingOptions.AllowedHosts"/>: an exact name or address, a wildcard
/// subdomain (<c>*.example.com</c>, which does not match <c>example.com</c> itself) or <c>*</c>. Matching ignores
/// case, ports and a trailing dot.
/// </summary>
public sealed partial class HostPatterns
{
    private readonly string[] _exact;
    private readonly string[] _suffixes;
    private readonly bool _any;

    public HostPatterns(IEnumerable<string> patterns)
    {
        var exact = new List<string>();
        var suffixes = new List<string>();
        foreach (var raw in patterns)
        {
            var pattern = Normalize(raw);
            if (string.Equals(pattern, "*", StringComparison.Ordinal))
            {
                _any = true;
            }
            else if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                suffixes.Add(pattern[1..]);
            }
            else if (pattern.Length > 0)
            {
                exact.Add(pattern);
            }
        }

        _exact = [.. exact];
        _suffixes = [.. suffixes];
    }

    public bool Matches(HostString host) => host.HasValue && Matches(host.Host);

    /// <summary>True when <paramref name="host"/> (a host name or address, without port) matches any pattern.</summary>
    public bool Matches(string host)
    {
        if (_any)
        {
            return true;
        }

        host = Normalize(host);
        if (host.Length == 0)
        {
            return false;
        }

        foreach (var exact in _exact)
        {
            if (string.Equals(host, exact, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var suffix in _suffixes)
        {
            if (host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True for <c>*</c> and wildcard-subdomain patterns.</summary>
    public static bool IsWildcard(string pattern) => pattern.Contains('*', StringComparison.Ordinal);

    /// <summary>Describes what is wrong with <paramref name="pattern"/>, or <c>null</c> when it is valid.</summary>
    public static string? Problem(string pattern, bool allowWildcard)
    {
        if (IsWildcard(pattern) && !allowWildcard)
        {
            return $"'{pattern}': wildcard hosts are only allowed in Development.";
        }

        var host = Normalize(pattern);
        host = host.StartsWith("*.", StringComparison.Ordinal) ? host[2..] : host;
        if (string.Equals(host, "*", StringComparison.Ordinal) || IPAddress.TryParse(host.Trim('[', ']'), out _) || HostName().IsMatch(host))
        {
            return null;
        }

        return $"'{pattern}' is not a host name, address, '*.domain' or '*'.";
    }

    private static string Normalize(string host)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();

        // Ports are not part of the pattern. Bracketed IPv6 ends at ']'; bare IPv6 (several colons) has no port.
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']', StringComparison.Ordinal);
            return close > 0 ? host[..(close + 1)] : host;
        }

        var colon = host.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && colon == host.LastIndexOf(':') ? host[..colon] : host;
    }

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 50)]
    private static partial Regex HostName();
}
