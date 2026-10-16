using System.Diagnostics.Metrics;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.Export;
using ApiGateway.Observability.Metrics;
using ApiGateway.Observability.Tracing;
using ApiGateway.Observability.Tracing.Sampling;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ApiGateway.Host.Features;

/// <summary>OpenTelemetry: resource, head and tail sampling, instrumentation, metric views and exporters.</summary>
internal sealed class TelemetryFeature : IGatewayFeature
{
    /// <summary>YARP's activity source (forwarder spans).</summary>
    private const string YarpActivitySource = "Yarp.ReverseProxy";

    public void Register(GatewayBuilder gateway)
    {
        // The AspNetCore instrumentation re-extracts the inbound context unless the default propagator is plain
        // W3C trace context; with it, the trust decision made by TrustAwareContextPropagator stands.
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

        var settings = gateway.Settings;
        var observability = settings.Observability;
        gateway.Services.AddSingleton<TailSamplingMetrics>();
        gateway.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: observability.ServiceName,
                    serviceNamespace: observability.ServiceNamespace,
                    serviceVersion: typeof(TelemetryFeature).Assembly.GetName().Version?.ToString(),
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(
                [
                    new("deployment.environment.name", gateway.Environment.EnvironmentName),
                    new("gateway.log.retention_days", settings.Logging.File.RetainedDays),
                ]))
            .WithTracing(tracing => ComposeTracing(tracing, settings))
            .WithMetrics(metrics => ComposeMetrics(metrics, observability.Metrics, observability.Export));
    }

    private static void ComposeTracing(TracerProviderBuilder tracing, GatewaySettings settings)
    {
        var listeners = settings.Listeners;
        var tracingOptions = settings.Observability.Tracing;
        var export = settings.Observability.Export;
        tracing
            .SetSampler(new GatewaySampler(tracingOptions.SamplingRatio))
            .AddSource(GatewayTelemetry.Name, GatewayTelemetry.Wildcard, YarpActivitySource)
            .AddAspNetCoreInstrumentation(options =>
            {
                // Metrics scrapes and health probes on the internal listener are not traced.
                options.Filter = context => !listeners.IsInternal(context);
                options.EnrichWithHttpResponse = GatewayActivityEnricher.EnrichWithResponse;
            })
            .AddHttpClientInstrumentation();

        if (export.OtlpEndpoint is not { } endpoint)
        {
            return;
        }

        tracing.AddProcessor(services =>
        {
            var exporter = new OtlpTraceExporter(new OtlpExporterOptions
            {
                Endpoint = endpoint,
                Protocol = ToOtlpProtocol(export.OtlpProtocol),
            });

            // Bounded queue: when the collector is down spans are dropped, never the request blocked.
            var batch = new BatchActivityExportProcessor(exporter, maxQueueSize: export.MaxExportQueueSize, maxExportBatchSize: export.MaxExportBatchSize);
            return new TailSamplingProcessor(
                batch,
                new TailSamplingLimits(tracingOptions.SlowThreshold, tracingOptions.TailBufferMaxTraces, tracingOptions.TailBufferMaxSpansPerTrace),
                services.GetRequiredService<TailSamplingMetrics>());
        });
    }

    private static void ComposeMetrics(MeterProviderBuilder metrics, MetricsOptions options, ExportOptions export)
    {
        metrics
            .AddMeter(GatewayTelemetry.Name, GatewayTelemetry.Wildcard)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddView(instrument => ViewFor(instrument, options));

        if (export.PrometheusEnabled)
        {
            metrics.AddPrometheusExporter();
        }

        if (export.OtlpEndpoint is { } endpoint)
        {
            metrics.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = endpoint;
                exporter.Protocol = ToOtlpProtocol(export.OtlpProtocol);
            });
        }
    }

    /// <summary>
    /// Latency histograms (unit <c>s</c>) get the configured buckets so p50/p95/p99 are meaningful; every stream
    /// gets the cardinality backstop.
    /// </summary>
    private static MetricStreamConfiguration ViewFor(Instrument instrument, MetricsOptions options)
    {
        var isLatencyHistogram = instrument.GetType().IsGenericType
            && instrument.GetType().GetGenericTypeDefinition() == typeof(Histogram<>)
            && string.Equals(instrument.Unit, "s", StringComparison.Ordinal);

        return isLatencyHistogram
            ? new ExplicitBucketHistogramConfiguration { Boundaries = options.LatencyBucketsSeconds, CardinalityLimit = options.CardinalityLimit }
            : new MetricStreamConfiguration { CardinalityLimit = options.CardinalityLimit };
    }

    private static OtlpExportProtocol ToOtlpProtocol(OtlpTransport transport) =>
        transport == OtlpTransport.Grpc ? OtlpExportProtocol.Grpc : OtlpExportProtocol.HttpProtobuf;
}
