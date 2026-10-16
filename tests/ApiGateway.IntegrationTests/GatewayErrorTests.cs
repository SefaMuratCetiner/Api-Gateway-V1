using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ApiGateway.IntegrationTests.Infrastructure;

namespace ApiGateway.IntegrationTests;

public sealed class GatewayErrorTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unknown_path_is_a_404_problem_with_trace_and_correlation_headers()
    {
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/nope", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var problem = await ReadProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("gateway.route_not_found");
        response.Headers.Contains("X-Trace-Id").ShouldBeTrue();
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
        response.Headers.Contains("traceresponse").ShouldBeTrue();
    }

    [Fact]
    public async Task Wrong_method_is_a_405_problem_that_keeps_allow()
    {
        using var client = factory.CreatePublicClient();

        using var response = await client.PostAsync("/api/catalog/items", new StringContent("{}"), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldContain("GET");
        using var problem = await ReadProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("gateway.method_not_allowed");
    }

    [Fact]
    public async Task Backend_errors_pass_through_unchanged()
    {
        factory.Upstream.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/orders/42", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("application/problem+json");
    }

    [Fact]
    public async Task Unreachable_backend_is_a_502_without_internal_details()
    {
        factory.Upstream.Handler = static (_, _) => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "Connection refused (orders-db01.internal:5101)", new SocketException((int)SocketError.ConnectionRefused));
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/orders/42", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync(Cancel);
        body.ShouldContain("gateway.upstream_unreachable");
        body.ShouldNotContain("orders-db01");
        body.ShouldNotContain("5101");
    }

    [Fact]
    public async Task Cluster_without_destinations_is_a_503_with_retry_after()
    {
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/empty/x", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull();
        using var problem = await ReadProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("gateway.no_healthy_destination");
    }

    [Fact]
    public async Task Slow_backend_hits_the_route_timeout_with_a_504()
    {
        factory.Upstream.Handler = static async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/catalog/items", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        var body = await response.Content.ReadAsStringAsync(Cancel);
        body.ShouldContain("\"code\":\"gateway.");
    }

    [Fact]
    public async Task Error_messages_follow_accept_language()
    {
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nope");
        request.Headers.AcceptLanguage.ParseAdd("tr-TR");

        using var response = await client.SendAsync(request, Cancel);

        response.Content.Headers.ContentLanguage.ShouldContain("tr");
        using var problem = await ReadProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("gateway.route_not_found");
        problem.RootElement.GetProperty("title").GetString().ShouldBe("İstekle eşleşen bir rota yok.");
    }

    [Fact]
    public async Task Error_catalog_is_served_on_the_internal_listener_only()
    {
        using var internalClient = factory.CreateInternalClient();
        using var publicClient = factory.CreatePublicClient();

        var catalog = await internalClient.GetFromJsonAsync<JsonElement>("/internal/errors", Cancel);
        using var fromPublic = await publicClient.GetAsync("/internal/errors", Cancel);

        catalog.GetArrayLength().ShouldBeGreaterThan(10);
        fromPublic.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<JsonDocument> ReadProblemAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Cancel), cancellationToken: Cancel);
}
