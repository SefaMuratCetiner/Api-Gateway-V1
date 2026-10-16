using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.Validation;

namespace ApiGateway.Host.Features;

/// <summary>Request validation against the backends' published contracts, re-fetched when they change.</summary>
internal sealed class ContractValidationFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        gateway.AddRoutePolicy<ContractValidationPolicy, ContractValidationMetadataParser>();
        gateway.Services.AddSingleton<ContractStore>();
        gateway.Services.AddHttpClient(ContractRefreshService.HttpClientName);
        gateway.Services.AddHostedService<ContractRefreshService>();
    }
}
