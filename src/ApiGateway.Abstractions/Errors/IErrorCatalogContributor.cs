namespace ApiGateway.Abstractions.Errors;

/// <summary>
/// Adds gateway errors to the catalogue. Each library that defines its own codes registers one contributor;
/// the catalogue validates all of them at startup (unique, <c>gateway.</c>-prefixed, localized).
/// </summary>
public interface IErrorCatalogContributor
{
    IEnumerable<GatewayError> GetErrors();
}

/// <summary>Read access to every registered gateway error.</summary>
public interface IGatewayErrorCatalog
{
    IReadOnlyCollection<GatewayError> Errors { get; }

    bool TryGet(string code, out GatewayError error);
}
