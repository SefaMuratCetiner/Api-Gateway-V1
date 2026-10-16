using ApiGateway.Abstractions.Routing;

namespace ApiGateway.Proxy.Compression;

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
