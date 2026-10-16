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

public sealed partial class GatewayCompositionRoot
{
    private void ComposeOptions()
    {
        AddOptions<ListenerOptions>(ListenerOptions.SectionName, new ListenerOptionsValidator(), new ListenerOptionsConsistencyValidator());
        AddOptions<ErrorsOptions>(ErrorsOptions.SectionName, new ErrorsOptionsValidator(), new ErrorsOptionsConsistencyValidator());
        AddOptions<ObservabilityOptions>(ObservabilityOptions.SectionName, new ObservabilityOptionsValidator(), new ObservabilityOptionsConsistencyValidator());
        AddOptions<LoggingOptions>(LoggingOptions.SectionName, new LoggingOptionsValidator());
        AddOptions<RoutingOptions>(RoutingOptions.SectionName, new RoutingOptionsValidator(), new RoutingOptionsConsistencyValidator(AllowWildcardHosts));
        AddOptions<HeaderOptions>(HeaderOptions.SectionName);
        AddOptions<RequestLimitsOptions>(RequestLimitsOptions.SectionName, new RequestLimitsOptionsValidator());
        AddOptions<GatewayCorsOptions>(GatewayCorsOptions.SectionName, new GatewayCorsOptionsConsistencyValidator());
        AddOptions<CompressionOptions>(CompressionOptions.SectionName);
        AddOptions<ContractValidationOptions>(ContractValidationOptions.SectionName, new ContractValidationOptionsValidator());
        AddOptions<AggregationOptions>(AggregationOptions.SectionName, new AggregationOptionsConsistencyValidator(Routing.PublicPathPrefix, AllowWildcardHosts));
    }

    /// <summary>
    /// Registers <typeparamref name="TOptions"/> bound to <paramref name="section"/> (reloading with it), validated by
    /// <paramref name="validators"/> on first use and at startup.
    /// </summary>
    private void AddOptions<TOptions>(string section, params IValidateOptions<TOptions>[] validators)
        where TOptions : class, new()
    {
        var configurationSection = Configuration.GetSection(section);
        Services.AddOptions<TOptions>()
            .Configure(options => ConfigurationBinding.Bind(configurationSection, options))
            .ValidateOnStart();
        Services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(new ConfigurationChangeTokenSource<TOptions>(configurationSection));

        foreach (var validator in validators)
        {
            Services.AddSingleton(validator);
        }
    }

    /// <summary>Binds and validates options needed while composing (before the container exists).</summary>
    private TOptions BindEagerly<TOptions>(string section, TOptions options, params IValidateOptions<TOptions>[] validators)
        where TOptions : class
    {
        ConfigurationBinding.Bind(Configuration.GetSection(section), options);

        var failures = validators
            .Select(validator => validator.Validate(Options.DefaultName, options))
            .Where(static result => result.Failed)
            .SelectMany(static result => result.Failures ?? [])
            .ToList();

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(TOptions), failures);
        }

        return options;
    }
}
