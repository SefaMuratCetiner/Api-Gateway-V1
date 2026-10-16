using System.Net;
using System.Text.Json;
using ApiGateway.IntegrationTests.Infrastructure;

namespace ApiGateway.IntegrationTests;

public sealed class GatewayRoutingTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Routes_match_on_path_header_and_query()
    {
        Ok();
        using var client = factory.CreatePublicClient();

        await HitAsync(client, "/api/match/x");
        Forwarded().Host.ShouldBe("localhost");

        await HitAsync(client, "/api/match/x", request => request.Headers.Add("X-Api-Version", "2"));
        Forwarded().Host.ShouldBe("v2.test");

        await HitAsync(client, "/api/match/x?variant=beta");
        Forwarded().Host.ShouldBe("v2.test");
    }

    [Fact]
    public async Task A_split_out_service_needs_only_a_route_and_a_cluster()
    {
        Ok();
        using var client = factory.CreatePublicClient();

        await HitAsync(client, "/api/orders/invoices/7");
        Forwarded().ShouldBe(new Uri("http://invoices.test/api/orders/invoices/7"));

        await HitAsync(client, "/api/orders/7");
        Forwarded().ShouldBe(new Uri("http://localhost:5101/api/orders/7"));
    }

    [Fact]
    public async Task Paths_are_forwarded_unchanged_by_default()
    {
        Ok();
        using var client = factory.CreatePublicClient();

        await HitAsync(client, "/api/orders/Order-42?expand=lines");

        Forwarded().PathAndQuery.ShouldBe("/api/orders/Order-42?expand=lines");
    }

    [Fact]
    public async Task Prefix_stripping_forwards_the_rest_and_announces_the_prefix()
    {
        Ok();
        using var client = factory.CreatePublicClient();

        await HitAsync(client, "/api/legacy/items/1");

        var upstream = factory.Upstream.Received.Last();
        upstream.RequestUri.ShouldBe(new Uri("http://legacy.test/svc/items/1"));
        upstream.Headers.GetValues("X-Forwarded-Prefix").Single().ShouldBe("/api/legacy");
    }

    [Fact]
    public async Task Location_and_cookies_from_a_rewritten_route_point_at_the_public_url()
    {
        factory.Upstream.Handler = static (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri("http://legacy.test/svc/items/3");
            response.Headers.TryAddWithoutValidation("Set-Cookie", "sid=abc; Domain=legacy.test; Path=/; HttpOnly");
            return Task.FromResult(response);
        };
        using var client = factory.CreatePublicClient();

        using var response = await SendAsync(client, "/api/legacy/items");

        response.Headers.Location.ShouldBe(new Uri("http://localhost/api/legacy/items/3"));
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.ShouldContain("sid=abc");
        cookie.ShouldContain("path=/api/legacy", Case.Insensitive);
        cookie.ShouldNotContain("domain", Case.Insensitive);
    }

    [Theory]
    [InlineData("https://evil.example.org/phish", HttpStatusCode.BadGateway)]
    [InlineData("//evil.example.org/phish", HttpStatusCode.BadGateway)]
    [InlineData("/\\evil.example.org", HttpStatusCode.BadGateway)]
    [InlineData("javascript:alert(1)", HttpStatusCode.BadGateway)]
    [InlineData("https://login.example.net/authorize", HttpStatusCode.Found)]
    [InlineData("/api/orders/next", HttpStatusCode.Found)]
    public async Task Backend_redirects_are_checked_against_allowed_hosts(string location, HttpStatusCode expected)
    {
        factory.Upstream.Handler = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.TryAddWithoutValidation("Location", location);
            return Task.FromResult(response);
        };
        using var client = factory.CreatePublicClient();

        using var response = await SendAsync(client, "/api/orders/1");

        response.StatusCode.ShouldBe(expected);
        if (expected == HttpStatusCode.BadGateway)
        {
            response.Headers.Location.ShouldBeNull();
            (await response.Content.ReadAsStringAsync(Cancel)).ShouldContain("gateway.bad_upstream_response");
        }
    }

    [Fact]
    public async Task A_deprecated_version_announces_deprecation_sunset_and_migration_notes()
    {
        Ok();
        using var client = factory.CreatePublicClient();

        using var response = await SendAsync(client, "/api/v1/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Deprecation").Single().ShouldBe("@1735689600");
        response.Headers.GetValues("Sunset").Single().ShouldBe("Thu, 01 Jan 2099 00:00:00 GMT");
        response.Headers.GetValues("Link").Single().ShouldBe("<https://docs.example.com/migrate-to-v2>; rel=\"deprecation sunset\"");
    }

    [Fact]
    public async Task A_retired_version_is_answered_with_410_and_a_migration_link()
    {
        using var client = factory.CreatePublicClient();
        var before = factory.Upstream.Received.Count;

        using var response = await SendAsync(client, "/api/v0/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        response.Headers.GetValues("Link").Single().ShouldContain("https://docs.example.com/migrate-to-v2");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        problem.RootElement.GetProperty("code").GetString().ShouldBe("gateway.api_version_retired");
        problem.RootElement.GetProperty("migration").GetString().ShouldBe("https://docs.example.com/migrate-to-v2");
        factory.Upstream.Received.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Tenants_with_dedicated_destinations_are_routed_to_them_and_others_to_the_shared_pool()
    {
        Ok();
        using var client = factory.CreatePublicClient();

        await HitAsync(client, "/api/t/acme/orders");
        Forwarded().Host.ShouldBe("acme.test");

        await HitAsync(client, "/api/t/globex/orders");
        Forwarded().Host.ShouldBe("shared.test");
    }

    [Fact]
    public async Task Weighted_clusters_split_traffic_by_destination_weight()
    {
        Ok();
        using var client = factory.CreatePublicClient();

        for (var i = 0; i < 10; i++)
        {
            await HitAsync(client, "/api/canary/x");
            Forwarded().Host.ShouldBe("next.test");
        }
    }

    [Fact]
    public async Task Proxy_routes_are_not_reachable_on_the_internal_listener()
    {
        Ok();
        using var client = factory.CreateInternalClient();
        var before = factory.Upstream.Received.Count;

        using var response = await client.GetAsync("/api/orders/1", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Upstream.Received.Count.ShouldBe(before);
    }

    private void Ok() => factory.Upstream.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    private Uri Forwarded() => factory.Upstream.Received.Last().RequestUri!;

    private static async Task HitAsync(HttpClient client, string path, Action<HttpRequestMessage>? configure = null)
    {
        using var response = await SendAsync(client, path, configure);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        configure?.Invoke(request);
        return await client.SendAsync(request, Cancel);
    }
}
