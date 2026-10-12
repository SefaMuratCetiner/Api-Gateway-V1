using System.Collections.Frozen;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ApiGateway.Errors.Localization;

/// <summary>
/// Picks the culture of a gateway error message from <c>Accept-Language</c>: highest quality first, exact
/// match before parent culture (<c>tr-TR</c> → <c>tr</c>), falling back to the configured default.
/// Only affects gateway-made errors; the request's own culture is never changed.
/// </summary>
public sealed class AcceptLanguageResolver
{
    /// <summary>Entries beyond this are ignored; a client gains nothing from sending more.</summary>
    private const int MaxLanguageRanges = 16;

    private readonly FrozenDictionary<string, CultureInfo> _supported;
    private readonly CultureInfo _default;

    public AcceptLanguageResolver(IOptions<ErrorsOptions> options)
    {
        _supported = options.Value.SupportedCultures
            .Select(static name => CultureInfo.GetCultureInfo(name))
            .DistinctBy(static culture => culture.Name, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(static culture => culture.Name, StringComparer.OrdinalIgnoreCase);
        _default = CultureInfo.GetCultureInfo(options.Value.DefaultCulture);
    }

    public CultureInfo Default => _default;

    public IReadOnlyCollection<CultureInfo> Supported => _supported.Values;

    public CultureInfo Resolve(HttpContext context)
    {
        IList<StringWithQualityHeaderValue> ranges;
        try
        {
            ranges = context.Request.GetTypedHeaders().AcceptLanguage;
        }
        catch (FormatException)
        {
            return _default;
        }

        return Resolve(ranges);
    }

    public CultureInfo Resolve(IEnumerable<StringWithQualityHeaderValue> ranges)
    {
        var ordered = ranges
            .Take(MaxLanguageRanges)
            .Where(static range => range.Quality is null or > 0)
            .OrderByDescending(static range => range.Quality ?? 1);

        foreach (var range in ordered)
        {
            var tag = range.Value.Value;
            if (string.IsNullOrEmpty(tag) || string.Equals(tag, "*", StringComparison.Ordinal))
            {
                return _default;
            }

            if (Match(tag) is { } culture)
            {
                return culture;
            }
        }

        return _default;
    }

    private CultureInfo? Match(string tag)
    {
        // Walk up the hierarchy: zh-Hant-TW → zh-Hant → zh.
        var candidate = tag;
        while (candidate.Length > 0)
        {
            if (_supported.TryGetValue(candidate, out var culture))
            {
                return culture;
            }

            var dash = candidate.LastIndexOf('-');
            if (dash <= 0)
            {
                break;
            }

            candidate = candidate[..dash];
        }

        return null;
    }
}
