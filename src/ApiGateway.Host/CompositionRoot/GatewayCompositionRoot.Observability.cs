using System.Diagnostics;
using ApiGateway.Abstractions.Audit;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Errors;
using ApiGateway.Observability;
using ApiGateway.Observability.Audit;
using ApiGateway.Observability.BodyLogging;
using ApiGateway.Observability.Metrics;
using ApiGateway.Observability.Redaction;
using ApiGateway.Observability.Serilog;
using ApiGateway.Observability.Synthetic;
using ApiGateway.Observability.Tracing;
using ApiGateway.Observability.Tracing.Sampling;
using ApiGateway.Observability.Usage;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    private void ComposeObservability()
    {
        Services.AddSingleton(TimeProvider.System);
        Services.AddHttpContextAccessor();

        // Redaction and anonymization (shared by the access log, body logging and the Serilog sink).
        Services.AddSingleton<SensitiveDataClassifier>();
        Services.AddSingleton<QueryStringRedactor>();
        Services.AddSingleton<HeaderRedactor>();
        Services.AddSingleton<JsonBodyRedactor>();
        Services.AddSingleton(ClientIpAnonymizerFactory.Create(Observability.ClientIp));

        // Trace trust: hosting resolves the propagator from DI when it creates the request span.
        Services.AddSingleton<TraceTrustEvaluator>();
        Services.AddSingleton<DistributedContextPropagator>(services => new TrustAwareContextPropagator(
            DistributedContextPropagator.Current,
            services.GetRequiredService<TraceTrustEvaluator>(),
            services.GetRequiredService<IHttpContextAccessor>()));

        // Metrics.
        Services.AddSingleton<CardinalityGuard>();
        Services.AddSingleton<GatewayMetrics>();
        Services.AddSingleton<IGatewayMetrics>(services => services.GetRequiredService<GatewayMetrics>());
        Services.AddHostedService<ClusterHealthGauge>();
        Services.AddSingleton<TailSamplingMetrics>();

        // Per-route body logging (policy from route metadata, validated when YARP loads config).
        AddRoutePolicy<BodyLoggingPolicy, BodyLoggingMetadataParser>();

        // Audit trail of configuration changes.
        Services.AddSingleton<IAuditLog, AuditLog>();
        Services.AddSingleton<IConfigChangeListener, ProxyConfigAuditListener>();
        Services.AddHostedService<OptionsChangeAuditor<ErrorsOptions>>();
        Services.AddHostedService<OptionsChangeAuditor<ObservabilityOptions>>();
        Services.AddHostedService<OptionsChangeAuditor<LoggingOptions>>();

        // Usage analytics.
        Services.AddSingleton<UsageAggregator>();
        Services.AddSingleton<IUsageSink, LoggingUsageSink>();
        Services.AddHostedService<UsageFlushService>();

        ComposeSyntheticChecks();
    }

    private void ComposeSyntheticChecks()
    {
        var synthetic = Observability.Synthetic;

        Services.AddSingleton<SyntheticCheckState>();
        Services.AddHealthChecks().AddCheck<SyntheticChecksHealthCheck>("synthetic", tags: [SyntheticChecksHealthCheck.Tag]);

        if (!synthetic.Enabled)
        {
            return;
        }

        Services.AddSingleton(services => new SyntheticSignature(
            synthetic.SigningKey!,
            services.GetRequiredService<TimeProvider>(),
            synthetic.MaxSignatureAge));
        Services.AddSingleton<SyntheticMetrics>();
        Services.AddHttpClient(SyntheticProbeService.HttpClientName);
        Services.AddHostedService<SyntheticProbeService>();
    }
}
