using ApiGateway.Observability.Redaction;
using ApiGateway.Observability.Serilog;
using ApiGateway.Observability.Serilog.AccessLog;
using Serilog;
using Serilog.Core;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    private void ComposeLogging()
    {
        // Serilog replaces the default providers; every library keeps logging through ILogger<T>.
        Services.AddSerilog((services, configuration) => GatewayLoggerConfigurator.Configure(
            configuration,
            Logging,
            Observability,
            services.GetRequiredService<SensitiveDataClassifier>(),
            Environment,
            services.GetServices<ILogEventSink>()));

        Services.AddSingleton<AccessLogEnricher>();
    }
}
