using System.ComponentModel.DataAnnotations;
using ApiGateway.Proxy.Routing;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Validation;

/// <summary>Configuration section <c>Validation</c>: how backend contracts are fetched and request bodies validated.</summary>
public sealed class ContractValidationOptions
{
    public const string SectionName = "Validation";

    /// <summary>How often published contracts are re-fetched (conditional GET with the last ETag).</summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Largest request body buffered for validation, unless a route sets <c>Validation.MaxBodyBytes</c>.</summary>
    [Range(1024, 64 * 1024 * 1024)]
    public int DefaultMaxBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>Validation errors listed in the problem's <c>errors</c> member.</summary>
    [Range(1, 100)]
    public int MaxReportedErrors { get; set; } = 20;
}

[OptionsValidator]
public sealed partial class ContractValidationOptionsValidator : IValidateOptions<ContractValidationOptions>;

/// <summary>What kind of document a route's contract URL serves.</summary>
public enum ContractKind
{
    /// <summary>OpenAPI 3 (JSON): the operation is found by the forwarded path and method.</summary>
    OpenApi,

    /// <summary>A JSON Schema the request body must match.</summary>
    JsonSchema,
}

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
