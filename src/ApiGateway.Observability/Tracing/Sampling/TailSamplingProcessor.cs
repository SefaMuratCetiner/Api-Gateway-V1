using System.Diagnostics;
using OpenTelemetry;

namespace ApiGateway.Observability.Tracing.Sampling;

/// <summary>Limits of <see cref="TailSamplingProcessor"/>.</summary>
/// <param name="SlowThreshold">Local root spans at least this long are exported.</param>
/// <param name="MaxTraces">Undecided traces held at once; the oldest is dropped beyond it.</param>
/// <param name="MaxSpansPerTrace">Spans held per undecided trace; further spans of that trace are dropped.</param>
public sealed record TailSamplingLimits(TimeSpan SlowThreshold, int MaxTraces, int MaxSpansPerTrace);

/// <summary>
/// Local tail sampling in front of an export processor. Spans that the head sampler only recorded are held per
/// trace until the trace's local root span (the gateway's server span) ends. If that request failed (5xx or an
/// error status) or was slow, the whole local trace is marked sampled and exported; otherwise it is discarded.
/// Memory is bounded by trace and span count; nothing ever blocks the request path.
/// </summary>
/// <remarks>
/// Only spans of this process can be rescued. Backend spans of such traces were not sampled (the outbound
/// <c>traceparent</c> carried the unsampled flag); full-trace tail sampling belongs in the collector
/// (<c>deploy/observability/otel-collector.yaml</c>).
/// </remarks>
public sealed class TailSamplingProcessor : BaseProcessor<Activity>
{
    private readonly BaseProcessor<Activity> _inner;
    private readonly TailSamplingLimits _limits;
    private readonly Lock _lock = new();
    private readonly Dictionary<ActivityTraceId, LinkedListNode<PendingTrace>> _pending = [];
    private readonly LinkedList<PendingTrace> _order = new();
    private readonly TailSamplingMetrics _metrics;

    public TailSamplingProcessor(BaseProcessor<Activity> inner, TailSamplingLimits limits, TailSamplingMetrics metrics)
    {
        _inner = inner;
        _limits = limits;
        _metrics = metrics;
    }

    public override void OnStart(Activity data) => _inner.OnStart(data);

    public override void OnEnd(Activity data)
    {
        if (data.Recorded)
        {
            _inner.OnEnd(data);
            return;
        }

        if (!IsLocalRoot(data))
        {
            Hold(data);
            return;
        }

        var spans = Release(data.TraceId);
        var keep = IsInteresting(data, _limits.SlowThreshold);
        _metrics.RecordDecision(keep);
        if (!keep)
        {
            return;
        }

        foreach (var span in spans)
        {
            Promote(span);
        }

        Promote(data);
    }

    /// <summary>True for requests worth keeping although they were not sampled up front.</summary>
    public static bool IsInteresting(Activity root, TimeSpan slowThreshold)
    {
        if (root.Status == ActivityStatusCode.Error || root.Duration >= slowThreshold)
        {
            return true;
        }

        return root.GetTagItem("http.response.status_code") switch
        {
            int status => status >= 500,
            string text => int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var status) && status >= 500,
            _ => false,
        };
    }

    /// <summary>The first span of this process in the trace: no local parent.</summary>
    internal static bool IsLocalRoot(Activity activity) => activity.Parent is null;

    protected override bool OnForceFlush(int timeoutMilliseconds) => _inner.ForceFlush(timeoutMilliseconds);

    protected override bool OnShutdown(int timeoutMilliseconds) => _inner.Shutdown(timeoutMilliseconds);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Promote(Activity span)
    {
        span.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
        _inner.OnEnd(span);
    }

    private void Hold(Activity span)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(span.TraceId, out var node))
            {
                if (node.Value.Spans.Count < _limits.MaxSpansPerTrace)
                {
                    node.Value.Spans.Add(span);
                }

                return;
            }

            if (_pending.Count >= _limits.MaxTraces && _order.First is { } oldest)
            {
                _order.RemoveFirst();
                _pending.Remove(oldest.Value.TraceId);
                _metrics.RecordEviction();
            }

            var trace = new PendingTrace(span.TraceId);
            trace.Spans.Add(span);
            _pending[span.TraceId] = _order.AddLast(trace);
        }
    }

    private List<Activity> Release(ActivityTraceId traceId)
    {
        lock (_lock)
        {
            if (!_pending.Remove(traceId, out var node))
            {
                return [];
            }

            _order.Remove(node);
            return node.Value.Spans;
        }
    }

    private sealed class PendingTrace(ActivityTraceId traceId)
    {
        public ActivityTraceId TraceId { get; } = traceId;

        public List<Activity> Spans { get; } = [];
    }
}
