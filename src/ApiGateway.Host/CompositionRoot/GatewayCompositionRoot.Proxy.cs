using ApiGateway.Observability.Metrics;
using ApiGateway.Proxy.Cors;
using ApiGateway.Proxy.Headers;
using ApiGateway.Proxy.Lifecycle;
using ApiGateway.Proxy.Limits;
using ApiGateway.Proxy.LoadBalancing;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Tenancy;
using ApiGateway.Proxy.Transforms;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.ServiceDiscovery;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    public const string ReverseProxySection = "ReverseProxy";

    private void ComposeProxy()
    {
        // Config filters run in order: conventions (hosts, methods, prefix, destination metadata), then route metadata.
        // Response transforms run in registration order: the upstream marker first, so later ones can undo it.
        var proxy = Services.AddReverseProxy()
            .LoadFromConfig(Configuration.GetSection(ReverseProxySection))
            .AddConfigFilter<ProxyConventionsFilter>()
            .AddConfigFilter<RouteMetadataValidationFilter>()
            .AddTransforms<UpstreamResponseMarkerTransformProvider>()
            .AddTransforms<ResponseHeaderStrippingTransformProvider>()
            .AddTransforms<UpstreamCorsHeaderTransformProvider>()
            .AddTransforms<PathRewriteTransformProvider>()
            .AddTransforms<ResponseLimitsTransformProvider>();

        // Service discovery: destination host names resolved through DNS (Kubernetes services, Consul DNS, plain DNS).
        if (Routing.DnsDiscovery.Enabled)
        {
            var refresh = Routing.DnsDiscovery.RefreshPeriod;
            proxy.AddDnsDestinationResolver(options => options.RefreshPeriod = refresh);
            ServiceDecoration.Decorate<IDestinationResolver>(Services, static (services, inner) => new ResilientDestinationResolver(
                inner,
                services.GetRequiredService<ILogger<ResilientDestinationResolver>>()));
        }

        // Canary / weighted splitting, and the backend response-header limit.
        Services.AddSingleton<ILoadBalancingPolicy, WeightedLoadBalancingPolicy>();
        Services.AddSingleton<IForwarderHttpClientFactory, LimitedForwarderHttpClientFactory>();

        // Per-route policies read from route metadata (validated when YARP loads config).
        AddRoutePolicy<ApiLifecyclePolicy, ApiLifecycleMetadataParser>();
        AddRoutePolicy<TenantRoutingPolicy, TenantRoutingMetadataParser>();
        AddRoutePolicy<ResponseLimitsPolicy, ResponseLimitsMetadataParser>();

        // Upstream timings from YARP's forwarder events.
        Services.AddTelemetryConsumer<UpstreamTimingTelemetryConsumer>();

        // Destination health transitions become metrics and log events.
        ServiceDecoration.Decorate<IDestinationHealthUpdater>(Services, static (services, inner) => new DestinationHealthChangeRecorder(
            inner,
            services.GetRequiredService<GatewayMetrics>(),
            services.GetRequiredService<ILogger<DestinationHealthChangeRecorder>>()));
    }

    /// <summary>Registers a feature's route-metadata parser (also as validator) and its policy resolver.</summary>
    private void AddRoutePolicy<TPolicy, TParser>()
        where TPolicy : class
        where TParser : class, IRouteMetadataParser<TPolicy>
    {
        Services.AddSingleton<TParser>();
        Services.AddSingleton<IRouteMetadataParser<TPolicy>>(services => services.GetRequiredService<TParser>());
        Services.AddSingleton<IRouteMetadataValidator>(services => services.GetRequiredService<TParser>());
        Services.AddSingleton<RoutePolicyResolver<TPolicy>>();
    }

    private void ComposeKestrel()
    {
        var listeners = Listeners;
        var protocolLimits = Errors.ProtocolLimits;
        var limits = Limits;
        _builder.WebHost.ConfigureKestrel(kestrel =>
        {
            foreach (var port in listeners.PublicPorts)
            {
                kestrel.ListenAnyIP(port);
            }

            kestrel.ListenAnyIP(listeners.InternalPort);

            // No "Server: Kestrel"; backend stack headers are stripped by ResponseHeaderStrippingTransformProvider.
            kestrel.AddServerHeader = false;

            // Kestrel's hard limits stay above the gateway's soft limits, so oversized requests reach middleware
            // and get a 414/431 problem body instead of Kestrel's empty response.
            kestrel.Limits.MaxRequestLineSize = Math.Max(kestrel.Limits.MaxRequestLineSize, protocolLimits.MaxUrlLength + 1024);
            kestrel.Limits.MaxRequestHeadersTotalSize = Math.Max(kestrel.Limits.MaxRequestHeadersTotalSize, protocolLimits.MaxRequestHeadersTotalSize + 2048);
            kestrel.Limits.MaxRequestHeaderCount = Math.Max(kestrel.Limits.MaxRequestHeaderCount, protocolLimits.MaxRequestHeaderCount + 10);

            // Body cap (routes override it with YARP's MaxRequestBodySize), connections, HTTP/2 streams, slow clients.
            var server = limits.Server;
            kestrel.Limits.MaxRequestBodySize = limits.MaxRequestBodySize;
            kestrel.Limits.MaxConcurrentConnections = server.MaxConcurrentConnections;
            kestrel.Limits.MaxConcurrentUpgradedConnections = server.MaxConcurrentUpgradedConnections;
            kestrel.Limits.Http2.MaxStreamsPerConnection = server.Http2MaxStreamsPerConnection;
            kestrel.Limits.MinRequestBodyDataRate = server.MinRequestBodyDataRate is { } rate
                ? new MinDataRate(rate, server.MinRequestBodyDataRateGracePeriod)
                : null;
        });
    }
}
