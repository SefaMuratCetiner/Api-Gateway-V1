using ApiGateway.Abstractions.Features;
using ApiGateway.Proxy.ApiLifecycle;
using ApiGateway.Proxy.LoadBalancing;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Tenancy;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.UnitTests.Proxy;

public sealed class RoutingTests
{
    [Fact]
    public void Routes_need_the_public_prefix_explicit_methods_and_explicit_hosts()
    {
        var problems = ProxyConventionsFilter.Validate("/health/{**rest}", methods: null, hosts: null, "/api", allowWildcardHosts: false);

        problems.ShouldContain(problem => problem.Contains("public prefix"));
        problems.ShouldContain(problem => problem.Contains("Match.Methods"));
        problems.ShouldContain(problem => problem.Contains("Match.Hosts"));
    }

    [Fact]
    public void Wildcard_and_missing_hosts_are_accepted_in_development_only()
    {
        ProxyConventionsFilter.Validate("/api/x", ["GET"], ["*"], "/api", allowWildcardHosts: false).ShouldNotBeEmpty();
        ProxyConventionsFilter.Validate("/api/x", ["GET"], ["*"], "/api", allowWildcardHosts: true).ShouldBeEmpty();
        ProxyConventionsFilter.Validate("/api/x", ["GET"], null, "/api", allowWildcardHosts: true).ShouldBeEmpty();
        ProxyConventionsFilter.Validate("/api/x", ["GET"], ["api.example.com"], "/api", allowWildcardHosts: false).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/api", true)]
    [InlineData("/api/orders", true)]
    [InlineData("/apix/orders", false)]
    [InlineData("/internal/errors", false)]
    public void Public_prefix_is_matched_on_segment_boundaries(string path, bool expected) =>
        ProxyConventionsFilter.IsUnderPrefix(path, "/api").ShouldBe(expected);

    [Fact]
    public void Wildcard_allowed_hosts_are_rejected_outside_development()
    {
        var options = new RoutingOptions { AllowedHosts = ["*"] };

        new RoutingOptionsConsistencyValidator(allowWildcardHosts: false).Validate(null, options).Failed.ShouldBeTrue();
        new RoutingOptionsConsistencyValidator(allowWildcardHosts: true).Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Weighted_policy_picks_by_cumulative_weight()
    {
        var stable = Destination("stable", (WeightedLoadBalancingPolicy.WeightKey, "90"));
        var canary = Destination("canary", (WeightedLoadBalancingPolicy.WeightKey, "10"));
        IReadOnlyList<DestinationState> destinations = [stable, canary];

        WeightedLoadBalancingPolicy.Pick(destinations, 0.0).ShouldBe(stable);
        WeightedLoadBalancingPolicy.Pick(destinations, 0.89).ShouldBe(stable);
        WeightedLoadBalancingPolicy.Pick(destinations, 0.91).ShouldBe(canary);
        WeightedLoadBalancingPolicy.Pick([Destination("drained", (WeightedLoadBalancingPolicy.WeightKey, "0"))], 0.5).ShouldBeNull();
        WeightedLoadBalancingPolicy.Validate(new Dictionary<string, string>(StringComparer.Ordinal) { [WeightedLoadBalancingPolicy.WeightKey] = "-1" }).ShouldNotBeEmpty();
    }

    [Fact]
    public void Tenants_use_their_dedicated_destinations_or_the_shared_pool()
    {
        var shared = Destination("shared");
        var acme = Destination("acme", (TenantDestinations.TenantsKey, "acme, acme-eu"));
        IReadOnlyList<DestinationState> all = [shared, acme];

        TenantDestinations.Select(all, all, "acme").ShouldBe([acme]);
        TenantDestinations.Select(all, all, "ACME-EU").ShouldBe([acme]);
        TenantDestinations.Select(all, all, "globex").ShouldBe([shared]);
        TenantDestinations.Select(all, all, tenant: null).ShouldBe([shared]);

        // A tenant whose dedicated destinations are all down is not spilled onto the shared pool.
        TenantDestinations.Select(all, [shared], "acme").ShouldBeEmpty();
    }

    [Fact]
    public void Renamed_destination_keys_are_rejected_with_their_new_name()
    {
        var old = new Dictionary<string, string>(StringComparer.Ordinal) { ["Weight"] = "5", ["Tenant"] = "acme" };

        WeightedLoadBalancingPolicy.Validate(old).ShouldContain("'Weight' was renamed to 'LoadBalancing.Weight'");
        TenantDestinations.Validate(old).ShouldContain("'Tenant' was renamed to 'Tenancy.Tenants'");
    }

    [Fact]
    public void Tenant_from_identity_wins_over_the_route_value()
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["tenant"] = "from-path";
        var policy = new TenantRoutingPolicy("tenant");

        TenantRoutingMiddleware.ResolveTenant(context, policy).ShouldBe("from-path");

        context.GetGatewayRequest().TenantId = "from-token";
        TenantRoutingMiddleware.ResolveTenant(context, policy).ShouldBe("from-token");
    }

    [Fact]
    public void Lifecycle_metadata_is_parsed_and_validated()
    {
        var parser = new ApiLifecycleMetadataParser();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ApiLifecycleMetadataParser.VersionKey] = "1",
            [ApiLifecycleMetadataParser.DeprecationKey] = "2025-01-01",
            [ApiLifecycleMetadataParser.SunsetKey] = "2026-06-30T00:00:00Z",
            [ApiLifecycleMetadataParser.LinkKey] = "https://docs.example.com/v2",
        };

        parser.Validate(metadata).ShouldBeEmpty();
        var policy = parser.Parse(metadata);
        policy.IsRetired(new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero)).ShouldBeFalse();
        policy.IsRetired(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)).ShouldBeTrue();

        metadata.Remove(ApiLifecycleMetadataParser.LinkKey);
        metadata[ApiLifecycleMetadataParser.DeprecationKey] = "2027-01-01";
        metadata["ApiLifecycle.Sunst"] = "typo";
        var problems = parser.Validate(metadata);
        problems.ShouldContain(problem => problem.Contains("required"));
        problems.ShouldContain(problem => problem.Contains("must not be later"));
        problems.ShouldContain(problem => problem.Contains("unknown key"));
    }

    [Fact]
    public void Lifecycle_headers_follow_rfc_9745_and_8594()
    {
        var headers = new HeaderDictionary();
        var policy = new ApiLifecyclePolicy("1", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new Uri("https://docs.example.com/v2"), Retired: false);

        ApiLifecycleMiddleware.Apply(headers, policy);

        headers["Deprecation"].ToString().ShouldBe("@1735689600");
        headers["Sunset"].ToString().ShouldBe("Thu, 01 Jan 2026 00:00:00 GMT");
        headers["Link"].ToString().ShouldBe("<https://docs.example.com/v2>; rel=\"deprecation sunset\"");
    }

    private static DestinationState Destination(string id, params (string Key, string Value)[] metadata) =>
        new(id, new DestinationModel(new DestinationConfig
        {
            Address = $"http://{id}.test/",
            Metadata = metadata.ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal),
        }));
}
