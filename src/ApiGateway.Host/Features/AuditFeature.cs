using ApiGateway.Abstractions.Audit;
using ApiGateway.Errors;
using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability;
using ApiGateway.Observability.Audit;
using ApiGateway.Observability.Serilog;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Host.Features;

/// <summary>Audit trail of configuration changes: proxy routes and clusters, and the reloadable options sections.</summary>
internal sealed class AuditFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var services = gateway.Services;
        services.AddSingleton<IAuditLog, AuditLog>();
        services.AddSingleton<IConfigChangeListener, ProxyConfigAuditListener>();
        services.AddHostedService<OptionsChangeAuditor<ErrorsOptions>>();
        services.AddHostedService<OptionsChangeAuditor<ObservabilityOptions>>();
        services.AddHostedService<OptionsChangeAuditor<LoggingOptions>>();
    }
}
