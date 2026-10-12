using ApiGateway.Abstractions.Context;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Observability.Metrics;
using ApiGateway.Observability.Options;
using ApiGateway.Observability.Redaction;
using ApiGateway.Proxy.Listeners;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

namespace ApiGateway.Observability.Serilog.AccessLog;

/// <summary>
/// Turns Serilog's request logging into the gateway access log: one structured event per request with method,
/// path, status and duration (from Serilog) plus route, cluster, destination, upstream wait, gateway overhead,
/// anonymized client IP, client, tenant, trace and correlation IDs, error code and body sizes.
/// Plugged into <c>UseSerilogRequestLogging</c> by the composition root.
/// </summary>
public sealed class AccessLogEnricher(
    IClientIpAnonymizer clientIp,
    QueryStringRedactor queryRedactor,
    HeaderRedactor headerRedactor,
    IOptions<ObservabilityOptions> options,
    IOptions<ListenerOptions> listeners)
{
    /// <summary>Message of the access-log event; every other field is a structured property.</summary>
    public const string MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms (route {RouteId}, upstream {UpstreamMs} ms)";

    private readonly AccessLogOptions _options = options.Value.AccessLog;
    private readonly ListenerOptions _listeners = listeners.Value;

    public void Enrich(IDiagnosticContext diagnostic, HttpContext context)
    {
        var gateway = context.FindGatewayRequest();
        diagnostic.Set("RouteId", gateway?.RouteId ?? "-");
        diagnostic.Set("ClusterId", gateway?.ClusterId);
        diagnostic.Set("DestinationId", gateway?.DestinationId);
        diagnostic.Set("ClientId", gateway?.ClientId);
        diagnostic.Set("TenantId", gateway?.TenantId);
        diagnostic.Set("ClientType", gateway?.ClientType);
        diagnostic.Set("IsSynthetic", gateway?.IsSynthetic ?? false);

        diagnostic.Set("ClientIp", clientIp.Anonymize(context.Connection.RemoteIpAddress));
        diagnostic.Set("CorrelationId", context.GetCorrelationId());
        diagnostic.Set("Protocol", context.Request.Protocol);
        diagnostic.Set("Host", context.Request.Host.Value);

        var timing = context.Features.Get<IUpstreamTimingFeature>();
        var upstream = timing.UpstreamWait();
        diagnostic.Set("UpstreamMs", upstream is { } wait ? Math.Round(wait.TotalMilliseconds, 1) : null);

        if (context.GetGatewayError() is { } error)
        {
            diagnostic.Set("ErrorCode", error.Code);
        }

        if (context.Features.Get<IBodySizeFeature>() is { } sizes)
        {
            diagnostic.Set("RequestBytes", sizes.RequestBytes);
            diagnostic.Set("ResponseBytes", sizes.ResponseBytes);
        }

        if (_options.IncludeQueryString && context.Request.QueryString.HasValue)
        {
            diagnostic.Set("QueryString", queryRedactor.Redact(context.Request.QueryString));
        }

        if (_options.LoggedRequestHeaders.Length > 0)
        {
            diagnostic.Set("RequestHeaders", headerRedactor.Select(context.Request.Headers, _options.LoggedRequestHeaders), destructureObjects: true);
        }
    }

    /// <summary>5xx and unhandled exceptions are errors, 4xx warnings; internal-listener traffic is verbose unless enabled.</summary>
    public LogEventLevel GetLevel(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        if (!_options.Enabled)
        {
            return LogEventLevel.Verbose;
        }

        if (!_options.IncludeInternalListener && _listeners.IsInternal(context))
        {
            return LogEventLevel.Verbose;
        }

        var status = context.Response.StatusCode;
        return exception is not null || status >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error
            : status >= StatusCodes.Status400BadRequest ? LogEventLevel.Warning
            : LogEventLevel.Information;
    }
}
