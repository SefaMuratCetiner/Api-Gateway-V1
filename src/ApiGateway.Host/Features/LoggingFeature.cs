using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.Redaction;
using ApiGateway.Observability.Serilog;
using ApiGateway.Observability.Serilog.AccessLog;
using Serilog;
using Serilog.Core;

namespace ApiGateway.Host.Features;

/// <summary>Serilog in place of the default providers (every library keeps logging through ILogger&lt;T&gt;), and the access log.</summary>
internal sealed class LoggingFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var settings = gateway.Settings;
        var environment = gateway.Environment;
        gateway.Services.AddSerilog((services, configuration) => GatewayLoggerConfigurator.Configure(
            configuration,
            settings.Logging,
            settings.Observability,
            services.GetRequiredService<SensitiveDataClassifier>(),
            environment,
            services.GetServices<ILogEventSink>()));

        gateway.Services.AddSingleton<AccessLogEnricher>();
    }
}
