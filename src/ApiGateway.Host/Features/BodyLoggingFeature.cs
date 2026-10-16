using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.BodyLogging;

namespace ApiGateway.Host.Features;

/// <summary>Opt-in, per-route request/response body logging (policy from route metadata).</summary>
internal sealed class BodyLoggingFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway) =>
        gateway.AddRoutePolicy<BodyLoggingPolicy, BodyLoggingMetadataParser>();
}
