using ApiGateway.Errors.Endpoints;
using ApiGateway.Errors.Forwarding;
using ApiGateway.Observability.BodyLogging;
using ApiGateway.Observability.Synthetic;
using ApiGateway.Proxy.Aggregation;
using ApiGateway.Proxy.Lifecycle;
using ApiGateway.Proxy.Limits;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Tenancy;
using ApiGateway.Proxy.Validation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    public const string MetricsPath = "/metrics";
    public const string SyntheticHealthPath = "/health/synthetic";

    /// <summary>
    /// Public listener: proxied routes and composite endpoints under <c>Routing:PublicPathPrefix</c> (and, in
    /// Development only, the error catalogue). Internal listener: metrics, error catalogue, synthetic health.
    /// Unmatched requests fall through to a 404 <c>gateway.route_not_found</c> problem.
    /// </summary>
    public void ComposeEndpoints(WebApplication app)
    {
        var publicHosts = Listeners.PublicHostPatterns();
        var internalHost = Listeners.InternalHostPattern();

        // The proxy pipeline, in order. YARP's own stages (affinity, load balancing, passive health) come last.
        app.MapReverseProxy(proxy =>
            {
                proxy.UseMiddleware<RouteContextMiddleware>();
                proxy.UseMiddleware<ApiLifecycleMiddleware>();
                proxy.UseMiddleware<RequestBodyLimitMiddleware>();
                proxy.UseMiddleware<RequestValidationMiddleware>();
                proxy.UseMiddleware<BodyCaptureMiddleware>();
                proxy.UseMiddleware<ForwarderErrorProblemMiddleware>();
                proxy.UseMiddleware<TenantRoutingMiddleware>();
                proxy.UseSessionAffinity();
                proxy.UseLoadBalancing();
                proxy.UsePassiveHealthChecks();
            })
            .ConfigureEndpoints((endpoint, route) =>
            {
                // Routes without explicit hosts (Development only) are bound to the public ports.
                if (route.Match.Hosts is not { Count: > 0 })
                {
                    endpoint.RequireHost(publicHosts);
                }
            });

        ComposeAggregateEndpoints(app, publicHosts);

        // Documentation endpoints: internal listener; also public in Development.
        string[] docsHosts = AllowWildcardHosts ? [internalHost, .. publicHosts] : [internalHost];
        app.MapGet(ErrorCatalogEndpoint.Pattern, ErrorCatalogEndpoint.Handle)
            .RequireHost(docsHosts)
            .ExcludeFromDescription();

        var syntheticHealth = new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains(SyntheticChecksHealthCheck.Tag),
        };
        app.MapHealthChecks(SyntheticHealthPath, syntheticHealth).RequireHost(internalHost);

        if (Observability.Export.PrometheusEnabled)
        {
            app.MapPrometheusScrapingEndpoint(MetricsPath).RequireHost(internalHost);
        }
    }

    private void ComposeAggregateEndpoints(WebApplication app, string[] publicHosts)
    {
        var handler = app.Services.GetRequiredService<AggregateRequestHandler>();
        foreach (var (id, route) in Aggregation.Routes)
        {
            app.MapMethods(route.Path, route.Methods, context => handler.HandleAsync(context, id, route))
                .RequireHost(route.Hosts.Length > 0 ? route.Hosts : publicHosts)
                .WithMetadata(new AggregateRouteMetadata(id))
                .WithDisplayName("aggregate:" + id)
                .ExcludeFromDescription();
        }
    }
}
