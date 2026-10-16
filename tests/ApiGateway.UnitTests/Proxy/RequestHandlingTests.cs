using System.Text;
using System.Text.Json;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors.Localization;
using ApiGateway.Errors.Problems;
using ApiGateway.Errors.Telemetry;
using ApiGateway.Proxy.Aggregation;
using ApiGateway.Proxy.Compression;
using ApiGateway.Proxy.Cors;
using ApiGateway.Proxy.Limits;
using ApiGateway.Proxy.Validation;
using ApiGateway.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NJsonSchema;
using NSwag;

namespace ApiGateway.UnitTests.Proxy;

public sealed class RequestHandlingTests
{
    [Fact]
    public void Cors_policies_reject_wildcard_with_credentials_and_malformed_origins()
    {
        var options = new GatewayCorsOptions
        {
            Policies =
            {
                ["ok"] = new CorsPolicyOptions { Origins = ["https://app.example.com", "https://*.example.com"], Methods = ["GET"], AllowCredentials = true },
                ["open"] = new CorsPolicyOptions { Origins = ["*"], Methods = ["GET"] },
                ["bad"] = new CorsPolicyOptions { Origins = ["*", "https://app.example.com/path", "ftp://x.example.com"], Methods = [], AllowCredentials = true },
            },
        };

        var result = new GatewayCorsOptionsConsistencyValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        var failures = result.Failures!.ToList();
        failures.ShouldAllBe(failure => failure.Contains("Cors:Policies:bad"));
        failures.ShouldContain(failure => failure.Contains("AllowCredentials"));
        failures.ShouldContain(failure => failure.Contains("app.example.com/path"));
        failures.ShouldContain(failure => failure.Contains("ftp://"));
        failures.ShouldContain(failure => failure.Contains("Methods"));
    }

    [Fact]
    public void Compression_can_be_switched_off_per_route()
    {
        var parser = new CompressionMetadataParser();

        parser.Parse(null).Enabled.ShouldBeTrue();
        parser.Parse(new Dictionary<string, string>(StringComparer.Ordinal) { [CompressionMetadataParser.EnabledKey] = "false" }).Enabled.ShouldBeFalse();
        parser.Validate(new Dictionary<string, string>(StringComparer.Ordinal) { [CompressionMetadataParser.EnabledKey] = "nope" }).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Content_length_over_the_route_limit_is_rejected_without_reading_the_body()
    {
        var writer = new RecordingProblemWriter();
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>(new MaxBodySize(10));
        context.Request.ContentLength = 11;
        context.Request.Body = new ThrowingStream();
        var nextCalled = false;

        await new RequestBodyLimitMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, writer).InvokeAsync(context);

        nextCalled.ShouldBeFalse();
        writer.Written.ShouldBe(GatewayErrors.PayloadTooLarge);
    }

    [Fact]
    public async Task Response_size_limit_stream_cuts_off_oversized_bodies()
    {
        var exceeded = false;
        await using var stream = new ResponseSizeLimitStream(new MemoryStream(), 8, () => exceeded = true);

        await stream.WriteAsync(new byte[8], TestContext.Current.CancellationToken);
        await Should.ThrowAsync<IOException>(async () => await stream.WriteAsync(new byte[1], TestContext.Current.CancellationToken));

        exceeded.ShouldBeTrue();
    }

