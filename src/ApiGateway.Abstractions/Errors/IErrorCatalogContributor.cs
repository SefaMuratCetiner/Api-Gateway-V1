namespace ApiGateway.Abstractions.Errors;

/// <summary>
/// Adds gateway errors to the catalog. Each library that defines its own codes registers one contributor;
/// the catalog validates all of them at startup (unique, <c>gateway.</c>-prefixed, localized).
/// </summary>
public interface IErrorCatalogContributor
{
    IEnumerable<GatewayError> GetErrors();
}
