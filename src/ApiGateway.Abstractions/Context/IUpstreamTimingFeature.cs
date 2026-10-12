using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Context;

/// <summary>
/// Timestamps (<see cref="Stopwatch.GetTimestamp"/>) of the upstream exchange, so the gateway can report
/// how long it waited for the backend and how much latency it added itself.
/// </summary>
public interface IUpstreamTimingFeature
{
    /// <summary>When the forwarder started sending the request upstream; 0 if it never did.</summary>
    long SendStarted { get; set; }

    /// <summary>When the upstream response headers arrived; 0 if they never did.</summary>
    long ResponseHeadersReceived { get; set; }

    /// <summary>When the forwarder finished, including body transfer; 0 if it has not.</summary>
    long ForwarderStopped { get; set; }
}

public sealed class UpstreamTimingFeature : IUpstreamTimingFeature
{
    public long SendStarted { get; set; }

    public long ResponseHeadersReceived { get; set; }

    public long ForwarderStopped { get; set; }
}

public static class UpstreamTimingHttpContextExtensions
{
    public static IUpstreamTimingFeature GetUpstreamTiming(this HttpContext context)
    {
        var feature = context.Features.Get<IUpstreamTimingFeature>();
        if (feature is null)
        {
            feature = new UpstreamTimingFeature();
            context.Features.Set(feature);
        }

        return feature;
    }

    /// <summary>Time from sending upstream to receiving response headers (time to first byte), if known.</summary>
    public static TimeSpan? UpstreamWait(this IUpstreamTimingFeature? timing) =>
        timing is { SendStarted: > 0, ResponseHeadersReceived: > 0 } && timing.ResponseHeadersReceived >= timing.SendStarted
            ? Stopwatch.GetElapsedTime(timing.SendStarted, timing.ResponseHeadersReceived)
            : null;
}
