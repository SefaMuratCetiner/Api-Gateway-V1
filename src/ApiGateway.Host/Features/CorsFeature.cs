using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.Cors;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace ApiGateway.Host.Features;

/// <summary>CORS: named policies from configuration; routes select one with YARP's <c>CorsPolicy</c>.</summary>
internal sealed class CorsFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        gateway.Services.AddCors();
        gateway.Services.AddSingleton<IConfigureOptions<CorsOptions>, GatewayCorsSetup>();
    }
}
