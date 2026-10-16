using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.ApiLifecycle;

namespace ApiGateway.Host.Features;

/// <summary>API versions, deprecation and sunset headers, and 410 for retired versions.</summary>
internal sealed class ApiLifecycleFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway) =>
        gateway.AddRoutePolicy<ApiLifecyclePolicy, ApiLifecycleMetadataParser>();
}
