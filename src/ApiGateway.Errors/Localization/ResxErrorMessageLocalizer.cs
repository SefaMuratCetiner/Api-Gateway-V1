using System.Globalization;
using System.Resources;
using ApiGateway.Abstractions.Errors;

namespace ApiGateway.Errors.Localization;

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
