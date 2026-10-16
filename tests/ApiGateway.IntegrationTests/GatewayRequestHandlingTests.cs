using System.Net;
using System.Text;
using System.Text.Json;
using ApiGateway.IntegrationTests.Infrastructure;
using ApiGateway.Proxy.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApiGateway.IntegrationTests;

public sealed class GatewayRequestHandlingTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cors_preflight_is_answered_at_the_gateway()
    {
        using var client = factory.CreatePublicClient();
        var before = factory.Upstream.Received.Count;
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/orders/1");
        request.Headers.Add("Origin", "https://app.example.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe("https://app.example.com");
        response.Headers.GetValues("Access-Control-Allow-Credentials").Single().ShouldBe("true");
        response.Headers.GetValues("Access-Control-Max-Age").Single().ShouldBe("600");
        factory.Upstream.Received.Count.ShouldBe(before);
    }

    [Theory]
    [InlineData("https://shop.example.com", true)]
    [InlineData("https://evil.test", false)]
    public async Task Cors_allows_configured_origins_and_wildcard_subdomains_only(string origin, bool allowed)
    {
        factory.Upstream.Handler = static (_, _) =>
        {
            var upstream = new HttpResponseMessage(HttpStatusCode.OK);
            upstream.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", "*");
            return Task.FromResult(upstream);
        };
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/1");
        request.Headers.Add("Origin", origin);

        using var response = await client.SendAsync(request, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        if (allowed)
        {
            response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(origin);
            response.Headers.GetValues("Access-Control-Expose-Headers").Single().ShouldContain("X-Correlation-Id");
        }
        else
        {
            response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData("/api/orders/1", "br")]
    [InlineData("/api/no-compression/1", null)]
    public async Task Responses_are_compressed_unless_the_route_opts_out(string path, string? expected)
    {
        factory.Upstream.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"items\":\"" + new string('a', 4096) + "\"}", Encoding.UTF8, "application/json"),
        });
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");

        using var response = await client.SendAsync(request, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        if (expected is null)
        {
            response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        }
        else
        {
            response.Content.Headers.ContentEncoding.ShouldContain(expected);
        }
    }

    [Fact]
    public async Task Request_bodies_are_validated_against_the_published_contract()
    {
        factory.Contracts.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"type":"object","required":["qty"],"properties":{"qty":{"type":"integer","minimum":1}}}""",
                Encoding.UTF8,
                "application/json"),
        });
        string? forwardedBody = null;
        factory.Upstream.Handler = async (request, token) =>
        {
            forwardedBody = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        using var client = factory.CreatePublicClient();
        await WaitForContractAsync();

        using var invalid = await client.PostAsync("/api/validated/orders", Json("""{"qty":0}"""), Cancel);
        using var valid = await client.PostAsync("/api/validated/orders", Json("""{"qty":3}"""), Cancel);

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync(Cancel));
        problem.RootElement.GetProperty("code").GetString().ShouldBe("gateway.request_validation_failed");
        problem.RootElement.GetProperty("errors")[0].GetString()!.ShouldContain("qty");

        valid.StatusCode.ShouldBe(HttpStatusCode.Created);
        forwardedBody.ShouldBe("""{"qty":3}""");
    }

    [Fact]
    public async Task Composite_endpoint_returns_every_part()
    {
        factory.Upstream.Handler = static (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"path\":\"{request.RequestUri!.AbsolutePath}\"}}", Encoding.UTF8, "application/json"),
        });
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/bff/summary/42", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        body.RootElement.GetProperty("data").GetProperty("order").GetProperty("path").GetString().ShouldBe("/orders/42");
        body.RootElement.GetProperty("data").GetProperty("item").GetProperty("path").GetString().ShouldBe("/items/42");
        body.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Composite_endpoint_returns_a_partial_result_with_problem_shaped_errors()
    {
        SlowItems();
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/bff/summary/42", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        body.RootElement.GetProperty("data").GetProperty("order").GetProperty("id").GetInt32().ShouldBe(42);
        body.RootElement.GetProperty("data").GetProperty("item").ValueKind.ShouldBe(JsonValueKind.Null);
        var error = body.RootElement.GetProperty("errors").GetProperty("item");
        error.GetProperty("code").GetString().ShouldBe("gateway.upstream_timeout");
        error.GetProperty("status").GetInt32().ShouldBe(504);
        error.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Composite_endpoint_in_fail_whole_mode_answers_with_the_failed_calls_problem()
    {
        SlowItems();
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/bff/summary-strict/42", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        problem.RootElement.GetProperty("code").GetString().ShouldBe("gateway.upstream_timeout");
        problem.RootElement.GetProperty("failedCalls")[0].GetString().ShouldBe("item");
    }

    private void SlowItems() =>
        factory.Upstream.Handler = static async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/items", StringComparison.Ordinal))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":42}", Encoding.UTF8, "application/json") };
        };

    private async Task WaitForContractAsync()
    {
        var refresh = factory.Services.GetServices<IHostedService>().OfType<ContractRefreshService>().Single();
        await refresh.RefreshAllAsync(Cancel);

        factory.Services.GetRequiredService<ContractStore>().Find(new Uri(TestRoutes.ContractUrl), ContractKind.JsonSchema).ShouldNotBeNull();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
