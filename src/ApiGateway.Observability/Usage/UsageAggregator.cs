using System.Collections.Concurrent;
using ApiGateway.Abstractions.Telemetry;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Usage;

/// <summary>Usage of one consumer on one route during one flush interval.</summary>
public sealed record UsageRecord(
    string Consumer,
    string Route,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long Requests,
    long ClientErrors,
    long ServerErrors,
    long BytesIn,
    long BytesOut);

/// <summary>
/// In-memory per-consumer, per-route counters between flushes. Lock-free on the request path; memory bounded by
/// <c>MaxEntries</c> (further pairs are counted under <c>_other</c>, so totals stay correct).
/// </summary>
public sealed class UsageAggregator
{
    private readonly int _maxEntries;
    private readonly bool _enabled;
    private readonly TimeProvider _time;
    private ConcurrentDictionary<(string Consumer, string Route), Counters> _current = new();
    private DateTimeOffset _periodStart;

    public UsageAggregator(IOptions<ObservabilityOptions> options, TimeProvider time)
    {
        _maxEntries = options.Value.Usage.MaxEntries;
        _enabled = options.Value.Usage.Enabled;
        _time = time;
        _periodStart = time.GetUtcNow();
    }

    public void Record(string? consumer, string? route, int statusCode, long bytesIn, long bytesOut)
    {
        if (!_enabled)
        {
            return;
        }

        var map = Volatile.Read(ref _current);
        var key = (consumer ?? "anonymous", route ?? GatewayTelemetry.Unknown);
        if (!map.TryGetValue(key, out var counters))
        {
            if (map.Count >= _maxEntries)
            {
                key = (GatewayTelemetry.Overflow, GatewayTelemetry.Overflow);
            }

            counters = map.GetOrAdd(key, static _ => new Counters());
        }

        counters.Add(statusCode, bytesIn, bytesOut);
    }

    /// <summary>Swaps in fresh counters and returns the finished period.</summary>
    public IReadOnlyList<UsageRecord> Drain()
    {
        var now = _time.GetUtcNow();
        var finished = Interlocked.Exchange(ref _current, new ConcurrentDictionary<(string Consumer, string Route), Counters>());
        var start = _periodStart;
        _periodStart = now;

        return [.. finished.Select(pair => pair.Value.ToRecord(pair.Key.Consumer, pair.Key.Route, start, now))];
    }

    private sealed class Counters
    {
        private long _requests;
        private long _clientErrors;
        private long _serverErrors;
        private long _bytesIn;
        private long _bytesOut;

        public void Add(int statusCode, long bytesIn, long bytesOut)
        {
            Interlocked.Increment(ref _requests);
            if (statusCode is >= 400 and < 500)
            {
                Interlocked.Increment(ref _clientErrors);
            }
            else if (statusCode >= 500)
            {
                Interlocked.Increment(ref _serverErrors);
            }

            Interlocked.Add(ref _bytesIn, bytesIn);
            Interlocked.Add(ref _bytesOut, bytesOut);
        }

        public UsageRecord ToRecord(string consumer, string route, DateTimeOffset start, DateTimeOffset end) =>
            new(consumer, route, start, end,
                Interlocked.Read(ref _requests), Interlocked.Read(ref _clientErrors), Interlocked.Read(ref _serverErrors),
                Interlocked.Read(ref _bytesIn), Interlocked.Read(ref _bytesOut));
    }
}
