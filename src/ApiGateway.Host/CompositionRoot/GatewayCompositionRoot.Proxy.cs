using ApiGateway.Observability.Metrics;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Transforms;
using Yarp.ReverseProxy.Health;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    public const string ReverseProxySection = "ReverseProxy";

    private void ComposeProxy()
    {
        Services.AddReverseProxy()
            .LoadFromConfig(Configuration.GetSection(ReverseProxySection))
            .AddConfigFilter<RouteMetadataValidationFilter>()
            .AddTransforms<UpstreamResponseMarkerTransformProvider>();

        // Upstream timings from YARP's forwarder events.
        Services.AddTelemetryConsumer<UpstreamTimingTelemetryConsumer>();

        // Destination health transitions become metrics and log events.
        ServiceDecoration.Decorate<IDestinationHealthUpdater>(Services, static (services, inner) => new DestinationHealthChangeRecorder(
            inner,
            services.GetRequiredService<GatewayMetrics>(),
            services.GetRequiredService<ILogger<DestinationHealthChangeRecorder>>()));
    }

    private void ComposeKestrel()
    {
        var listeners = Listeners;
        var limits = Errors.ProtocolLimits;
        _builder.WebHost.ConfigureKestrel(kestrel =>
        {
            foreach (var port in listeners.PublicPorts)
            {
                kestrel.ListenAnyIP(port);
            }

            kestrel.ListenAnyIP(listeners.InternalPort);

            // Kestrel's hard limits stay above the gateway's soft limits, so oversized requests reach middleware
            // and get a 414/431 problem body instead of Kestrel's empty response.
            kestrel.Limits.MaxRequestLineSize = Math.Max(kestrel.Limits.MaxRequestLineSize, limits.MaxUrlLength + 1024);
            kestrel.Limits.MaxRequestHeadersTotalSize = Math.Max(kestrel.Limits.MaxRequestHeadersTotalSize, limits.MaxRequestHeadersTotalSize + 2048);
            kestrel.Limits.MaxRequestHeaderCount = Math.Max(kestrel.Limits.MaxRequestHeaderCount, limits.MaxRequestHeaderCount + 10);
        });
    }
}
