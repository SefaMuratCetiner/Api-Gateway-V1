using ApiGateway.Abstractions.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Logging;

namespace ApiGateway.Errors.Handling;

/// <summary>
/// The gateway's one exception handler. Kestrel protocol errors keep their status (400/408/413/414/431), a route
/// timeout becomes 504, everything else 500. The exception is logged with the trace ID; no exception text,
/// type name or stack trace ever reaches the client.
/// </summary>
public sealed partial class GatewayExceptionHandler(
    IProblemResponseWriter writer,
    ILogger<GatewayExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested && !IsRouteTimeout(httpContext))
        {
            // The client went away; nobody is left to answer.
            LogClientAborted(logger, httpContext.Request.Method);
            return true;
        }

        var error = Classify(httpContext, exception);
        if (error.Status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, error.Code, httpContext.Request.Method, exception);
        }
        else
        {
            LogRejected(logger, error.Code, httpContext.Request.Method, exception);
        }

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        return await writer.WriteAsync(httpContext, error);
    }

    /// <summary>Maps an exception to a gateway error without looking at its message.</summary>
    public static GatewayError Classify(HttpContext context, Exception exception) => exception switch
    {
        BadHttpRequestException bad => ForKestrelStatus(bad.StatusCode),
        OperationCanceledException when IsRouteTimeout(context) => GatewayErrors.RouteTimeout,
        _ => GatewayErrors.InternalError,
    };

    internal static GatewayError ForKestrelStatus(int status) => status switch
    {
        StatusCodes.Status413PayloadTooLarge => GatewayErrors.PayloadTooLarge,
        StatusCodes.Status414UriTooLong => GatewayErrors.UriTooLong,
        StatusCodes.Status431RequestHeaderFieldsTooLarge => GatewayErrors.HeadersTooLarge,
        StatusCodes.Status408RequestTimeout => GatewayErrors.RouteTimeout,
        _ => GatewayErrors.BadRequest,
    };

    private static bool IsRouteTimeout(HttpContext context) =>
        context.Features.Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken.IsCancellationRequested == true;

    [LoggerMessage(EventId = 2000, Level = LogLevel.Error, Message = "Unhandled exception, answered with {ErrorCode} for {Method}")]
    private static partial void LogUnhandled(ILogger logger, string errorCode, string method, Exception exception);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Request rejected with {ErrorCode} for {Method}")]
    private static partial void LogRejected(ILogger logger, string errorCode, string method, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Client aborted {Method} request")]
    private static partial void LogClientAborted(ILogger logger, string method);
}
