using ApiGateway.Host.CompositionRoot;

namespace ApiGateway.IntegrationTests;

/// <summary>Composition-root conventions from <c>docs/architecture.md</c> that need the Host assembly.</summary>
public sealed class GatewayCompositionTests
{
    [Fact]
    public void Every_feature_is_registered_exactly_once()
    {
        var declared = typeof(IGatewayFeature).Assembly.GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false } && typeof(IGatewayFeature).IsAssignableFrom(type))
            .ToList();
        var registered = GatewayCompositionRoot.Features.Select(static feature => feature.GetType()).ToList();

        registered.ShouldBeUnique();
        registered.ShouldBe(declared, ignoreOrder: true);
    }

    [Fact]
    public void Features_are_internal_sealed_and_live_in_Features()
    {
        GatewayCompositionRoot.Features.Select(static feature => feature.GetType()).ShouldAllBe(static type =>
            !type.IsPublic && type.IsSealed
            && type.Namespace == "ApiGateway.Host.Features"
            && type.Name.EndsWith("Feature", StringComparison.Ordinal));
    }
}
