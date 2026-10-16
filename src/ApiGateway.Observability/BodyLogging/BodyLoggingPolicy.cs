using ApiGateway.Abstractions.Routing;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.BodyLogging;

/// <summary>Per-route body logging, read from route metadata. Off unless a route opts in.</summary>
/// <param name="Enabled">Log bodies of this route.</param>
/// <param name="MaxBytes">Bytes captured per body.</param>
/// <param name="ContentTypes">Media types logged; others are counted but not captured.</param>
/// <param name="Request">Capture the request body.</param>
/// <param name="Response">Capture the response body.</param>
public sealed record BodyLoggingPolicy(bool Enabled, int MaxBytes, IReadOnlyList<string> ContentTypes, bool Request, bool Response)
{
    public bool Allows(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }

        var mediaType = contentType.AsSpan();
        var semicolon = mediaType.IndexOf(';');
        if (semicolon >= 0)
        {
            mediaType = mediaType[..semicolon];
        }

        mediaType = mediaType.Trim();
        foreach (var allowed in ContentTypes)
        {
            if (mediaType.Equals(allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsJson(string? contentType) =>
        contentType is not null
        && (contentType.Contains("/json", StringComparison.OrdinalIgnoreCase) || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Route metadata keys:
/// <c>BodyLogging.Enabled</c> (true/false),
/// <c>BodyLogging.MaxBytes</c> (int, capped by <c>HardMaxBytes</c>),
/// <c>BodyLogging.ContentTypes</c> (comma-separated),
/// <c>BodyLogging.Request</c> / <c>.Response</c> (true/false, default true).
/// </summary>
public sealed class BodyLoggingMetadataParser(IOptions<ObservabilityOptions> options) : IRouteMetadataParser<BodyLoggingPolicy>
{
    public const string EnabledKey = "BodyLogging.Enabled";
    public const string MaxBytesKey = "BodyLogging.MaxBytes";
    public const string ContentTypesKey = "BodyLogging.ContentTypes";
    public const string RequestKey = "BodyLogging.Request";
    public const string ResponseKey = "BodyLogging.Response";

    private static readonly IReadOnlySet<string> KnownKeys =
        new HashSet<string>(StringComparer.Ordinal) { EnabledKey, MaxBytesKey, ContentTypesKey, RequestKey, ResponseKey };

    /// <summary>Keys used before the <c>BodyLogging.</c> prefix; rejected so old configuration fails loudly.</summary>
    private static readonly Dictionary<string, string> RenamedKeys = new(StringComparer.Ordinal)
    {
        ["Observability.BodyLogging"] = EnabledKey,
        ["Observability.BodyLogging.MaxBytes"] = MaxBytesKey,
        ["Observability.BodyLogging.ContentTypes"] = ContentTypesKey,
        ["Observability.BodyLogging.Request"] = RequestKey,
        ["Observability.BodyLogging.Response"] = ResponseKey,
    };

    private readonly BodyLoggingOptions _options = options.Value.BodyLogging;

    public string Prefix => "BodyLogging.";

    public BodyLoggingPolicy Parse(IReadOnlyDictionary<string, string>? metadata)
    {
        var contentTypes = RouteMetadata.GetList(metadata, ContentTypesKey);
        return new BodyLoggingPolicy(
            Enabled: RouteMetadata.GetBool(metadata, EnabledKey, fallback: false),
            MaxBytes: Math.Min(RouteMetadata.GetInt(metadata, MaxBytesKey, _options.DefaultMaxBytes), _options.HardMaxBytes),
            ContentTypes: contentTypes.Count > 0 ? contentTypes : _options.DefaultContentTypes,
            Request: RouteMetadata.GetBool(metadata, RequestKey, fallback: true),
            Response: RouteMetadata.GetBool(metadata, ResponseKey, fallback: true));
    }

    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        RouteMetadata.ValidateKnownKeys(metadata, Prefix, KnownKeys, problems);
        RouteMetadata.ValidateRenamedKeys(metadata, RenamedKeys, problems);
        RouteMetadata.ValidateBool(metadata, EnabledKey, problems);
        RouteMetadata.ValidateBool(metadata, RequestKey, problems);
        RouteMetadata.ValidateBool(metadata, ResponseKey, problems);
        RouteMetadata.ValidateInt(metadata, MaxBytesKey, 1, _options.HardMaxBytes, problems);
        return problems;
    }
}
