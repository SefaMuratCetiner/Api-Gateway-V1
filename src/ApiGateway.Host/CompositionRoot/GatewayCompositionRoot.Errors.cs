using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors.Handling;
using ApiGateway.Errors.Localization;
using ApiGateway.Errors.Problems;
using ApiGateway.Errors.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    /// <summary>Request-timeout policy routes reference with <c>"TimeoutPolicy": "gateway-default"</c>.</summary>
    public const string DefaultRouteTimeoutPolicy = "gateway-default";

    private void ComposeErrors()
    {
        // Catalogue (validated at construction and against the resources at startup) and localization.
        Services.AddSingleton<IErrorCatalogContributor, BuiltInErrorCatalogContributor>();
        Services.AddSingleton<IGatewayErrorCatalog, GatewayErrorCatalog>();
        Services.AddSingleton<IErrorMessageLocalizer, ResxErrorMessageLocalizer>();
        Services.AddSingleton<AcceptLanguageResolver>();
        Services.AddHostedService<ErrorCatalogStartupValidator>();

        // The one problem writer, and the components that use it.
        Services.AddSingleton<ErrorMetrics>();
        Services.AddSingleton<IProblemResponseWriter, ProblemResponseWriter>();
        Services.AddSingleton<RouteTimeoutProblemWriter>();
        Services.AddSingleton<RateLimitRejectionProblemWriter>();
        Services.AddExceptionHandler<GatewayExceptionHandler>();

        // Route authorization failures as problem details (authentication schemes are added by the auth section).
        Services.AddAuthentication();
        Services.AddAuthorization();
        Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        Services.AddRequestTimeouts(options => options.AddPolicy(DefaultRouteTimeoutPolicy, new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(30),
            TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
            WriteTimeoutResponse = static context => context.RequestServices.GetRequiredService<RouteTimeoutProblemWriter>().WriteAsync(context),
        }));
    }
}
