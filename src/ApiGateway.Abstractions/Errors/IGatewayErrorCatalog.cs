namespace ApiGateway.Abstractions.Errors;

/// <summary>Read access to every registered gateway error.</summary>
public interface IGatewayErrorCatalog
{
    IReadOnlyCollection<GatewayError> Errors { get; }

    bool TryGet(string code, out GatewayError error);
}
