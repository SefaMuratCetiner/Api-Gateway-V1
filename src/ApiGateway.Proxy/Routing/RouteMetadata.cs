using System.Globalization;

namespace ApiGateway.Proxy.Routing;

/// <summary>Typed readers for route-metadata values, shared by every parser.</summary>
public static class RouteMetadata
{
    public static string? Get(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null && metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    public static bool GetBool(IReadOnlyDictionary<string, string>? metadata, string key, bool fallback) =>
        bool.TryParse(Get(metadata, key), out var value) ? value : fallback;

    public static int GetInt(IReadOnlyDictionary<string, string>? metadata, string key, int fallback) =>
        int.TryParse(Get(metadata, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>Comma-separated list, trimmed, empty entries removed.</summary>
    public static IReadOnlyList<string> GetList(IReadOnlyDictionary<string, string>? metadata, string key) =>
        Get(metadata, key)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    /// <summary>Adds a problem when <paramref name="key"/> is present but not a boolean.</summary>
    public static void ValidateBool(IReadOnlyDictionary<string, string>? metadata, string key, List<string> problems)
    {
        var raw = Get(metadata, key);
        if (raw is not null && !bool.TryParse(raw, out _))
        {
            problems.Add($"'{key}' must be true or false, got '{raw}'");
        }
    }

    /// <summary>Adds a problem when <paramref name="key"/> is present but not an integer in range.</summary>
    public static void ValidateInt(IReadOnlyDictionary<string, string>? metadata, string key, int min, int max, List<string> problems)
    {
        var raw = Get(metadata, key);
        if (raw is null)
        {
            return;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            problems.Add($"'{key}' must be an integer between {min} and {max}, got '{raw}'");
        }
    }

    /// <summary>Adds a problem for every key under <paramref name="prefix"/> not in <paramref name="known"/> (catches typos).</summary>
    public static void ValidateKnownKeys(IReadOnlyDictionary<string, string>? metadata, string prefix, IReadOnlySet<string> known, List<string> problems)
    {
        if (metadata is null)
        {
            return;
        }

        foreach (var key in metadata.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && !known.Contains(key))
            {
                problems.Add($"unknown key '{key}'");
            }
        }
    }
}
