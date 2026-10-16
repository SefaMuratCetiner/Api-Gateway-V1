using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Observability.Export;
using ApiGateway.Observability.Redaction;
using ApiGateway.Observability.Serilog.Redaction;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Extensions.Hosting;
using Serilog.Filters;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace ApiGateway.Observability.Serilog;

/// <summary>
/// Builds the gateway's Serilog pipeline:
/// <list type="bullet">
/// <item>main logger: console, rolling file (retention) and OTLP, excluding audit and usage events;</item>
/// <item>audit sub-logger: <c>ApiGateway.Audit</c> to its own file with long retention (and OTLP);</item>
/// <item>usage sub-logger: <c>ApiGateway.Usage</c> to its own file (and OTLP).</item>
/// </list>
/// Every sink sits behind <c>Async</c> (bounded buffer, drops instead of blocking) and <see cref="RedactingSink"/>,
/// including additional sinks registered in DI (e.g. by tests or a log-forwarding extension).
/// </summary>
public static class GatewayLoggerConfigurator
{
    private const string TextTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {CorrelationId}{NewLine}{Exception}";

    /// <summary>Console-only logger for the time before configuration is loaded; replaced once the host is built.</summary>
    public static ReloadableLogger CreateBootstrapLogger()
    {
        var classifier = new SensitiveDataClassifier(new RedactionOptions());
        var redactor = new LogEventRedactor(classifier);
        var configuration = new LoggerConfiguration().MinimumLevel.Information();
        Redacted(configuration.WriteTo, redactor, wrapped => wrapped.Console(new RenderedCompactJsonFormatter()));
        return configuration.CreateBootstrapLogger();
    }

    public static LoggerConfiguration Configure(
        LoggerConfiguration configuration,
        LoggingOptions logging,
        ObservabilityOptions observability,
        SensitiveDataClassifier classifier,
        IHostEnvironment environment,
        IEnumerable<ILogEventSink>? additionalSinks = null)
    {
        var redactor = new LogEventRedactor(classifier);

        configuration.MinimumLevel.Is(logging.MinimumLevel);
        foreach (var (source, level) in logging.Overrides)
        {
            configuration.MinimumLevel.Override(source, level);
        }

        configuration
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", observability.ServiceName)
            .Enrich.WithProperty("Environment", environment.EnvironmentName);

        var isAudit = Matching.FromSource(GatewayTelemetry.AuditLogCategory);
        var isUsage = Matching.FromSource(GatewayTelemetry.UsageLogCategory);

        configuration.WriteTo.Logger(main => main
            .Filter.ByExcluding(logEvent => isAudit(logEvent) || isUsage(logEvent))
            .WriteTo.Async(
                sinks => Redacted(sinks, redactor, wrapped => ConfigureMainSinks(wrapped, logging, observability, environment)),
                bufferSize: logging.AsyncBufferSize,
                blockWhenFull: false));

        foreach (var sink in additionalSinks ?? [])
        {
            configuration.WriteTo.Sink(new RedactingSink(sink, redactor));
        }

        if (logging.Audit.Enabled)
        {
            configuration.WriteTo.Logger(audit => audit
                .Filter.ByIncludingOnly(isAudit)
                .WriteTo.Async(
                    sinks => Redacted(sinks, redactor, wrapped => ConfigureCategorySinks(wrapped, logging.Audit, logging, observability, environment)),
                    bufferSize: logging.AsyncBufferSize,
                    blockWhenFull: false));
        }

        if (logging.Usage.Enabled)
        {
            configuration.WriteTo.Logger(usage => usage
                .Filter.ByIncludingOnly(isUsage)
                .WriteTo.Async(
                    sinks => Redacted(sinks, redactor, wrapped => ConfigureCategorySinks(wrapped, logging.Usage, logging, observability, environment)),
                    bufferSize: logging.AsyncBufferSize,
                    blockWhenFull: false));
        }

        return configuration;
    }

    private static void Redacted(LoggerSinkConfiguration sinks, LogEventRedactor redactor, Action<LoggerSinkConfiguration> configure) =>
        sinks.Sink(LoggerSinkConfiguration.Wrap(sink => new RedactingSink(sink, redactor), configure));

    private static void ConfigureMainSinks(LoggerSinkConfiguration sinks, LoggingOptions logging, ObservabilityOptions observability, IHostEnvironment environment)
    {
        if (logging.Console.Enabled)
        {
            if (logging.Console.Format == ConsoleFormat.Json)
            {
                sinks.Console(new RenderedCompactJsonFormatter());
            }
            else
            {
                sinks.Console(outputTemplate: TextTemplate, formatProvider: System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (logging.File.Enabled)
        {
            AddFile(sinks, logging.File);
        }

        AddOtlp(sinks, logging, observability, environment);
    }

    private static void ConfigureCategorySinks(LoggerSinkConfiguration sinks, FileSinkOptions file, LoggingOptions logging, ObservabilityOptions observability, IHostEnvironment environment)
    {
        AddFile(sinks, file);
        AddOtlp(sinks, logging, observability, environment);
    }

    private static void AddFile(LoggerSinkConfiguration sinks, FileSinkOptions file) =>
        sinks.File(
            new CompactJsonFormatter(),
            file.Path,
            rollingInterval: RollingInterval.Day,
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: file.FileSizeLimitBytes,
            retainedFileCountLimit: file.RetainedFileCountLimit,
            retainedFileTimeLimit: TimeSpan.FromDays(file.RetainedDays),
            shared: false);

    private static void AddOtlp(LoggerSinkConfiguration sinks, LoggingOptions logging, ObservabilityOptions observability, IHostEnvironment environment)
    {
        if (!logging.Otlp.Enabled || observability.Export.OtlpEndpoint is not { } endpoint)
        {
            return;
        }

        sinks.OpenTelemetry(options =>
        {
            options.Endpoint = endpoint.ToString();
            options.Protocol = observability.Export.OtlpProtocol == OtlpTransport.Grpc ? OtlpProtocol.Grpc : OtlpProtocol.HttpProtobuf;
            options.ResourceAttributes = ResourceAttributes(observability, environment, logging);
            options.BatchingOptions.QueueLimit = logging.Otlp.QueueLimit;
        });
    }

    /// <summary>OTel resource attributes shared by the log exporter (traces and metrics use the same values).</summary>
    public static Dictionary<string, object> ResourceAttributes(ObservabilityOptions observability, IHostEnvironment environment, LoggingOptions logging)
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["service.name"] = observability.ServiceName,
            ["service.instance.id"] = Environment.MachineName,
            ["deployment.environment.name"] = environment.EnvironmentName,
            ["gateway.log.retention_days"] = logging.File.RetainedDays,
        };
        if (observability.ServiceNamespace is { } ns)
        {
            attributes["service.namespace"] = ns;
        }

        return attributes;
    }
}
