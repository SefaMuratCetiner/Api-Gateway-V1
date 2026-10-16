using ApiGateway.Abstractions.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Limits;

/// <summary>
/// Enforces soft URL and header limits so oversized requests get a 414/431 problem body. Kestrel's own limits
/// stay higher and act as the backstop (with an empty body) for requests too large to reach middleware at all.
/// </summary>
public sealed class ProtocolLimitsMiddleware(RequestDelegate next, IProblemResponseWriter writer, IOptions<RequestLimitsOptions> options)
{
    private readonly ProtocolLimitsOptions _limits = options.Value.Protocol;

    public Task InvokeAsync(HttpContext context)
    {
        var error = Check(context, _limits);
        return error is null ? next(context) : writer.WriteAsync(context, error).AsTask();
    }

    internal static GatewayError? Check(HttpContext context, ProtocolLimitsOptions limits)
    {
        var request = context.Request;

        // The raw target is what the client sent (before decoding), which is what URL limits are about.
        var target = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        var urlLength = !string.IsNullOrEmpty(target)
            ? target.Length
            : request.PathBase.Value?.Length + request.Path.Value?.Length + request.QueryString.Value?.Length ?? 0;
        if (urlLength > limits.MaxUrlLength)
        {
            return GatewayErrors.UriTooLong;
        }

        if (request.Headers.Count > limits.MaxRequestHeaderCount)
        {
            return GatewayErrors.HeadersTooLarge;
        }

        long total = 0;
        foreach (var (name, values) in request.Headers)
        {
            total += name.Length + 4; // ": " and CRLF
            foreach (var value in values)
            {
                total += value?.Length ?? 0;
            }

            if (total > limits.MaxRequestHeadersTotalSize)
            {
                return GatewayErrors.HeadersTooLarge;
            }
        }

        return null;
    }
}
