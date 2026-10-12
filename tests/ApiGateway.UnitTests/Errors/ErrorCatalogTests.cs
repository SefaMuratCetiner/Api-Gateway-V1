using System.Globalization;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors.Localization;
using ApiGateway.Errors.Problems;

namespace ApiGateway.UnitTests.Errors;

public sealed class ErrorCatalogTests
{
    [Fact]
    public void Built_in_errors_form_a_valid_catalogue()
    {
        var catalog = new GatewayErrorCatalog([new BuiltInErrorCatalogContributor()]);

        catalog.Errors.Count.ShouldBe(GatewayErrors.All.Count);
        catalog.Errors.ShouldAllBe(static error => error.Code.StartsWith(GatewayErrors.Prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_codes_without_the_gateway_prefix()
    {
        var contributor = new StaticContributor(new GatewayError("orders.not_found", 404, "X_Title"));

        Should.Throw<InvalidOperationException>(() => new GatewayErrorCatalog([contributor]));
    }

    [Fact]
    public void Rejects_a_code_registered_twice_with_different_status()
    {
        var contributor = new StaticContributor(new GatewayError(GatewayErrors.RouteNotFound.Code, 410, "X_Title"));

        Should.Throw<InvalidOperationException>(() => new GatewayErrorCatalog([new BuiltInErrorCatalogContributor(), contributor]));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void Every_built_in_error_is_translated(string culture)
    {
        var missing = new ResxErrorMessageLocalizer().FindMissingKeys(GatewayErrors.All, CultureInfo.GetCultureInfo(culture));

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Status_map_matches_the_checklist()
    {
        GatewayErrors.InvalidToken.Status.ShouldBe(401);
        GatewayErrors.Forbidden.Status.ShouldBe(403);
        GatewayErrors.RouteNotFound.Status.ShouldBe(404);
        GatewayErrors.MethodNotAllowed.Status.ShouldBe(405);
        GatewayErrors.PayloadTooLarge.Status.ShouldBe(413);
        GatewayErrors.UriTooLong.Status.ShouldBe(414);
        GatewayErrors.HeadersTooLarge.Status.ShouldBe(431);
        GatewayErrors.RateLimited.Status.ShouldBe(429);
        GatewayErrors.BadUpstreamResponse.Status.ShouldBe(502);
        GatewayErrors.NoHealthyDestination.Status.ShouldBe(503);
        GatewayErrors.Overloaded.Status.ShouldBe(503);
        GatewayErrors.Maintenance.Status.ShouldBe(503);
        GatewayErrors.UpstreamTimeout.Status.ShouldBe(504);
    }

    private sealed class StaticContributor(params GatewayError[] errors) : IErrorCatalogContributor
    {
        public IEnumerable<GatewayError> GetErrors() => errors;
    }
}
