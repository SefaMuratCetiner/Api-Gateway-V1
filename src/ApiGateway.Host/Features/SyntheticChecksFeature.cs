using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.Synthetic;

namespace ApiGateway.Host.Features;

/// <summary>Synthetic probes through the public listener, and the health check that reports them.</summary>
internal sealed class SyntheticChecksFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var services = gateway.Services;
        var synthetic = gateway.Settings.Observability.Synthetic;

        services.AddSingleton<SyntheticCheckState>();
        services.AddHealthChecks().AddCheck<SyntheticChecksHealthCheck>("synthetic", tags: [SyntheticChecksHealthCheck.Tag]);

        if (!synthetic.Enabled)
        {
            return;
        }

        services.AddSingleton(provider => new SyntheticSignature(
            synthetic.SigningKey!,
            provider.GetRequiredService<TimeProvider>(),
            synthetic.MaxSignatureAge));
        services.AddSingleton<SyntheticMetrics>();
        services.AddHttpClient(SyntheticProbeService.HttpClientName);
        services.AddHostedService<SyntheticProbeService>();
    }
}
