using System.Collections.Concurrent;
using ApiGateway.Abstractions.Telemetry;

namespace ApiGateway.Observability.Metrics;

/// <summary>
/// Caps the number of distinct values per metric label. The first <c>limit</c> values seen for a label are
/// kept; later ones are reported as <see cref="GatewayTelemetry.Overflow"/>. Protects the metrics backend from
/// label explosions (a new route per tenant, a misconfigured client ID) without dropping the measurement.
/// </summary>
public sealed class CardinalityGuard
{
    private readonly ConcurrentDictionary<string, LabelValues> _labels = new(StringComparer.Ordinal);

    public string Guard(string label, string? value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return GatewayTelemetry.Unknown;
        }

        if (limit <= 0)
        {
            return GatewayTelemetry.Overflow;
        }

        var values = _labels.GetOrAdd(label, static _ => new LabelValues());
        return values.Admit(value, limit) ? value : GatewayTelemetry.Overflow;
    }

    /// <summary>Distinct values currently admitted for <paramref name="label"/>.</summary>
    public int CountOf(string label) => _labels.TryGetValue(label, out var values) ? values.Count : 0;

    private sealed class LabelValues
    {
        private readonly ConcurrentDictionary<string, byte> _admitted = new(StringComparer.Ordinal);
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public bool Admit(string value, int limit)
        {
            if (_admitted.ContainsKey(value))
            {
                return true;
            }

            // Reserve a slot first so concurrent callers cannot overshoot the limit.
            if (Interlocked.Increment(ref _count) > limit)
            {
                Interlocked.Decrement(ref _count);
                return false;
            }

            if (!_admitted.TryAdd(value, 0))
            {
                // Another thread admitted the same value meanwhile; give the slot back.
                Interlocked.Decrement(ref _count);
            }

            return true;
        }
    }
}
