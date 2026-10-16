using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;

namespace ApiGateway.Proxy.Compression;

/// <summary>Configuration section <c>Compression</c>: Brotli/gzip for responses, including HTTPS ones.</summary>
public sealed class CompressionOptions
{
    public const string SectionName = "Compression";

    public bool Enabled { get; set; } = true;

    /// <summary>Media types compressed in addition to the framework defaults (text, JSON, XML).</summary>
    public string[] AdditionalMimeTypes { get; set; } = ["application/problem+json"];
}

/// <summary>Per-route response compression, read from route metadata. On unless a route opts out.</summary>
/// <param name="Enabled">Compress this route's responses.</param>
public sealed record CompressionPolicy(bool Enabled);

/// <summary>
/// Route metadata key <c>Compression.Enabled</c> (true/false). Set it to <c>false</c> on routes whose responses mix
/// secrets (tokens, CSRF values) with attacker-controlled input: compressing those enables BREACH-style attacks.
/// </summary>
public sealed class CompressionMetadataParser : IRouteMetadataParser<CompressionPolicy>
{
    public const string EnabledKey = "Compression.Enabled";

    private static readonly IReadOnlySet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal) { EnabledKey };
    private static readonly CompressionPolicy On = new(Enabled: true);
    private static readonly CompressionPolicy Off = new(Enabled: false);

    public string Prefix => "Compression.";

    public CompressionPolicy Parse(IReadOnlyDictionary<string, string>? metadata) =>
        RouteMetadata.GetBool(metadata, EnabledKey, fallback: true) ? On : Off;

    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        RouteMetadata.ValidateKnownKeys(metadata, Prefix, KnownKeys, problems);
        RouteMetadata.ValidateBool(metadata, EnabledKey, problems);
        return problems;
    }
}

/// <summary>
/// The framework's compression provider (Brotli, gzip) with the per-route opt-out applied. Responses a backend already
/// encoded, and range responses, are never re-compressed (framework behaviour).
/// </summary>
public sealed class RouteAwareResponseCompressionProvider(
    ResponseCompressionProvider inner,
    RoutePolicyResolver<CompressionPolicy> policies) : IResponseCompressionProvider
{
    public ICompressionProvider? GetCompressionProvider(HttpContext context) => inner.GetCompressionProvider(context);

    public bool ShouldCompressResponse(HttpContext context) =>
        policies.Resolve(context).Enabled && inner.ShouldCompressResponse(context);

    public bool CheckRequestAcceptsCompression(HttpContext context) => inner.CheckRequestAcceptsCompression(context);
}
