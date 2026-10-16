using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors.Handling;
using ApiGateway.Errors.Localization;
using ApiGateway.Errors.Problems;
using ApiGateway.Errors.Telemetry;
using ApiGateway.Host.CompositionRoot;

namespace ApiGateway.Host.Features;

/// <summary>Gateway-made errors: the catalog, localized messages, the one problem writer and the components that use it.</summary>
internal sealed class ErrorsFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var services = gateway.Services;

        // Catalog (validated at construction and against the resources at startup) and localization.
        services.AddSingleton<IErrorCatalogContributor, BuiltInErrorCatalogContributor>();
        services.AddSingleton<IGatewayErrorCatalog, GatewayErrorCatalog>();
        services.AddSingleton<IErrorMessageLocalizer, ResxErrorMessageLocalizer>();
        services.AddSingleton<AcceptLanguageResolver>();
        services.AddHostedService<ErrorCatalogStartupValidator>();

        services.AddSingleton<ErrorMetrics>();
        services.AddSingleton<IProblemResponseWriter, ProblemResponseWriter>();
        services.AddSingleton<RateLimitRejectionProblemWriter>();
        services.AddExceptionHandler<GatewayExceptionHandler>();
    }
}
