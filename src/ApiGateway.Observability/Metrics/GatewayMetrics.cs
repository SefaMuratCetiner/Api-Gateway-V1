using System.Diagnostics;
using System.Diagnostics.Metrics;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Observability.Options;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Metrics;

/// <summary>One finished request, as seen by the metrics.</summary>
public readonly record struct RequestMeasurement(
    string Method,
    int StatusCode,
    string? RouteId,
    string? ClusterId,
    string? ClientType,
    string? ConsumerId,
    TimeSpan Duration,
    TimeSpan? UpstreamWait,
    long? RequestBytes,
    long? ResponseBytes,
    bool IsSynthetic);

/// <summary>
/// The gateway's own instruments (meter <c>ApiGateway</c>). Labels are restricted to bounded values: route and
/// cluster IDs, method, status class, client type and (capped) consumer, never paths or raw IDs.
/// ASP.NET Core's built-in <c>http.server.request.duration</c> is enriched with the same route/cluster labels.
/// </summary>
public sealed class GatewayMetrics : IGatewayMetrics
{
    private static readonly string[] KnownMethods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE", "CONNECT"];

    private readonly CardinalityGuard _guard;
    private readonly MetricsOptions _options;
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _upstreamDuration;
    private readonly Histogram<double> _overheadDuration;
    private readonly Histogram<long> _requestSize;
    private readonly Histogram<long> _responseSize;
    private readonly Counter<long> _rejections;
    private readonly Counter<long> _consumerRequests;
    private readonly Counter<long> _healthChanges;

    public GatewayMetrics(IMeterFactory meterFactory, CardinalityGuard guard, IOptions<ObservabilityOptions> options)
    {
        _guard = guard;
        _options = options.Value.Metrics;
        var meter = meterFactory.Create(GatewayTelemetry.Name);

        _requests = meter.CreateCounter<long>("gateway.requests", unit: "{request}", description: "Requests handled by the gateway.");
        _duration = meter.CreateHistogram<double>("gateway.request.duration", unit: "s", description: "Total time the gateway spent on a request, including the upstream.");
        _upstreamDuration = meter.CreateHistogram<double>("gateway.upstream.duration", unit: "s", description: "Time from sending a request upstream until its response headers arrived.");
        _overheadDuration = meter.CreateHistogram<double>("gateway.overhead.duration", unit: "s", description: "Latency the gateway added: total time minus upstream wait.");
        _requestSize = meter.CreateHistogram<long>("gateway.request.body.size", unit: "By", description: "Request body bytes received from clients.");
        _responseSize = meter.CreateHistogram<long>("gateway.response.body.size", unit: "By", description: "Response body bytes sent to clients.");
        _rejections = meter.CreateCounter<long>("gateway.rejections", unit: "{request}", description: "Requests the gateway turned away, by reason.");
        _consumerRequests = meter.CreateCounter<long>("gateway.consumer.requests", unit: "{request}", description: "Requests per consumer (capped label).");
        _healthChanges = meter.CreateCounter<long>("gateway.destination.health_changes", unit: "{change}", description: "Destination health transitions, by cluster and new state.");
    }

    public void RecordRequest(in RequestMeasurement measurement)
    {
        var route = _guard.Guard(GatewayTelemetry.Tags.RouteId, measurement.RouteId, _options.MaxLabelValues);
        var cluster = _guard.Guard(GatewayTelemetry.Tags.ClusterId, measurement.ClusterId, _options.MaxLabelValues);
        var clientType = _guard.Guard(GatewayTelemetry.Tags.ClientType, measurement.ClientType, _options.MaxLabelValues);

        var tags = new TagList
        {
            { GatewayTelemetry.Tags.RouteId, route },
            { GatewayTelemetry.Tags.ClusterId, cluster },
            { GatewayTelemetry.Tags.Method, NormalizeMethod(measurement.Method) },
            { GatewayTelemetry.Tags.StatusClass, StatusClassOf(measurement.StatusCode) },
            { GatewayTelemetry.Tags.ClientType, clientType },
            { GatewayTelemetry.Tags.Synthetic, measurement.IsSynthetic },
        };

        _requests.Add(1, tags);
        _duration.Record(measurement.Duration.TotalSeconds, tags);

        if (measurement.UpstreamWait is { } upstream)
        {
            var routeTags = new TagList { { GatewayTelemetry.Tags.RouteId, route }, { GatewayTelemetry.Tags.ClusterId, cluster } };
            _upstreamDuration.Record(upstream.TotalSeconds, routeTags);
            _overheadDuration.Record(Math.Max(0, (measurement.Duration - upstream).TotalSeconds), routeTags);
        }

        if (measurement.RequestBytes is { } inBytes)
        {
            _requestSize.Record(inBytes, new TagList { { GatewayTelemetry.Tags.RouteId, route } });
        }

        if (measurement.ResponseBytes is { } outBytes)
        {
            _responseSize.Record(outBytes, new TagList { { GatewayTelemetry.Tags.RouteId, route } });
        }

        if (!measurement.IsSynthetic && _options.MaxConsumerLabelValues > 0)
        {
            var consumer = _guard.Guard(GatewayTelemetry.Tags.Consumer, measurement.ConsumerId, _options.MaxConsumerLabelValues);
            _consumerRequests.Add(1, new TagList
            {
                { GatewayTelemetry.Tags.Consumer, consumer },
                { GatewayTelemetry.Tags.StatusClass, StatusClassOf(measurement.StatusCode) },
            });
        }
    }

    public void RecordRejection(RejectionReason reason, string? routeId) =>
        _rejections.Add(1, new TagList
        {
            { GatewayTelemetry.Tags.Reason, ReasonOf(reason) },
            { GatewayTelemetry.Tags.RouteId, _guard.Guard(GatewayTelemetry.Tags.RouteId, routeId, _options.MaxLabelValues) },
        });

    public void RecordHealthChange(string clusterId, string newHealth) =>
        _healthChanges.Add(1, new TagList
        {
            { GatewayTelemetry.Tags.ClusterId, _guard.Guard(GatewayTelemetry.Tags.ClusterId, clusterId, _options.MaxLabelValues) },
            { GatewayTelemetry.Tags.Health, newHealth },
        });

    /// <summary><c>2xx</c>, <c>4xx</c>, ...: bounded, unlike raw status codes from arbitrary backends.</summary>
    public static string StatusClassOf(int status) => status switch
    {
        >= 100 and < 200 => "1xx",
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 and < 600 => "5xx",
        _ => "other",
    };

    /// <summary>Known methods as is, everything else as <c>_OTHER</c> (methods are client-controlled).</summary>
    public static string NormalizeMethod(string method)
    {
        foreach (var known in KnownMethods)
        {
            if (string.Equals(known, method, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return "_OTHER";
    }

    public static string ReasonOf(RejectionReason reason) => reason switch
    {
        RejectionReason.RateLimit => "rate_limit",
        RejectionReason.Quota => "quota",
        RejectionReason.Token => "token",
        RejectionReason.Shed => "shed",
        RejectionReason.Concurrency => "concurrency",
        RejectionReason.Blocked => "blocked",
        RejectionReason.Maintenance => "maintenance",
        _ => "other",
    };
}
