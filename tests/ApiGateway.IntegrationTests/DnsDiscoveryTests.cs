using System.Net;
using ApiGateway.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy;

namespace ApiGateway.IntegrationTests;

/// <summary>
/// The gateway with DNS discovery on: destination host names become one destination per address. The other test
/// clusters use names that do not resolve (<c>*.test</c>), which must not keep the gateway from starting.
/// </summary>
public sealed class DnsGatewayFactory : GatewayFactory
{
    protected override void ConfigureSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Routing:DnsDiscovery:Enabled", "true");
        builder.UseSetting("ReverseProxy:Clusters:dns:Destinations:svc:Address", "http://localhost:5199/");
    }
}

public sealed class DnsDiscoveryTests(DnsGatewayFactory factory) : IClassFixture<DnsGatewayFactory>
{
    [Fact]
    public async Task Destination_host_names_are_resolved_and_unresolvable_ones_kept()
    {
        using var client = factory.CreatePublicClient();
        var proxy = factory.Services.GetRequiredService<IProxyStateLookup>();

        // Resolution runs when the configuration is applied; give it a moment.
        IReadOnlyList<string> addresses = [];
        for (var attempt = 0; attempt < 50 && addresses.Count == 0; attempt++)
        {
            if (proxy.TryGetCluster("dns", out var cluster))
            {
                addresses = [.. cluster.Destinations.Values.Select(static destination => destination.Model.Config.Address)];
            }

            if (addresses.Count == 0)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }

        addresses.ShouldNotBeEmpty();
        addresses.ShouldAllBe(address => IPAddress.IsLoopback(IPAddress.Parse(new Uri(address).Host.Trim('[', ']'))));

        proxy.TryGetCluster("v2", out var unresolvable).ShouldBeTrue();
        unresolvable.Destinations.Values.Single().Model.Config.Address.ShouldBe("http://v2.test/");
    }
}
