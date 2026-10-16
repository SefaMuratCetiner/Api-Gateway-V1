using ApiGateway.Errors;
using ApiGateway.Observability;
using ApiGateway.Observability.Serilog;
using ApiGateway.Proxy.Aggregation;
using ApiGateway.Proxy.Compression;
using ApiGateway.Proxy.Limits;
using ApiGateway.Proxy.Listeners;
using ApiGateway.Proxy.Routing;

namespace ApiGateway.Host.CompositionRoot;

/// <summary>
/// The gateway's composition root: the one place where object graphs are composed. Libraries expose
/// constructor-injected types and never register themselves; this class decides which implementations are used,
/// how they are configured, in which order middleware runs and which endpoints exist on which listener.
/// Split into partial files by concern:
/// <list type="bullet">
/// <item><c>.Options</c>: bind and validate every options class (eagerly too, where composition needs values).</item>
/// <item><c>.Logging</c>: Serilog.</item>
/// <item><c>.Telemetry</c>: OpenTelemetry tracing and metrics.</item>
/// <item><c>.Observability</c>: correlation, metrics, redaction, audit, usage, synthetic checks.</item>
/// <item><c>.Errors</c>: problem details and error mapping.</item>
/// <item><c>.Proxy</c>: YARP and its extension points (routing conventions, rewriting, limits, per-route policies), Kestrel.</item>
/// <item><c>.RequestHandling</c>: CORS, compression, contract validation, request aggregation.</item>
/// <item><c>.Pipeline</c>: the middleware order.</item>
/// <item><c>.Endpoints</c>: public and internal endpoints.</item>
/// </list>
/// </summary>
public sealed partial class GatewayCompositionRoot
{
    private readonly WebApplicationBuilder _builder;

    public GatewayCompositionRoot(WebApplicationBuilder builder)
    {
        _builder = builder;

        // Values the composition itself depends on (ports, exporters, sampler, sinks). Bound and validated now,
        // with the same validators the options pipeline uses later, so bad configuration fails before anything starts.
        Listeners = BindEagerly(ListenerOptions.SectionName, new ListenerOptions(), new ListenerOptionsValidator(), new ListenerOptionsConsistencyValidator());
        Errors = BindEagerly(ErrorsOptions.SectionName, new ErrorsOptions(), new ErrorsOptionsValidator(), new ErrorsOptionsConsistencyValidator());
        Observability = BindEagerly(ObservabilityOptions.SectionName, new ObservabilityOptions(), new ObservabilityOptionsValidator(), new ObservabilityOptionsConsistencyValidator());
        Logging = BindEagerly(LoggingOptions.SectionName, new LoggingOptions(), new LoggingOptionsValidator());
        Routing = BindEagerly(RoutingOptions.SectionName, new RoutingOptions(), new RoutingOptionsValidator(), new RoutingOptionsConsistencyValidator(AllowWildcardHosts));
        Limits = BindEagerly(RequestLimitsOptions.SectionName, new RequestLimitsOptions(), new RequestLimitsOptionsValidator());
        Compression = BindEagerly(CompressionOptions.SectionName, new CompressionOptions());
        Aggregation = BindEagerly(AggregationOptions.SectionName, new AggregationOptions(), new AggregationOptionsConsistencyValidator(Routing.PublicPathPrefix, AllowWildcardHosts));
    }

    private IServiceCollection Services => _builder.Services;

    private ConfigurationManager Configuration => _builder.Configuration;

    private IWebHostEnvironment Environment => _builder.Environment;

    private ListenerOptions Listeners { get; }

    private ErrorsOptions Errors { get; }

    private ObservabilityOptions Observability { get; }

    private LoggingOptions Logging { get; }

    private RoutingOptions Routing { get; }

    private RequestLimitsOptions Limits { get; }

    private CompressionOptions Compression { get; }

    private AggregationOptions Aggregation { get; }

    /// <summary>Routes without hosts, wildcard hosts and <c>*</c> in <c>Routing:AllowedHosts</c>: Development only.</summary>
    private bool AllowWildcardHosts => Environment.IsDevelopment();

    /// <summary>Registers every service. Order inside follows dependencies: options, logging, libraries, hosting.</summary>
    public void ComposeServices()
    {
        ComposeOptions();
        ComposeKestrel();
        ComposeLogging();
        ComposeObservability();
        ComposeTelemetry();
        ComposeErrors();
        ComposeProxy();
        ComposeRequestHandling();
    }
}
