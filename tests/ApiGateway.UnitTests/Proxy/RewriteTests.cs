using ApiGateway.Proxy.Headers;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Transforms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ApiGateway.UnitTests.Proxy;

public sealed class RewriteTests
{
    private static readonly HostPatterns Allowed = new(["api.example.com", "login.example.net"]);

    [Fact]
    public void Path_mapping_is_read_from_route_transforms()
    {
        PathMapping.From(null).Kind.ShouldBe(PathMappingKind.None);
        PathMapping.From([Transform("PathRemovePrefix", "/api/legacy/")]).ShouldBe(new PathMapping(PathMappingKind.RemovePrefix, new PathString("/api/legacy")));
        PathMapping.From([Transform("PathPrefix", "/v1")]).Kind.ShouldBe(PathMappingKind.AddPrefix);
        PathMapping.From([Transform("PathPattern", "/x/{**rest}")]).Kind.ShouldBe(PathMappingKind.Opaque);
        PathMapping.From([Transform("PathRemovePrefix", "/a"), Transform("PathPrefix", "/b")]).Kind.ShouldBe(PathMappingKind.Opaque);
        PathMapping.From([Transform("RequestHeader", "X-A"), Transform("Set", "1")]).Kind.ShouldBe(PathMappingKind.None);
    }

    [Theory]
    [InlineData("http://legacy.test/svc/items/3?x=1", "https://api.example.com/api/legacy/items/3?x=1")]
    [InlineData("/svc/items/3", "/api/legacy/items/3")]
    [InlineData("https://login.example.net/authorize", "https://login.example.net/authorize")]
    [InlineData("http://legacy.test/elsewhere", "http://legacy.test/elsewhere")]
    [InlineData("//evil.example.org/", "//evil.example.org/")]
    public void Location_pointing_at_the_backend_is_mapped_to_the_public_url(string location, string expected) =>
        PathRewriteTransformProvider.RewriteLocation(location, View(PathMappingKind.RemovePrefix, "/api/legacy")).ShouldBe(expected);

    [Fact]
    public void Location_is_mapped_back_through_an_added_prefix()
    {
        var view = View(PathMappingKind.AddPrefix, "/v1", destinationBase: "", outbound: "http://legacy.test/v1/items");

        PathRewriteTransformProvider.RewriteLocation("http://legacy.test/v1/items/4", view).ShouldBe("https://api.example.com/items/4");
    }

    [Fact]
    public void Cookie_domain_naming_the_backend_is_dropped_and_path_mapped()
    {
        var view = View(PathMappingKind.RemovePrefix, "/api/legacy");

        var cookies = PathRewriteTransformProvider.RewriteCookies(
            new StringValues(["sid=1; domain=legacy.test; path=/; httponly", "pref=2; domain=.example.com; path=/svc/settings"]),
            view);

        cookies[0].ShouldBe("sid=1; path=/api/legacy; httponly");
        cookies[1].ShouldBe("pref=2; domain=.example.com; path=/api/legacy/settings");
    }

    [Theory]
    [InlineData("/api/orders/2", true)]
    [InlineData("next?page=2", true)]
    [InlineData("https://api.example.com/api/orders", true)]
    [InlineData("https://login.example.net/authorize", true)]
    [InlineData("https://evil.example.org/", false)]
    [InlineData("//evil.example.org/", false)]
    [InlineData("/\\evil.example.org", false)]
    [InlineData("https:evil.example.org", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/api/orders\r\nSet-Cookie: x=1", false)]
    public void Redirect_targets_are_checked(string location, bool safe) =>
        PathRewriteTransformProvider.IsSafeRedirect(location, new HostString("api.example.com"), Allowed).ShouldBe(safe);

    [Fact]
    public void Header_name_sets_match_exact_names_and_prefixes()
    {
        var set = new HeaderNameSet(["X-Forwarded-*", "X-User-Id"]);
        var headers = new HeaderDictionary
        {
            ["x-forwarded-for"] = "1.2.3.4",
            ["X-Forwarded-Port"] = "443",
            ["x-user-id"] = "admin",
            ["X-Correlation-Id"] = "keep",
        };

        set.RemoveFrom(headers);

        headers.Keys.ShouldBe(["X-Correlation-Id"]);
    }

    private static ExchangeView View(PathMappingKind kind, string prefix, string destinationBase = "/svc", string outbound = "http://legacy.test/svc/items/1")
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.example.com");
        context.Request.Path = "/api/legacy/items/1";
        return new ExchangeView(context.Request, new Uri(outbound), "legacy.test", destinationBase, new PathMapping(kind, new PathString(prefix)));
    }

    private static Dictionary<string, string> Transform(string key, string value) => new(StringComparer.Ordinal) { [key] = value };
}
