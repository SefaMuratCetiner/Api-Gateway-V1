using ApiGateway.Errors;
using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability;
using ApiGateway.Observability.Metrics;
using Microsoft.Extensions.Configuration;

namespace ApiGateway.IntegrationTests;

public sealed class ConfigurationBindingTests
{
    [Fact]
    public void Configured_arrays_replace_defaults_instead_of_appending()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["SupportedCultures:0"] = "en",
                ["SupportedCultures:1"] = "de",
            })
            .Build();
        var options = new ErrorsOptions();

        ConfigurationBinding.Bind(configuration, options);

        options.SupportedCultures.ShouldBe(["en", "de"]);
    }

    [Fact]
    public void Nested_arrays_are_replaced_and_unmentioned_defaults_kept()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracing:TrustedNetworks:0"] = "10.1.0.0/16",
            })
            .Build();
        var options = new ObservabilityOptions();

        ConfigurationBinding.Bind(configuration, options);

        options.Tracing.TrustedNetworks.ShouldBe(["10.1.0.0/16"]);
        options.Metrics.LatencyBucketsSeconds.Length.ShouldBe(new MetricsOptions().LatencyBucketsSeconds.Length);
    }
}
