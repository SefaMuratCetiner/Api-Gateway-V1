using ApiGateway.Proxy.Normalization;
using ApiGateway.Proxy.Routing;
using System.Text.Json;
using ApiGateway.Abstractions.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiGateway.UnitTests.Proxy;

public sealed class NormalizationTests
{
    [Theory]
    [InlineData("/api/orders/1", RequestTargetProblem.None)]
    [InlineData("/api/orders/caf%C3%A9?q=%E2%82%AC", RequestTargetProblem.None)]
    [InlineData("/api/orders/%252e%252e/admin", RequestTargetProblem.DoubleEncoding)]
    [InlineData("/api/orders/%c0%ae%c0%ae/admin", RequestTargetProblem.InvalidUtf8)]
    [InlineData("/api/orders/%e0%80%af", RequestTargetProblem.InvalidUtf8)]
    [InlineData("/api/orders/a%00b", RequestTargetProblem.ControlCharacter)]
    [InlineData("/api/orders/a%0d%0ab", RequestTargetProblem.ControlCharacter)]
    [InlineData("/api/orders?note=a%00b", RequestTargetProblem.ControlCharacter)]
    [InlineData("/api/orders/%zz", RequestTargetProblem.MalformedEncoding)]
    [InlineData("/api/orders?discount=50%", RequestTargetProblem.None)]
    [InlineData("/api/orders/..%2Fadmin", RequestTargetProblem.EncodedSlash)]
    [InlineData("/api/orders/..%5cadmin", RequestTargetProblem.EncodedSlash)]
    [InlineData("/api/orders/..\\admin", RequestTargetProblem.EncodedSlash)]
    [InlineData("http://api.example.com/api/orders/%252e", RequestTargetProblem.DoubleEncoding)]
    [InlineData("*", RequestTargetProblem.None)]
    public void Raw_targets_are_inspected(string target, RequestTargetProblem expected) =>
        RequestTargetInspector.Inspect(target, allowEncodedSlashes: false).ShouldBe(expected);

    [Fact]
    public void Encoded_slashes_can_be_allowed() =>
        RequestTargetInspector.Inspect("/api/files/a%2Fb", allowEncodedSlashes: true).ShouldBe(RequestTargetProblem.None);

    [Theory]
    [InlineData("/api/orders/%2e%2e/admin", RequestTargetProblem.DoubleEncoding)]
    [InlineData("/api/orders/a%2Fb", RequestTargetProblem.EncodedSlash)]
    [InlineData("/api/orders/a\0b", RequestTargetProblem.ControlCharacter)]
    [InlineData("/api/orders/50%", RequestTargetProblem.None)]
    public void Decoded_paths_are_inspected(string path, RequestTargetProblem expected) =>
        RequestTargetInspector.InspectDecodedPath(path, allowEncodedSlashes: false).ShouldBe(expected);

    [Theory]
    [InlineData("/api//orders///1", "/api/orders/1")]
    [InlineData("/api/orders/./1/", "/api/orders/1")]
    [InlineData("/api/public/../admin", "/api/admin")]
    [InlineData("/../../etc/passwd", "/etc/passwd")]
    [InlineData("//evil.example.org/", "/evil.example.org")]
    [InlineData("/", "/")]
    public void Paths_are_canonicalized(string path, string expected) =>
        PathCanonicalizer.Canonicalize(path, removeTrailingSlash: true, lowercase: false).ShouldBe(expected);

    [Fact]
    public void Trailing_slash_and_case_rules_are_configurable()
    {
        PathCanonicalizer.Canonicalize("/api/Orders/", removeTrailingSlash: false, lowercase: false).ShouldBe("/api/Orders/");
        PathCanonicalizer.Canonicalize("/api/Orders/", removeTrailingSlash: true, lowercase: true).ShouldBe("/api/orders");
    }

    [Theory]
    [InlineData(NonCanonicalPathHandling.Rewrite, 200, null)]
    [InlineData(NonCanonicalPathHandling.Redirect, 308, "/api/orders/1?x=%2F")]
    public async Task Non_canonical_paths_are_rewritten_or_redirected(NonCanonicalPathHandling handling, int status, string? location)
    {
        var options = Options.Create(new RoutingOptions { Normalization = { Handling = handling } });
        var context = new DefaultHttpContext();
        context.Request.Path = "//api//orders/1/";
        context.Request.QueryString = new QueryString("?x=%2F");
        string? seenPath = null;
        var middleware = new RequestNormalizationMiddleware(
            ctx => { seenPath = ctx.Request.Path.Value; return Task.CompletedTask; },
            options,
            new NoProblems(),
            NullLogger<RequestNormalizationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(status);
        if (location is null)
        {
            seenPath.ShouldBe("/api/orders/1");
        }
        else
        {
            seenPath.ShouldBeNull();
            context.Response.Headers.Location.ToString().ShouldBe(location);
        }
    }

    [Theory]
    [InlineData("api.example.com", true)]
    [InlineData("API.Example.com.", true)]
    [InlineData("api.example.com:8443", true)]
    [InlineData("shop.tenants.example.com", true)]
    [InlineData("tenants.example.com", false)]
    [InlineData("evil.test", false)]
    [InlineData("", false)]
    public void Host_patterns_match_exact_names_and_wildcard_subdomains(string host, bool expected)
    {
        var patterns = new HostPatterns(["api.example.com", "*.tenants.example.com"]);

        patterns.Matches(HostString.FromUriComponent(host)).ShouldBe(expected);
    }

    [Fact]
    public void Forwarded_hosts_are_validated_like_the_host()
    {
        var allowed = new HostPatterns(["api.example.com"]);
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("api.example.com");

        HostValidationMiddleware.IsAllowed(context.Request, allowed).ShouldBeTrue();

        context.Request.Headers["X-Forwarded-Host"] = "api.example.com, evil.test";
        HostValidationMiddleware.IsAllowed(context.Request, allowed).ShouldBeFalse();
    }

    [Theory]
    [InlineData("*", false, "only allowed in Development")]
    [InlineData("*.example.com", false, "only allowed in Development")]
    [InlineData("*.example.com", true, null)]
    [InlineData("api.example.com", false, null)]
    [InlineData("[::1]:8080", false, null)]
    [InlineData("bad host", false, "is not a host name")]
    public void Host_pattern_validation(string pattern, bool allowWildcard, string? problem)
    {
        var result = HostPatterns.Problem(pattern, allowWildcard);

        if (problem is null)
        {
            result.ShouldBeNull();
        }
        else
        {
            result.ShouldNotBeNull().ShouldContain(problem);
        }
    }

    private sealed class NoProblems : IProblemResponseWriter
    {
        public ValueTask<bool> WriteAsync(HttpContext context, GatewayError error, ProblemWriteOptions? options = null) =>
            throw new InvalidOperationException($"Unexpected problem {error.Code}.");

        public void WriteEmbedded(Utf8JsonWriter writer, HttpContext context, GatewayError error) => throw new NotSupportedException();
    }
}
