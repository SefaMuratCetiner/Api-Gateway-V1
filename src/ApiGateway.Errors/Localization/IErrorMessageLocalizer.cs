using System.Globalization;
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
