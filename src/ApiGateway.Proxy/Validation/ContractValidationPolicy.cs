using ApiGateway.Abstractions.Routing;
using ApiGateway.Proxy.Routing;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Validation;

/// <summary>Per-route request validation, read from route metadata.</summary>
/// <param name="ContractUrl">The backend's published contract, or <c>null</c> when the route is not validated.</param>
/// <param name="Kind">What the contract URL serves.</param>
/// <param name="MaxBodyBytes">Largest request body buffered for validation (413 beyond it).</param>
public sealed record ContractValidationPolicy(Uri? ContractUrl, ContractKind Kind, int MaxBodyBytes)
{
    public bool Enabled => ContractUrl is not null;
}

/// <summary>
/// Route metadata keys: <c>Validation.Contract</c> (absolute URL of the backend's published contract),
/// <c>Validation.ContractType</c> (<c>OpenApi</c>, the default, or <c>JsonSchema</c>) and <c>Validation.MaxBodyBytes</c>.
/// </summary>
public sealed class ContractValidationMetadataParser(IOptions<ContractValidationOptions> options) : IRouteMetadataParser<ContractValidationPolicy>
{
    public const string ContractKey = "Validation.Contract";
    public const string ContractTypeKey = "Validation.ContractType";
    public const string MaxBodyBytesKey = "Validation.MaxBodyBytes";

    private static readonly IReadOnlySet<string> KnownKeys =
        new HashSet<string>(StringComparer.Ordinal) { ContractKey, ContractTypeKey, MaxBodyBytesKey };

    private readonly int _defaultMaxBodyBytes = options.Value.DefaultMaxBodyBytes;

    public string Prefix => "Validation.";

    public ContractValidationPolicy Parse(IReadOnlyDictionary<string, string>? metadata) => new(
        ParseUrl(RouteMetadata.Get(metadata, ContractKey)),
        Enum.TryParse<ContractKind>(RouteMetadata.Get(metadata, ContractTypeKey), ignoreCase: true, out var kind) ? kind : ContractKind.OpenApi,
        RouteMetadata.GetInt(metadata, MaxBodyBytesKey, _defaultMaxBodyBytes));

    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        RouteMetadata.ValidateKnownKeys(metadata, Prefix, KnownKeys, problems);
        RouteMetadata.ValidateInt(metadata, MaxBodyBytesKey, 1024, 64 * 1024 * 1024, problems);

        var url = RouteMetadata.Get(metadata, ContractKey);
        if (url is not null && ParseUrl(url) is null)
        {
            problems.Add($"'{ContractKey}' must be an absolute http(s) URL, got '{url}'");
        }

        var type = RouteMetadata.Get(metadata, ContractTypeKey);
        if (type is not null && !Enum.TryParse<ContractKind>(type, ignoreCase: true, out _))
        {
            problems.Add($"'{ContractTypeKey}' must be OpenApi or JsonSchema, got '{type}'");
        }

        return problems;
    }

    private static Uri? ParseUrl(string? raw) => HttpUrl.Parse(raw);
}
