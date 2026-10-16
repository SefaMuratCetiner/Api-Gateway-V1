using System.Text.Json;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Features;
using ApiGateway.Errors.Localization;
using ApiGateway.Errors.Problems;
using ApiGateway.Errors.Telemetry;
using ApiGateway.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.UnitTests.Errors;

public sealed class ProblemResponseWriterTests : IDisposable
{
    private readonly TestMeterFactory _meters = new();
    private readonly ProblemResponseWriter _writer;

    public ProblemResponseWriterTests()
    {
        var options = TestOptions.Errors();
        _writer = new ProblemResponseWriter(new ResxErrorMessageLocalizer(), new AcceptLanguageResolver(options), new ErrorMetrics(_meters), options);
    }

    public void Dispose() => _meters.Dispose();

    [Fact]
    public async Task Writes_rfc9457_problem_with_code_and_trace_id_in_fixed_order()
    {
        var context = NewContext("/api/orders/42");

        var written = await _writer.WriteAsync(context, GatewayErrors.RouteNotFound);

        written.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(404);
        context.Response.ContentType.ShouldBe("application/problem+json");
        context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");

        using var json = await ReadBodyAsync(context);
        var names = json.RootElement.EnumerateObject().Select(static p => p.Name).ToArray();
        names.ShouldBe(["type", "title", "status", "detail", "instance", "code", "traceId"]);
        json.RootElement.GetProperty("code").GetString().ShouldBe("gateway.route_not_found");
        json.RootElement.GetProperty("type").GetString().ShouldBe("urn:apigateway:error:gateway.route_not_found");
        json.RootElement.GetProperty("instance").GetString().ShouldBe("/api/orders/42");
        json.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Sets_retry_after_in_whole_seconds_rounded_up()
    {
        var context = NewContext("/");

        await _writer.WriteAsync(context, GatewayErrors.NoHealthyDestination, new ProblemWriteArgs { RetryAfter = TimeSpan.FromMilliseconds(4200) });

        context.Response.StatusCode.ShouldBe(503);
        context.Response.Headers.RetryAfter.ToString().ShouldBe("5");
    }

    [Fact]
    public async Task Localizes_title_from_accept_language_and_sets_content_language()
    {
        var context = NewContext("/");
        context.Request.Headers.AcceptLanguage = "tr-TR,tr;q=0.9,en;q=0.5";

        await _writer.WriteAsync(context, GatewayErrors.RouteNotFound);

        context.Response.Headers.ContentLanguage.ToString().ShouldBe("tr");
        context.Response.Headers.Vary.ToString().ShouldContain("Accept-Language");
        using var json = await ReadBodyAsync(context);
        json.RootElement.GetProperty("title").GetString().ShouldBe("İstekle eşleşen bir rota yok.");
        json.RootElement.GetProperty("code").GetString().ShouldBe("gateway.route_not_found");
    }

    [Fact]
    public async Task Marks_the_request_with_the_gateway_error()
    {
        var context = NewContext("/");

        await _writer.WriteAsync(context, GatewayErrors.UpstreamTimeout);

        context.GetGatewayError().ShouldBe(GatewayErrors.UpstreamTimeout);
    }

    [Fact]
    public async Task Does_not_write_after_the_response_started()
    {
        var context = NewContext("/");
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());

        var written = await _writer.WriteAsync(context, GatewayErrors.InternalError);

        written.ShouldBeFalse();
    }

    [Fact]
    public void Extensions_cannot_overwrite_standard_members()
    {
        var document = new ProblemDocument("t", "title", 400, null, null, "gateway.bad_request", "abc",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "evil", ["retryAfter"] = TimeSpan.FromSeconds(3) });

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            document.WriteTo(writer);
        }

        using var json = JsonDocument.Parse(stream.ToArray());
        json.RootElement.GetProperty("code").GetString().ShouldBe("gateway.bad_request");
        json.RootElement.GetProperty("retryAfter").GetInt64().ShouldBe(3);
    }

    [Fact]
    public void Instance_is_truncated()
    {
        ProblemResponseWriter.InstanceOf(new PathString("/" + new string('a', 500)), 32)!.Length.ShouldBe(32);
    }

    private static DefaultHttpContext NewContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
