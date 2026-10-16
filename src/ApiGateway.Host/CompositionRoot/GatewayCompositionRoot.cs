using ApiGateway.Host.Features;
using ApiGateway.Proxy.Cors;
using ApiGateway.Proxy.Headers;
using ApiGateway.Proxy.Limits;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Transforms;

namespace ApiGateway.Host.CompositionRoot;

/// <summary>
/// The gateway's composition root: the one place where object graphs are composed. Libraries expose
/// constructor-injected types and never register themselves. The work is split three ways:
/// <list type="bullet">
/// <item><see cref="GatewaySettings"/>: every configuration section, bound, validated and registered once.</item>
/// <item><c>Features/</c>: one <see cref="IGatewayFeature"/> per capability, holding its service registrations.</item>
/// <item>This class: everything whose order matters, readable top to bottom. <see cref="Features"/> (registration
/// order) and YARP's config filters and transforms here; the middleware order in <c>.Pipeline</c>; the proxy
/// pipeline and the endpoints on each listener in <c>.Endpoints</c>.</item>
/// </list>
/// </summary>
public sealed partial class GatewayCompositionRoot
{
    public const string ReverseProxySection = "ReverseProxy";

    private readonly WebApplicationBuilder _builder;

    public GatewayCompositionRoot(WebApplicationBuilder builder)
    {
        _builder = builder;
        Settings = new GatewaySettings(builder.Services, builder.Configuration, builder.Environment);
    }

    /// <summary>
    /// Every feature, in registration order. Order matters only where a feature decorates a service another one
    /// registers; independent features are grouped by concern.
    /// </summary>
    internal static IReadOnlyList<IGatewayFeature> Features { get; } =
    [
        // Hosting
        new ListenersFeature(),
        new LimitsFeature(),

        // Observability
        new RedactionFeature(),
        new LoggingFeature(),
        new TraceTrustFeature(),
        new TelemetryFeature(),
        new MetricsFeature(),
        new BodyLoggingFeature(),
        new AuditFeature(),
        new UsageFeature(),
        new SyntheticChecksFeature(),

        // Errors, authorization, timeouts
        new ErrorsFeature(),
        new AuthorizationFeature(),
        new RequestTimeoutsFeature(),

        // Proxy
        new ServiceDiscoveryFeature(),
        new LoadBalancingFeature(),
        new TenancyFeature(),
        new ApiLifecycleFeature(),
        new CorsFeature(),
        new CompressionFeature(),
        new ContractValidationFeature(),
        new AggregationFeature(),
    ];

    internal GatewaySettings Settings { get; }

    private IServiceCollection Services => _builder.Services;

    /// <summary>Registers every service: shared infrastructure, YARP, then each feature in order.</summary>
    public void ComposeServices()
    {
        Services.AddSingleton(TimeProvider.System);
        Services.AddHttpContextAccessor();

        // Config filters run in order: conventions (hosts, methods, prefix, destination metadata), then route metadata.
        // Response transforms run in registration order: the upstream marker first, so later ones can undo it.
        var proxy = Services.AddReverseProxy()
            .LoadFromConfig(_builder.Configuration.GetSection(ReverseProxySection))
            .AddConfigFilter<ProxyConventionsFilter>()
            .AddConfigFilter<RouteMetadataValidationFilter>()
            .AddTransforms<UpstreamResponseMarkerTransformProvider>()
            .AddTransforms<ResponseHeaderStrippingTransformProvider>()
            .AddTransforms<UpstreamCorsHeaderTransformProvider>()
            .AddTransforms<PathRewriteTransformProvider>()
            .AddTransforms<ResponseLimitsTransformProvider>();

        var gateway = new GatewayBuilder(_builder, Settings, proxy);
        foreach (var feature in Features)
        {
            feature.Register(gateway);
        }
    }
}
