namespace ApiGateway.Proxy.Compression;

/// <summary>Configuration section <c>Compression</c>: Brotli/gzip for responses, including HTTPS ones.</summary>
public sealed class CompressionOptions
{
    public const string SectionName = "Compression";

    public bool Enabled { get; set; } = true;

    /// <summary>Media types compressed in addition to the framework defaults (text, JSON, XML).</summary>
    public string[] AdditionalMimeTypes { get; set; } = ["application/problem+json"];
}
