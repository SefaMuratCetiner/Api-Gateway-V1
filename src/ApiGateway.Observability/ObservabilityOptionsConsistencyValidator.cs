using System.Net;
using ApiGateway.Observability.Redaction;
using ApiGateway.Observability.Tracing;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability;

/// <summary>Cross-field checks the attribute validator cannot express.</summary>
public sealed class ObservabilityOptionsConsistencyValidator : IValidateOptions<ObservabilityOptions>
{
    /// <summary>HMAC keys shorter than this are rejected.</summary>
    public const int MinKeyLength = 32;

    public ValidateOptionsResult Validate(string? name, ObservabilityOptions options)
    {
        var failures = new List<string>();

        foreach (var network in options.Tracing.TrustedNetworks)
        {
            if (!IPNetwork.TryParse(network, out _))
            {
                failures.Add($"Observability:Tracing:TrustedNetworks: '{network}' is not a valid CIDR range.");
            }
        }

        if (options.Tracing.TrustMode == TraceTrustMode.TrustedNetworks && options.Tracing.TrustedNetworks.Length == 0)
        {
            failures.Add("Observability:Tracing:TrustMode is TrustedNetworks but no TrustedNetworks are configured.");
        }

        if (options.ClientIp.Mode == ClientIpMode.Hash && (options.ClientIp.HashKey?.Length ?? 0) < MinKeyLength)
        {
            failures.Add($"Observability:ClientIp:HashKey must be at least {MinKeyLength} characters when Mode is Hash.");
        }

        if (options.BodyLogging.DefaultMaxBytes > options.BodyLogging.HardMaxBytes)
        {
            failures.Add("Observability:BodyLogging:DefaultMaxBytes must not exceed HardMaxBytes.");
        }

        var buckets = options.Metrics.LatencyBucketsSeconds;
        for (var i = 1; i < buckets.Length; i++)
        {
            if (buckets[i] <= buckets[i - 1])
            {
                failures.Add("Observability:Metrics:LatencyBucketsSeconds must be strictly increasing.");
                break;
            }
        }

        var synthetic = options.Synthetic;
        if (synthetic.Enabled)
        {
            if ((synthetic.SigningKey?.Length ?? 0) < MinKeyLength)
            {
                failures.Add($"Observability:Synthetic:SigningKey must be at least {MinKeyLength} characters when synthetic checks are enabled.");
            }

            var duplicates = synthetic.Probes.GroupBy(static probe => probe.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1);
            failures.AddRange(duplicates.Select(static group => $"Observability:Synthetic:Probes: name '{group.Key}' is used twice."));

            foreach (var probe in synthetic.Probes)
            {
                if (probe.Timeout >= probe.Interval)
                {
                    failures.Add($"Observability:Synthetic:Probes '{probe.Name}': Timeout must be shorter than Interval.");
                }
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
