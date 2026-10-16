using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.BodyLogging;

public sealed class BodyLoggingOptions
{
    /// <summary>Bytes captured per body when a route enables body logging without its own limit.</summary>
    [Range(64, 1_048_576)]
    public int DefaultMaxBytes { get; set; } = 4096;

    /// <summary>No route may capture more than this per body.</summary>
    [Range(64, 1_048_576)]
    public int HardMaxBytes { get; set; } = 65_536;

    /// <summary>Content types logged when a route does not list its own.</summary>
    [Required]
    public string[] DefaultContentTypes { get; set; } = ["application/json", "application/problem+json", "text/plain"];
}
