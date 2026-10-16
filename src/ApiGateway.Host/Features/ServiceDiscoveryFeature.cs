using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.Routing;
using Yarp.ReverseProxy.ServiceDiscovery;

namespace ApiGateway.Host.Features;

/// <summary>Destination host names resolved through DNS (Kubernetes services, Consul DNS, plain DNS).</summary>
internal sealed class ServiceDiscoveryFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var discovery = gateway.Settings.Routing.DnsDiscovery;
        if (!discovery.Enabled)
        {
            return;
        }

        var refresh = discovery.RefreshPeriod;
        gateway.Proxy.AddDnsDestinationResolver(options => options.RefreshPeriod = refresh);
        gateway.Decorate<IDestinationResolver>(static (services, inner) => new ResilientDestinationResolver(
            inner,
            services.GetRequiredService<ILogger<ResilientDestinationResolver>>()));
    }
}
