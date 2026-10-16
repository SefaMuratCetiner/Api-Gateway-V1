using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Synthetic;

public sealed class SyntheticProbeOptions
{
    /// <summary>Short, bounded name; used as a metric label.</summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9-]{0,40}$")]
    public string Name { get; set; } = "";

    /// <summary>Absolute URL through the gateway's public listener, e.g. <c>http://localhost:8080/api/orders/health</c>.</summary>
    [Required]
    [Url]
    public string Url { get; set; } = "";

    [Required]
    public string Method { get; set; } = "GET";

    [Range(100, 599)]
    public int ExpectedStatus { get; set; } = 200;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
