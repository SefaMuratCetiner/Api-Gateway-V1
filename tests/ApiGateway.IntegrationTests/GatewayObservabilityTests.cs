using System.Globalization;
using System.Net;
using ApiGateway.IntegrationTests.Infrastructure;
using Serilog.Events;

namespace ApiGateway.IntegrationTests;

public sealed class GatewayObservabilityTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Well_formed_inbound_correlation_id_is_kept_and_forwarded()
    {
        factory.Upstream.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/1");
        request.Headers.Add("X-Correlation-Id", "client-req-123");

        using var response = await client.SendAsync(request, Cancel);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("client-req-123");
        factory.Upstream.Received.Last().Headers.GetValues("X-Correlation-Id").Single().ShouldBe("client-req-123");
    }

    [Fact]
    public async Task Malformed_inbound_correlation_id_is_replaced()
    {
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nope");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "<script>alert(1)</script>");

        using var response = await client.SendAsync(request, Cancel);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldNotContain("<");
    }

    [Fact]
    public async Task Access_log_has_gateway_fields_and_no_secrets()
    {
        factory.Upstream.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        using var client = factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/7?access_token=leaky-token-value");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer super-secret-token-value");
        request.Headers.Add("X-Correlation-Id", "access-log-test");

        using var response = await client.SendAsync(request, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var accessLog = await WaitForAsync(() => factory.Logs.Events.FirstOrDefault(static e =>
            e.Properties.TryGetValue("CorrelationId", out var id) && id.ToString(null, CultureInfo.InvariantCulture).Contains("access-log-test", StringComparison.Ordinal)
            && e.Properties.ContainsKey("RouteId")));

        accessLog.ShouldNotBeNull();
        accessLog.Properties["RouteId"].ToString(null, CultureInfo.InvariantCulture).ShouldContain("orders");
        accessLog.Properties.ShouldContainKey("ClusterId");
        accessLog.Properties.ShouldContainKey("Elapsed");

        var everything = string.Join('\n', factory.Logs.Events.Select(static e => e.RenderMessage(CultureInfo.InvariantCulture) + " " + string.Join(' ', e.Properties.Values)));
        everything.ShouldNotContain("super-secret-token-value");
        everything.ShouldNotContain("leaky-token-value");
    }

    [Fact]
    public async Task Metrics_are_scraped_on_the_internal_listener()
    {
        using var publicClient = factory.CreatePublicClient();
        using var internalClient = factory.CreateInternalClient();
        using var warmUp = await publicClient.GetAsync("/nope", Cancel);

        using var response = await internalClient.GetAsync("/metrics", Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Cancel);
        body.ShouldContain("gateway_errors");
    }

    private static async Task<LogEvent?> WaitForAsync(Func<LogEvent?> probe)
    {
        // Sinks are asynchronous; give the pipeline a moment.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (probe() is { } found)
            {
                return found;
            }

            await Task.Delay(50, Cancel);
        }

        return null;
    }
}
