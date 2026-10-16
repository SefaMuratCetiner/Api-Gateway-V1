using System.Diagnostics.Metrics;
using ApiGateway.Abstractions.Telemetry;
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

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    /// <summary>YARP's activity source (forwarder spans).</summary>
    private const string YarpActivitySource = "Yarp.ReverseProxy";

    private void ComposeTelemetry()
    {
        // The AspNetCore instrumentation re-extracts the inbound context unless the default propagator is plain
        // W3C trace context; with it, the trust decision made by TrustAwareContextPropagator stands.
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

        var export = Observability.Export;
        Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: Observability.ServiceName,
                    serviceNamespace: Observability.ServiceNamespace,
                    serviceVersion: typeof(GatewayCompositionRoot).Assembly.GetName().Version?.ToString(),
                    serviceInstanceId: System.Environment.MachineName)
                .AddAttributes(
                [
                    new("deployment.environment.name", Environment.EnvironmentName),
                    new("gateway.log.retention_days", Logging.File.RetainedDays),
                ]))
            .WithTracing(tracing => ComposeTracing(tracing, export))
            .WithMetrics(metrics => ComposeMetrics(metrics, export));
    }

    private void ComposeTracing(TracerProviderBuilder tracing, ExportOptions export)
    {
        var listeners = Listeners;
        tracing
            .SetSampler(new GatewaySampler(Observability.Tracing.SamplingRatio))
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

        var tracingOptions = Observability.Tracing;
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

    private void ComposeMetrics(MeterProviderBuilder metrics, ExportOptions export)
    {
        var options = Observability.Metrics;
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
