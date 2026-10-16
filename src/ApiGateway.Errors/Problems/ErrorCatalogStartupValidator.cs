using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors.Localization;
using Microsoft.Extensions.Hosting;

namespace ApiGateway.Errors.Problems;

/// <summary>
/// Fails startup when the catalog is invalid (resolving it runs its own checks) or when any supported culture
/// lacks a title or detail for any registered code. A missing translation is a deployment bug, not something to
/// discover from a client's error response.
/// </summary>
public sealed class ErrorCatalogStartupValidator(
    IGatewayErrorCatalog catalog,
    IErrorMessageLocalizer localizer,
    AcceptLanguageResolver languages) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var problems = languages.Supported
            .SelectMany(culture => localizer.FindMissingKeys(catalog.Errors, culture)
                .Select(key => $"{(culture.Name.Length == 0 ? "invariant" : culture.Name)}:{key}"))
            .ToList();

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Missing error message resources: " + string.Join(", ", problems));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
