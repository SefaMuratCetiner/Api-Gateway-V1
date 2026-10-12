using System.Globalization;
using System.Resources;
using ApiGateway.Abstractions.Errors;

namespace ApiGateway.Errors.Localization;

/// <summary>Title and detail of an error in one culture.</summary>
/// <param name="Title">Short summary, never empty.</param>
/// <param name="Detail">Longer explanation, if the error has one.</param>
public sealed record LocalizedErrorText(string Title, string? Detail);

/// <summary>
/// Source of localized error messages. The default reads embedded resources; a database- or CMS-backed
/// implementation can be swapped in at the composition root.
/// </summary>
public interface IErrorMessageLocalizer
{
    LocalizedErrorText Localize(GatewayError error, CultureInfo culture);

    /// <summary>Resource keys missing for <paramref name="culture"/> (used by startup validation).</summary>
    IReadOnlyList<string> FindMissingKeys(IEnumerable<GatewayError> errors, CultureInfo culture);
}

/// <summary>Reads <c>Resources/ErrorMessages*.resx</c>.</summary>
public sealed class ResxErrorMessageLocalizer : IErrorMessageLocalizer
{
    private static readonly ResourceManager Resources =
        new("ApiGateway.Errors.Resources.ErrorMessages", typeof(ResxErrorMessageLocalizer).Assembly);

    public LocalizedErrorText Localize(GatewayError error, CultureInfo culture)
    {
        var title = Resources.GetString(error.TitleKey, culture);
        var detail = error.DetailKey is null ? null : Resources.GetString(error.DetailKey, culture);

        // Codes registered without resources still get a readable title rather than an empty one.
        return new LocalizedErrorText(string.IsNullOrEmpty(title) ? error.Code : title, string.IsNullOrEmpty(detail) ? null : detail);
    }

    public IReadOnlyList<string> FindMissingKeys(IEnumerable<GatewayError> errors, CultureInfo culture)
    {
        // tryParents: false, so a culture whose satellite assembly lacks a key is reported instead of
        // silently falling back to English.
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var missing = new List<string>();
        foreach (var error in errors)
        {
            if (set?.GetString(error.TitleKey) is not { Length: > 0 })
            {
                missing.Add(error.TitleKey);
            }

            if (error.DetailKey is not null && set?.GetString(error.DetailKey) is not { Length: > 0 })
            {
                missing.Add(error.DetailKey);
            }
        }

        return missing;
    }
}
