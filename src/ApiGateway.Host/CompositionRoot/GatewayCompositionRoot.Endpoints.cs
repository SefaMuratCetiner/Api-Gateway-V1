using ApiGateway.Errors.Endpoints;
using ApiGateway.Errors.Forwarding;
using ApiGateway.Observability.BodyLogging;
using ApiGateway.Observability.Synthetic;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    public const string MetricsPath = "/metrics";
    public const string SyntheticHealthPath = "/health/synthetic";

    /// <summary>
    /// Public listener: proxied routes only. Internal listener: metrics, error catalogue, synthetic health.
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
                proxy.UseMiddleware<BodyCaptureMiddleware>();
                proxy.UseMiddleware<ForwarderErrorProblemMiddleware>();
                proxy.UseSessionAffinity();
                proxy.UseLoadBalancing();
                proxy.UsePassiveHealthChecks();
            })
            .ConfigureEndpoints((endpoint, route) =>
            {
                // Routes without explicit hosts are bound to the public ports.
                if (route.Match.Hosts is not { Count: > 0 })
                {
                    endpoint.RequireHost(publicHosts);
                }
            });

        app.MapGet(ErrorCatalogEndpoint.Pattern, ErrorCatalogEndpoint.Handle)
            .RequireHost(internalHost)
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
}
