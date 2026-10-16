using System.Diagnostics;
using ApiGateway.Abstractions.Features;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Observability.IO;
using ApiGateway.Observability.Usage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Metrics;

/// <summary>
/// Measures every request on the public listener: total duration, upstream wait, gateway overhead, body sizes,
/// labeled by route/cluster/method/status class/client type. Adds the gateway's labels to ASP.NET Core's own
/// <c>http.server.request.duration</c> and feeds per-consumer usage analytics.
/// </summary>
public sealed class RequestMetricsMiddleware(
    RequestDelegate next,
    GatewayMetrics metrics,
    UsageAggregator usage,
    IOptions<ObservabilityOptions> options)
{
    private readonly bool _countBytes = options.Value.Metrics.CountBodyBytes;

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();

        ObservedReadStream? requestBody = null;
        ObservedWriteStream? responseBody = null;
        var originalRequestBody = context.Request.Body;
        var originalResponseBody = context.Response.Body;
        if (_countBytes)
        {
            requestBody = new ObservedReadStream(originalRequestBody);
            responseBody = new ObservedWriteStream(originalResponseBody);
            context.Request.Body = requestBody;
            context.Response.Body = responseBody;
        }

        var sizes = new BodySizeFeature(requestBody, responseBody);
        context.Features.Set<IBodySizeFeature>(sizes);

        try
        {
            await next(context);
        }
        finally
        {
            if (_countBytes)
            {
                context.Request.Body = originalRequestBody;
                context.Response.Body = originalResponseBody;
            }

            Record(context, Stopwatch.GetElapsedTime(started), sizes);
        }
    }

    private void Record(HttpContext context, TimeSpan duration, BodySizeFeature sizes)
    {
        var gateway = context.FindGatewayRequest();
        var status = context.Response.StatusCode;

        // Labels on the framework's own request-duration metric, so dashboards can use either.
        if (context.Features.Get<IHttpMetricsTagsFeature>() is { } frameworkTags)
        {
            frameworkTags.Tags.Add(new(GatewayTelemetry.Tags.RouteId, gateway?.RouteId ?? GatewayTelemetry.Unknown));
            frameworkTags.Tags.Add(new(GatewayTelemetry.Tags.ClusterId, gateway?.ClusterId ?? GatewayTelemetry.Unknown));
        }

        var measurement = new RequestMeasurement(
            Method: context.Request.Method,
            StatusCode: status,
            RouteId: gateway?.RouteId,
            ClusterId: gateway?.ClusterId,
            ClientType: gateway?.ClientType,
            ConsumerId: gateway?.ClientId,
            Duration: duration,
            UpstreamWait: context.Features.Get<IUpstreamTimingFeature>().UpstreamWait(),
            RequestBytes: sizes.RequestBytes,
            ResponseBytes: sizes.ResponseBytes,
            IsSynthetic: gateway?.IsSynthetic ?? false);

        metrics.RecordRequest(measurement);

        if (!measurement.IsSynthetic)
        {
            usage.Record(gateway?.ClientId, gateway?.RouteId, status, sizes.RequestBytes ?? 0, sizes.ResponseBytes ?? 0);
        }
    }

    private sealed class BodySizeFeature(ObservedReadStream? request, ObservedWriteStream? response) : IBodySizeFeature
    {
        public long? RequestBytes => request?.BytesRead;

        public long? ResponseBytes => response?.BytesWritten;
    }
}
