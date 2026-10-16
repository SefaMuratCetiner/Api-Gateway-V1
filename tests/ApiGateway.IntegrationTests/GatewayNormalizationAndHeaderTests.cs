using System.Net;
using System.Text.Json;
using ApiGateway.IntegrationTests.Infrastructure;

namespace ApiGateway.IntegrationTests;

public sealed class GatewayNormalizationAndHeaderTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Duplicate_and_trailing_slashes_are_canonicalized_before_routing()
    {
        Ok();
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api//orders//42/", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Upstream.Received.Last().RequestUri!.AbsolutePath.ShouldBe("/api/orders/42");
    }

    [Theory]
    [InlineData("/api/orders/%252e%252e/admin")]
    [InlineData("/api/orders/a%0d%0aSet-Cookie:x")]
    [InlineData("/api/orders/%c0%ae%c0%ae/admin")]
    public async Task Double_encoding_control_characters_and_overlong_utf8_are_rejected(string path)
    {
        using var client = factory.CreatePublicClient();
        var before = factory.Upstream.Received.Count;

        using var response = await client.GetAsync(path, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("gateway.bad_request");
        factory.Upstream.Received.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Hosts_outside_the_allowed_list_are_rejected()
    {
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/1");
        request.Headers.Host = "evil.test";

        using var response = await client.SendAsync(request, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("gateway.bad_request");
        response.Headers.Contains("X-Trace-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task X_forwarded_host_is_validated_like_host()
    {
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/1");
        request.Headers.Add("X-Forwarded-Host", "localhost, evil.test");

        using var response = await client.SendAsync(request, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Client_forwarding_and_identity_headers_are_replaced_or_removed()
    {
        Ok();
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/1");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "localhost");
        request.Headers.Add("X-Forwarded-Prefix", "/admin");
        request.Headers.Add("Forwarded", "for=203.0.113.9");
        request.Headers.Add("X-Tenant-Id", "other-tenant");
        request.Headers.Add("X-User-Id", "admin");
        request.Headers.Add("X-Internal-Debug", "1");

        using var response = await client.SendAsync(request, Cancel);

        var upstream = factory.Upstream.Received.Last().Headers;
        upstream.Contains("X-Tenant-Id").ShouldBeFalse();
        upstream.Contains("X-User-Id").ShouldBeFalse();
        upstream.Contains("X-Internal-Debug").ShouldBeFalse();
        upstream.Contains("Forwarded").ShouldBeFalse();
        upstream.Contains("X-Forwarded-Prefix").ShouldBeFalse();
        upstream.GetValues("X-Forwarded-For").Single().ShouldBe("127.0.0.1");
        upstream.GetValues("X-Forwarded-Proto").Single().ShouldBe("http");
        upstream.GetValues("X-Forwarded-Host").Single().ShouldBe("localhost");
    }

    [Fact]
    public async Task Idempotency_key_reaches_the_backend_intact()
    {
        Ok();
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = new StringContent("{}") };
        request.Headers.Add("Idempotency-Key", "8e03978e-40d5-43e8-bc93-6894a57f9324");

        using var response = await client.SendAsync(request, Cancel);

        factory.Upstream.Received.Last().Headers.GetValues("Idempotency-Key").Single().ShouldBe("8e03978e-40d5-43e8-bc93-6894a57f9324");
    }

    [Fact]
    public async Task Stack_revealing_headers_are_removed_and_backend_rate_limit_headers_kept()
    {
        factory.Upstream.Handler = static (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Server", "Apache/2.4.1");
            response.Headers.TryAddWithoutValidation("X-Powered-By", "PHP/8.1");
            response.Headers.TryAddWithoutValidation("X-AspNet-Version", "4.0.30319");
            response.Headers.TryAddWithoutValidation("RateLimit-Limit", "100");
            response.Headers.TryAddWithoutValidation("RateLimit-Remaining", "99");
            return Task.FromResult(response);
        };
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/orders/1", Cancel);

        response.Headers.Contains("Server").ShouldBeFalse();
        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
        response.Headers.Contains("X-AspNet-Version").ShouldBeFalse();
        response.Headers.GetValues("RateLimit-Limit").Single().ShouldBe("100");
        response.Headers.GetValues("RateLimit-Remaining").Single().ShouldBe("99");
    }

    [Fact]
    public async Task Bodies_over_the_route_limit_are_rejected_before_the_backend_is_called()
    {
        Ok();
        using var client = factory.CreatePublicClient();
        var before = factory.Upstream.Received.Count;
        using var content = new StringContent(new string('x', 100));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/small-body/upload") { Content = content };
        request.Headers.ExpectContinue = true;

        using var response = await client.SendAsync(request, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await ProblemCodeAsync(response)).ShouldBe("gateway.payload_too_large");
        factory.Upstream.Received.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Backend_responses_over_the_route_limit_become_502()
    {
        factory.Upstream.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 100)),
        });
        using var client = factory.CreatePublicClient();

        using var response = await client.GetAsync("/api/small-response/x", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await ProblemCodeAsync(response)).ShouldBe("gateway.bad_upstream_response");
    }

    [Fact]
    public async Task Range_requests_and_206_responses_pass_through()
    {
        factory.Upstream.Handler = static (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[10]) };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 9, 1000);
            return Task.FromResult(response);
        };
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/files/1");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);

        using var response = await client.SendAsync(request, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentRange!.Length.ShouldBe(1000);
        factory.Upstream.Received.Last().Headers.Range!.ToString().ShouldBe("bytes=0-9");
    }

    private void Ok() => factory.Upstream.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        return problem.RootElement.GetProperty("code").GetString();
    }
}
