using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.Usage;

namespace ApiGateway.Host.Features;

/// <summary>Per-consumer usage analytics, aggregated in memory and flushed to the usage sink.</summary>
internal sealed class UsageFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        gateway.Services.AddSingleton<UsageAggregator>();
        gateway.Services.AddSingleton<IUsageSink, LoggingUsageSink>();
        gateway.Services.AddHostedService<UsageFlushService>();
    }
}
