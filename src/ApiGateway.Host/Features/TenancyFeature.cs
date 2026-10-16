using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.Tenancy;

namespace ApiGateway.Host.Features;

/// <summary>Tenant-specific destinations, with the tenant taken from identity or a route value.</summary>
internal sealed class TenancyFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway) =>
        gateway.AddRoutePolicy<TenantRoutingPolicy, TenantRoutingMetadataParser>();
}
