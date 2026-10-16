using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Http;
using ApiGateway.Proxy.Listeners;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Normalization;

/// <summary>
/// On the public listener, rejects (400 <c>gateway.bad_request</c>) requests whose <c>Host</c>, or any
/// <c>X-Forwarded-Host</c> value, is not in <see cref="RoutingOptions.AllowedHosts"/>. Runs before routing, so
/// nothing downstream (routing, redirects, <c>Location</c> rewriting, logs) ever works with an unexpected host.
/// The internal listener is not checked: probes and scrapers address it by pod IP.
/// </summary>
public sealed partial class HostValidationMiddleware(
    RequestDelegate next,
    IOptions<RoutingOptions> routing,
    IOptions<ListenerOptions> listeners,
    IProblemResponseWriter writer,
    ILogger<HostValidationMiddleware> logger)
{
    private readonly HostPatterns _allowed = new(routing.Value.AllowedHosts);
    private readonly ListenerOptions _listeners = listeners.Value;

    public Task InvokeAsync(HttpContext context)
    {
        if (_listeners.IsInternal(context) || IsAllowed(context.Request, _allowed))
        {
            return next(context);
        }

        LogRejected(logger);
        return writer.WriteAsync(context, GatewayErrors.BadRequest).AsTask();
    }

    internal static bool IsAllowed(HttpRequest request, HostPatterns allowed)
    {
        if (!allowed.Matches(request.Host))
        {
            return false;
        }

        foreach (var value in request.Headers[GatewayHeaders.XForwardedHost])
        {
            foreach (var host in (value ?? "").Split(',', StringSplitOptions.TrimEntries))
            {
                if (!allowed.Matches(HostString.FromUriComponent(host)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Request rejected: Host or X-Forwarded-Host is not an allowed host")]
    private static partial void LogRejected(ILogger logger);
}
