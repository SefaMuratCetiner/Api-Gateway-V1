using ApiGateway.Abstractions.Routing;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Limits;

/// <summary>Per-route response limit, from metadata.</summary>
/// <param name="MaxResponseBodySize">Largest response body forwarded, or <c>null</c> when unlimited.</param>
public sealed record ResponseLimitsPolicy(long? MaxResponseBodySize);

/// <summary>Route metadata key <c>Limits.MaxResponseBodySize</c>: bytes, or <c>-1</c> for unlimited.</summary>
public sealed class ResponseLimitsMetadataParser(IOptions<RequestLimitsOptions> options) : IRouteMetadataParser<ResponseLimitsPolicy>
{
    public const string MaxResponseBodySizeKey = "Limits.MaxResponseBodySize";

    private static readonly IReadOnlySet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal) { MaxResponseBodySizeKey };

    private readonly long? _default = options.Value.MaxResponseBodySize;

    public string Prefix => "Limits.";

    public ResponseLimitsPolicy Parse(IReadOnlyDictionary<string, string>? metadata) =>
        long.TryParse(RouteMetadata.Get(metadata, MaxResponseBodySizeKey), System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? new ResponseLimitsPolicy(value < 0 ? null : value)
            : new ResponseLimitsPolicy(_default);

    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        RouteMetadata.ValidateKnownKeys(metadata, Prefix, KnownKeys, problems);
        var raw = RouteMetadata.Get(metadata, MaxResponseBodySizeKey);
        if (raw is not null && (!long.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value) || value is 0 or < -1))
        {
            problems.Add($"'{MaxResponseBodySizeKey}' must be a positive number of bytes or -1, got '{raw}'");
        }

        return problems;
    }
}
