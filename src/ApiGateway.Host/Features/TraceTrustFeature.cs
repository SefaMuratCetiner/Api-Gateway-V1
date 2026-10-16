using System.Diagnostics;
using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.Tracing;

namespace ApiGateway.Host.Features;

/// <summary>Whether an inbound trace context is trusted. Hosting resolves the propagator from DI when it creates the request span.</summary>
internal sealed class TraceTrustFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        gateway.Services.AddSingleton<TraceTrustEvaluator>();
        gateway.Services.AddSingleton<DistributedContextPropagator>(services => new TrustAwareContextPropagator(
            DistributedContextPropagator.Current,
            services.GetRequiredService<TraceTrustEvaluator>(),
            services.GetRequiredService<IHttpContextAccessor>()));
    }
}
