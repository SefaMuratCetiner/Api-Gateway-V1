using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.Limits;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Host.Features;

/// <summary>Kestrel's hard limits, the backend response-header limit and the per-route response limit.</summary>
internal sealed class LimitsFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var limits = gateway.Settings.Limits;
        var protocolLimits = limits.Protocol;
        gateway.WebHost.ConfigureKestrel(kestrel =>
        {
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

        gateway.Services.AddSingleton<IForwarderHttpClientFactory, LimitedForwarderHttpClientFactory>();
        gateway.AddRoutePolicy<ResponseLimitsPolicy, ResponseLimitsMetadataParser>();
    }
}
