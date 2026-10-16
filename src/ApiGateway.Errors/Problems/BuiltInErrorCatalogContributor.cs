using ApiGateway.Abstractions.Errors;

namespace ApiGateway.Errors.Problems;

/// <summary>Contributes the built-in <see cref="GatewayErrors"/>.</summary>
public sealed class BuiltInErrorCatalogContributor : IErrorCatalogContributor
{
    public IEnumerable<GatewayError> GetErrors() => GatewayErrors.All;
}
