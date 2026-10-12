using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors;
using ApiGateway.Errors.Handling;
using ApiGateway.Errors.Localization;
using ApiGateway.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace ApiGateway.UnitTests.Errors;

public sealed class HandlingTests
{
    [Fact]
    public void Empty_gateway_404_without_endpoint_becomes_route_not_found()
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = 404;

        StatusCodeProblemMiddleware.ShouldWrite(context).ShouldBeTrue();
        StatusCodeProblemMiddleware.Classify(context).ShouldBe(GatewayErrors.RouteNotFound);
    }

    [Fact]
    public void Backend_responses_are_never_rewritten()
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = 404;
        context.Features.Set<IUpstreamResponseFeature>(new UpstreamResponseFeature(404));

        StatusCodeProblemMiddleware.ShouldWrite(context).ShouldBeFalse();
    }

    [Fact]
    public void Responses_with_a_body_are_left_alone()
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = 400;
        context.Response.ContentType = "text/plain";

        StatusCodeProblemMiddleware.ShouldWrite(context).ShouldBeFalse();
    }

    [Fact]
    public void Url_over_the_soft_limit_is_414()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/" + new string('a', 300);

        ProtocolLimitsMiddleware.Check(context, new ProtocolLimitsOptions { MaxUrlLength = 256 }).ShouldBe(GatewayErrors.UriTooLong);
    }

    [Fact]
    public void Too_many_headers_is_431()
    {
        var context = new DefaultHttpContext();
        for (var i = 0; i < 20; i++)
        {
            context.Request.Headers[$"X-H{i}"] = "v";
        }

        ProtocolLimitsMiddleware.Check(context, new ProtocolLimitsOptions { MaxRequestHeaderCount = 10 }).ShouldBe(GatewayErrors.HeadersTooLarge);
    }

    [Fact]
    public void Kestrel_statuses_map_to_gateway_errors()
    {
        var context = new DefaultHttpContext();

        GatewayExceptionHandler.Classify(context, new BadHttpRequestException("x", 413)).ShouldBe(GatewayErrors.PayloadTooLarge);
        GatewayExceptionHandler.Classify(context, new BadHttpRequestException("x", 431)).ShouldBe(GatewayErrors.HeadersTooLarge);
        GatewayExceptionHandler.Classify(context, new InvalidOperationException("secret host db01")).ShouldBe(GatewayErrors.InternalError);
    }

    [Theory]
    [InlineData("tr-TR,en;q=0.5", "tr")]
    [InlineData("de-DE,de;q=0.9", "en")]
    [InlineData("de;q=1,tr;q=0.8", "tr")]
    [InlineData("tr;q=0,en;q=0.1", "en")]
    [InlineData("*", "en")]
    public void Accept_language_resolution(string header, string expected)
    {
        var resolver = new AcceptLanguageResolver(TestOptions.Errors());

        var culture = resolver.Resolve(StringWithQualityHeaderValue.ParseList([header]));

        culture.Name.ShouldBe(expected);
    }
}
