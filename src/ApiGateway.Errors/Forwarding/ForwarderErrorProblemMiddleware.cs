using ApiGateway.Abstractions.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Errors.Forwarding;

/// <summary>
/// Proxy-pipeline middleware: after the forwarder ran, turns YARP's empty error responses (502/503/504/400/413)
/// into problem details. When the backend's response had already started streaming nothing can be written; the
/// failure is logged and YARP aborts the connection.
/// </summary>
public sealed partial class ForwarderErrorProblemMiddleware(
    RequestDelegate next,
    IProblemResponseWriter writer,
    IOptions<ErrorsOptions> options,
    ILogger<ForwarderErrorProblemMiddleware> logger)
{
    private readonly TimeSpan _noHealthyDestinationRetryAfter = options.Value.NoHealthyDestinationRetryAfter;

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        var failure = context.Features.Get<IForwarderErrorFeature>();
        if (failure is null || failure.Error == ForwarderError.None)
        {
            return;
        }

        var classification = ForwarderErrorClassifier.Classify(failure.Error, failure.Exception);
        if (classification.ClientGone)
        {
            LogClientGone(logger, failure.Error);
            return;
        }

        if (classification.Error is not { } error)
        {
            return;
        }

        // The exception is logged (it may name hosts and addresses), never returned.
        LogForwarderError(logger, failure.Error, error.Code, failure.Exception);

        if (context.Response.HasStarted)
        {
            return;
        }

        var writeOptions = error == GatewayErrors.NoHealthyDestination
            ? new ProblemWriteArgs { RetryAfter = _noHealthyDestinationRetryAfter }
            : null;
        await writer.WriteAsync(context, error, writeOptions);
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Warning, Message = "Forwarding failed with {ForwarderError}; answered with {ErrorCode}")]
    private static partial void LogForwarderError(ILogger logger, ForwarderError forwarderError, string errorCode, Exception? exception);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Debug, Message = "Forwarding stopped because the client went away ({ForwarderError})")]
    private static partial void LogClientGone(ILogger logger, ForwarderError forwarderError);
}
