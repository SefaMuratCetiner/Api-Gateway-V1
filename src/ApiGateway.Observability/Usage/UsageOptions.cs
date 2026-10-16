using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Usage;

public sealed class UsageOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Distinct consumer/route pairs held between flushes; further ones are counted under <c>_other</c>.</summary>
    [Range(10, 1_000_000)]
    public int MaxEntries { get; set; } = 50_000;
}
