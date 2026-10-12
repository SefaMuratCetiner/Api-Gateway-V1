namespace ApiGateway.Abstractions.Errors;

/// <summary>
/// A stable error the gateway can produce: the machine-readable <see cref="Code"/>, the HTTP status, and the
/// resource keys of its localized title and detail. Codes never change once published; clients branch on them.
/// </summary>
/// <param name="Code">Stable code, e.g. <c>gateway.route_not_found</c>.</param>
/// <param name="Status">HTTP status code.</param>
/// <param name="TitleKey">Resource key of the short, human-readable title.</param>
/// <param name="DetailKey">Resource key of the longer explanation; <c>null</c> when the title says it all.</param>
public sealed record GatewayError(string Code, int Status, string TitleKey, string? DetailKey = null)
{
    public override string ToString() => Code;
}
