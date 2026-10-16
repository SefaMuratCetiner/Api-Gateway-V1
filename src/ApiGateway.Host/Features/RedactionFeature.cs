using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.Redaction;

namespace ApiGateway.Host.Features;

/// <summary>Redaction and client-IP anonymization, shared by the access log, body logging and the Serilog sink.</summary>
internal sealed class RedactionFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var services = gateway.Services;
        services.AddSingleton<SensitiveDataClassifier>();
        services.AddSingleton<QueryStringRedactor>();
        services.AddSingleton<HeaderRedactor>();
        services.AddSingleton<JsonBodyRedactor>();
        services.AddSingleton(ClientIpAnonymizerFactory.Create(gateway.Settings.Observability.ClientIp));
    }
}
