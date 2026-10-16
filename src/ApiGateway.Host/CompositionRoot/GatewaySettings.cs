using ApiGateway.Errors;
using ApiGateway.Observability;
using ApiGateway.Observability.Serilog;
using ApiGateway.Proxy.Aggregation;
using ApiGateway.Proxy.Compression;
using ApiGateway.Proxy.Cors;
using ApiGateway.Proxy.Headers;
using ApiGateway.Proxy.Limits;
using ApiGateway.Proxy.Listeners;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Validation;
using Microsoft.Extensions.Options;

namespace ApiGateway.Host.CompositionRoot;

/// <summary>
/// Every configuration section of the gateway, in one place. Each section is bound and validated once, before
/// anything is composed, so bad configuration fails before the host starts; features read these startup values
/// when a registration depends on them. Each section is also registered as reloading options with the same
/// validators, re-run on every reload, for the components that inject <see cref="IOptions{TOptions}"/> or
/// <see cref="IOptionsMonitor{TOptions}"/>.
/// </summary>
internal sealed class GatewaySettings
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    public GatewaySettings(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        _services = services;
        _configuration = configuration;
        AllowWildcardHosts = environment.IsDevelopment();

        Listeners = Add(ListenerOptions.SectionName, new ListenerOptionsValidator(), new ListenerOptionsConsistencyValidator());
        Errors = Add(ErrorsOptions.SectionName, new ErrorsOptionsValidator(), new ErrorsOptionsConsistencyValidator());
        Observability = Add(ObservabilityOptions.SectionName, new ObservabilityOptionsValidator(), new ObservabilityOptionsConsistencyValidator());
        Logging = Add(LoggingOptions.SectionName, new LoggingOptionsValidator());
        Routing = Add(RoutingOptions.SectionName, new RoutingOptionsValidator(), new RoutingOptionsConsistencyValidator(AllowWildcardHosts));
        Headers = Add<HeaderOptions>(HeaderOptions.SectionName);
        Limits = Add(RequestLimitsOptions.SectionName, new RequestLimitsOptionsValidator());
        Cors = Add(GatewayCorsOptions.SectionName, new GatewayCorsOptionsConsistencyValidator());
        Compression = Add<CompressionOptions>(CompressionOptions.SectionName);
        ContractValidation = Add(ContractValidationOptions.SectionName, new ContractValidationOptionsValidator());
        Aggregation = Add(AggregationOptions.SectionName, new AggregationOptionsConsistencyValidator(Routing.PublicPathPrefix, AllowWildcardHosts));
    }

    /// <summary>Routes without hosts, wildcard hosts and <c>*</c> in <c>Routing:AllowedHosts</c>: Development only.</summary>
    public bool AllowWildcardHosts { get; }

    public ListenerOptions Listeners { get; }

    public ErrorsOptions Errors { get; }

    public ObservabilityOptions Observability { get; }

    public LoggingOptions Logging { get; }

    public RoutingOptions Routing { get; }

    public HeaderOptions Headers { get; }

    public RequestLimitsOptions Limits { get; }

    public GatewayCorsOptions Cors { get; }

    public CompressionOptions Compression { get; }

    public ContractValidationOptions ContractValidation { get; }

    public AggregationOptions Aggregation { get; }

    /// <summary>
    /// Binds <paramref name="section"/> into a startup snapshot (throwing on validation failures) and registers
    /// <typeparamref name="TOptions"/> as reloading options validated by <paramref name="validators"/>.
    /// </summary>
    private TOptions Add<TOptions>(string section, params IValidateOptions<TOptions>[] validators)
        where TOptions : class, new()
    {
        var configurationSection = _configuration.GetSection(section);

        var snapshot = new TOptions();
        ConfigurationBinding.Bind(configurationSection, snapshot);
        var failures = validators
            .Select(validator => validator.Validate(Options.DefaultName, snapshot))
            .Where(static result => result.Failed)
            .SelectMany(static result => result.Failures ?? [])
            .ToList();

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(TOptions), failures);
        }

        _services.AddOptions<TOptions>()
            .Configure(options => ConfigurationBinding.Bind(configurationSection, options))
            .ValidateOnStart();
        _services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(new ConfigurationChangeTokenSource<TOptions>(configurationSection));
        foreach (var validator in validators)
        {
            _services.AddSingleton(validator);
        }

        return snapshot;
    }
}
