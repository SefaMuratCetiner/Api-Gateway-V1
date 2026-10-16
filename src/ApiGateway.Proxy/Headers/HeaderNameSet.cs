using Microsoft.AspNetCore.Http;

namespace ApiGateway.Proxy.Headers;

/// <summary>Exact and prefix (<c>X-Internal-*</c>) header-name matching.</summary>
public sealed class HeaderNameSet
{
    private readonly HashSet<string> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _prefixes;

    public HeaderNameSet(IEnumerable<string> patterns)
    {
        var prefixes = new List<string>();
        foreach (var pattern in patterns)
        {
            if (pattern.EndsWith('*'))
            {
                prefixes.Add(pattern[..^1]);
            }
            else
            {
                _exact.Add(pattern);
            }
        }

        _prefixes = [.. prefixes];
    }

    public bool Contains(string name)
    {
        if (_exact.Contains(name))
        {
            return true;
        }

        foreach (var prefix in _prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Removes every matching header.</summary>
    public void RemoveFrom(IHeaderDictionary headers)
    {
        List<string>? matches = null;
        foreach (var name in headers.Keys)
        {
            if (Contains(name))
            {
                (matches ??= []).Add(name);
            }
        }

        foreach (var name in matches ?? [])
        {
            headers.Remove(name);
        }
    }
}
