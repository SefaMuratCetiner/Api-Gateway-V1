using System.Diagnostics;
using ApiGateway.Abstractions.Context;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;
using Yarp.Telemetry.Consumption;

namespace ApiGateway.Observability.Metrics;

/// <summary>
/// Consumes YARP's forwarder telemetry to time the upstream exchange of each request. YARP raises these events
/// synchronously on the request's own execution context, so the current <see cref="HttpContext"/> is available
/// through the accessor.
/// </summary>
public sealed class UpstreamTimingTelemetryConsumer(IHttpContextAccessor accessor) : IForwarderTelemetryConsumer
{
    public void OnForwarderStage(DateTime timestamp, ForwarderStage stage)
    {
        if (accessor.HttpContext is not { } context)
        {
            return;
        }

        var timing = context.GetUpstreamTiming();
        switch (stage)
        {
            case ForwarderStage.SendAsyncStart:
                timing.SendStarted = Stopwatch.GetTimestamp();
                break;
            case ForwarderStage.SendAsyncStop:
                timing.ResponseHeadersReceived = Stopwatch.GetTimestamp();
                break;
        }
    }

    public void OnForwarderStop(DateTime timestamp, int statusCode)
    {
        if (accessor.HttpContext is { } context)
        {
            context.GetUpstreamTiming().ForwarderStopped = Stopwatch.GetTimestamp();
        }
    }

    public void OnForwarderStart(DateTime timestamp, string destinationPrefix)
    {
    }

    public void OnForwarderFailed(DateTime timestamp, ForwarderError error)
    {
    }

    public void OnContentTransferring(DateTime timestamp, bool isRequest, long contentLength, long iops, TimeSpan readTime, TimeSpan writeTime)
    {
    }

    public void OnContentTransferred(DateTime timestamp, bool isRequest, long contentLength, long iops, TimeSpan readTime, TimeSpan writeTime, TimeSpan firstReadTime)
    {
    }

    public void OnForwarderInvoke(DateTime timestamp, string clusterId, string routeId, string destinationId)
    {
    }
}
