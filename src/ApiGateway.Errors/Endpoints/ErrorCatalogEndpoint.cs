using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ApiGateway.Errors.Endpoints;

/// <summary>One entry of the published error catalogue.</summary>
public sealed record ErrorCatalogEntry(string Code, int Status, string Type, string Title, string? Detail);

/// <summary>
/// <c>GET /internal/errors</c> on the internal listener: every gateway error code with its status and text in
/// the requested language. The source for the documentation's error catalogue.
/// </summary>
public static class ErrorCatalogEndpoint
{
    public const string Pattern = "/internal/errors";

    public static IResult Handle(
        HttpContext context,
        IGatewayErrorCatalog catalog,
        IErrorMessageLocalizer localizer,
        AcceptLanguageResolver languages,
        IOptions<ErrorsOptions> options)
    {
        var culture = languages.Resolve(context);
        var entries = catalog.Errors
            .OrderBy(static error => error.Status)
            .ThenBy(static error => error.Code, StringComparer.Ordinal)
            .Select(error =>
            {
                var text = localizer.Localize(error, culture);
                return new ErrorCatalogEntry(error.Code, error.Status, options.Value.TypeBaseUri + error.Code, text.Title, text.Detail);
            })
            .ToArray();

        context.Response.Headers.ContentLanguage = culture.Name;
        return Results.Ok(entries);
    }
}