    [Fact]
    public void Response_limit_metadata_overrides_the_global_default()
    {
        var parser = new ResponseLimitsMetadataParser(Options.Create(new RequestLimitsOptions { MaxResponseBodySize = 1000 }));

        parser.Parse(null).MaxResponseBodySize.ShouldBe(1000);
        parser.Parse(new Dictionary<string, string>(StringComparer.Ordinal) { [ResponseLimitsMetadataParser.MaxResponseBodySizeKey] = "-1" }).MaxResponseBodySize.ShouldBeNull();
        parser.Validate(new Dictionary<string, string>(StringComparer.Ordinal) { [ResponseLimitsMetadataParser.MaxResponseBodySizeKey] = "0" }).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Json_schema_violations_are_reported_by_path()
    {
        var schema = await JsonSchema.FromJsonAsync("""{"type":"object","required":["qty"],"properties":{"qty":{"type":"integer","minimum":1}},"additionalProperties":false}""", TestContext.Current.CancellationToken);

        RequestValidationMiddleware.Validate(schema, """{"qty":3}""", 10).ShouldBeEmpty();
        RequestValidationMiddleware.Validate(schema, """{"qty":0,"extra":1}""", 10).ShouldBe(["#/qty: NumberTooSmall", "#/extra: NoAdditionalPropertiesAllowed"], ignoreOrder: true);
        RequestValidationMiddleware.Validate(schema, """{"qty":""", 10).ShouldBe(["#: InvalidJson"]);
    }

    [Fact]
    public async Task OpenApi_operations_are_found_by_backend_path_and_method()
    {
        var document = await OpenApiDocument.FromJsonAsync("""
            {"openapi":"3.0.1","info":{"title":"orders","version":"1"},"servers":[{"url":"https://orders.internal/v1"}],
             "paths":{
               "/orders/{id}":{"put":{"operationId":"replace","responses":{"200":{"description":"ok"}}}},
               "/orders/search":{"put":{"operationId":"search","responses":{"200":{"description":"ok"}}}}}}
            """, cancellationToken: TestContext.Current.CancellationToken);

        OpenApiOperations.Find(document, "/v1/orders/42", "PUT")!.OperationId.ShouldBe("replace");
        OpenApiOperations.Find(document, "/v1/orders/search", "PUT")!.OperationId.ShouldBe("search");
        OpenApiOperations.Find(document, "/v1/orders/42", "DELETE").ShouldBeNull();
        OpenApiOperations.Find(document, "/v1/customers/1", "PUT").ShouldBeNull();
    }

    [Fact]
    public void Aggregation_routes_follow_conventions_and_use_known_placeholders()
    {
        var options = new AggregationOptions
        {
            Routes =
            {
                ["bad"] = new AggregateRouteOptions
                {
                    Path = "/bff/{id}",
                    Methods = ["POST"],
                    Calls = { ["order"] = new AggregateCallOptions { ClusterId = "orders", Path = "/orders/{orderId}", Timeout = TimeSpan.Zero } },
                },
            },
        };

        var failures = new AggregationOptionsConsistencyValidator("/api", allowWildcardHosts: false).Validate(null, options).Failures!.ToList();

        failures.ShouldContain(failure => failure.Contains("public prefix"));
        failures.ShouldContain(failure => failure.Contains("Match.Hosts"));
        failures.ShouldContain(failure => failure.Contains("GET and HEAD only"));
        failures.ShouldContain(failure => failure.Contains("{orderId}"));
        failures.ShouldContain(failure => failure.Contains("Timeout"));
    }

    [Fact]
    public void Aggregate_call_paths_are_expanded_with_escaped_route_values()
    {
        var values = new RouteValueDictionary { ["id"] = "a b", ["rest"] = "x/y z" };

        AggregateRequestHandler.ExpandPath("/orders/{id}/files/{rest}", values).ShouldBe("/orders/a%20b/files/x/y%20z");
    }

    [Fact]
    public void Embedded_problems_have_the_problem_response_shape()
    {
        var writer = new ProblemResponseWriter(
            new ResxErrorMessageLocalizer(),
            new AcceptLanguageResolver(TestOptions.Errors()),
            new ErrorMetrics(new TestMeterFactory()),
            TestOptions.Errors());
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/bff/summary/1";
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            writer.WriteEmbedded(json, context, GatewayErrors.UpstreamTimeout);
        }

        using var problem = JsonDocument.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
        problem.RootElement.EnumerateObject().Select(static member => member.Name).ShouldBe(["type", "title", "status", "detail", "instance", "code", "traceId"]);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("gateway.upstream_timeout");
        context.Response.HasStarted.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(200);
    }

    private sealed class RecordingProblemWriter : IProblemResponseWriter
    {
        public GatewayError? Written { get; private set; }

        public ValueTask<bool> WriteAsync(HttpContext context, GatewayError error, ProblemWriteArgs? args = null)
        {
            Written = error;
            return ValueTask.FromResult(true);
        }

        public void WriteEmbedded(Utf8JsonWriter writer, HttpContext context, GatewayError error) => Written = error;
    }

    private sealed class MaxBodySize(long? limit) : Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = limit;
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body must not be read.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The body must not be read.");
    }
}
