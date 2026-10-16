namespace ApiGateway.Proxy.Validation;

/// <summary>What kind of document a route's contract URL serves.</summary>
public enum ContractKind
{
    /// <summary>OpenAPI 3 (JSON): the operation is found by the forwarded path and method.</summary>
    OpenApi,

    /// <summary>A JSON Schema the request body must match.</summary>
    JsonSchema,
}
