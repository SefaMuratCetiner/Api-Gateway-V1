using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Observability.IO;
using ApiGateway.Observability.Redaction;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ApiGateway.Observability.BodyLogging;

/// <summary>
/// Proxy-pipeline middleware that logs request/response bodies for routes that opted in. Bodies are teed while
/// they stream (first N bytes only), redacted (sensitive JSON properties, secret patterns) and written as one
/// event in the <c>ApiGateway.Body</c> category after the exchange. Nothing is buffered, so uploads, downloads
/// and server-sent events are unaffected.
/// </summary>
public sealed partial class BodyCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RoutePolicyResolver<BodyLoggingPolicy> _policies;
    private readonly JsonBodyRedactor _redactor;
    private readonly ILogger _logger;

    public BodyCaptureMiddleware(
        RequestDelegate next,
        RoutePolicyResolver<BodyLoggingPolicy> policies,
        JsonBodyRedactor redactor,
        ILoggerFactory loggerFactory)
    {
        _next = next;
        _policies = policies;
        _redactor = redactor;
        _logger = loggerFactory.CreateLogger(GatewayTelemetry.BodyLogCategory);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var policy = _policies.Resolve(context);
        if (!policy.Enabled || !_logger.IsEnabled(LogLevel.Information))
        {
            await _next(context);
            return;
        }

        var captureRequest = policy.Request && policy.Allows(context.Request.ContentType);
        var originalRequest = context.Request.Body;
        var originalResponse = context.Response.Body;
        var request = captureRequest ? new ObservedReadStream(originalRequest, policy.MaxBytes) : null;
        var response = policy.Response ? new ObservedWriteStream(originalResponse, policy.MaxBytes) : null;

        if (request is not null)
        {
            context.Request.Body = request;
        }

        if (response is not null)
        {
            context.Response.Body = response;
        }

        try
        {
            await _next(context);
        }
        finally
        {
            context.Request.Body = originalRequest;
            context.Response.Body = originalResponse;
            Log(context, policy, request, response);
        }
    }

    private void Log(HttpContext context, BodyLoggingPolicy policy, ObservedReadStream? request, ObservedWriteStream? response)
    {
        var requestBody = request is null
            ? null
            : _redactor.Redact(request.Captured.Span, BodyLoggingPolicy.IsJson(context.Request.ContentType), request.CaptureTruncated);

        var responseContentType = context.Response.ContentType;
        var responseBody = response is null || !policy.Allows(responseContentType)
            ? null
            : _redactor.Redact(response.Captured.Span, BodyLoggingPolicy.IsJson(responseContentType), response.CaptureTruncated);

        if (requestBody is null && responseBody is null)
        {
            return;
        }

        LogBodies(_logger, context.Request.Method, RouteContext.FindRoute(context)?.Config.RouteId ?? GatewayTelemetry.Unknown, context.Response.StatusCode, requestBody, responseBody);
    }

    [LoggerMessage(EventId = 3200, Level = LogLevel.Information, Message = "Bodies of {Method} on route {RouteId} ({StatusCode}): request {RequestBody}, response {ResponseBody}")]
    private static partial void LogBodies(ILogger logger, string method, string routeId, int statusCode, string? requestBody, string? responseBody);
}
