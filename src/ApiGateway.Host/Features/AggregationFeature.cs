using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.Aggregation;

namespace ApiGateway.Host.Features;

/// <summary>Composite (BFF) endpoints; they are mapped with the other endpoints in the composition root.</summary>
internal sealed class AggregationFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway) =>
        gateway.Services.AddSingleton<AggregateRequestHandler>();
}
