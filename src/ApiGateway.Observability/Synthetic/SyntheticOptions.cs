using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Synthetic;

public sealed class SyntheticOptions
{
    public bool Enabled { get; set; }

    /// <summary>HMAC key signing <c>X-Synthetic-Check</c>. Supply from the secret store.</summary>
    public string? SigningKey { get; set; }

    /// <summary>How old a synthetic signature may be.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:30:00")]
    public TimeSpan MaxSignatureAge { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Consecutive failures before a probe reports unhealthy.</summary>
    [Range(1, 100)]
    public int FailureThreshold { get; set; } = 3;

    [ValidateEnumeratedItems]
    public SyntheticProbeOptions[] Probes { get; set; } = [];
}
